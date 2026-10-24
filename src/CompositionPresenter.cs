using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CursorMotion;

internal sealed class CompositionPresenter : IDisposable
{
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly IDCompositionDevice composition;
    readonly IDCompositionTarget target;
    readonly IDCompositionVisual visual;
    readonly nint hwnd;
    IDXGISwapChain1? swapChain;
    ID3D11Texture2D? buffer;
    ID3D11RenderTargetView? view;
    int width, height;
    Native.Rect previous;
    public CompositionPresenter(ID3D11Device device, ID3D11DeviceContext context, nint hwnd)
    {
        this.device = device; this.context = context; this.hwnd = hwnd;
        long style = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
        if ((style & Native.NoRedirectionBitmap) == 0 || (style & Native.LayeredWindow) != 0)
            throw new InvalidOperationException("DirectComposition requires NOREDIRECTIONBITMAP without a legacy layered surface.");
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        try
        {
            composition.CreateTargetForHwnd(hwnd, true, out target!).CheckError();
            composition.CreateVisual(out visual!).CheckError();
            target.SetRoot(visual).CheckError();
            composition.Commit().CheckError();
        }
        catch { visual?.Dispose(); target?.Dispose(); composition.Dispose(); throw; }
    }
    void EnsureSize(int requiredWidth, int requiredHeight)
    {
        if (requiredWidth <= width && requiredHeight <= height) return;
        context.OMSetRenderTargets(0, Array.Empty<ID3D11RenderTargetView>(), null);
        view?.Dispose(); buffer?.Dispose();
        width = Math.Max(width, (requiredWidth + 63) / 64 * 64); height = Math.Max(height, (requiredHeight + 63) / 64 * 64);
        if ((long)width * height > 16_777_216) { width = (requiredWidth + 63) / 64 * 64; height = (requiredHeight + 63) / 64 * 64; }
        if (swapChain == null)
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgi.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            swapChain = factory.CreateSwapChainForComposition(device, new SwapChainDescription1
            {
                Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new(1, 0), BufferUsage = Usage.RenderTargetOutput, BufferCount = 2,
                Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential, AlphaMode = AlphaMode.Premultiplied
            });
            visual.SetContent(swapChain).CheckError(); composition.Commit().CheckError();
        }
        else swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        buffer = swapChain.GetBuffer<ID3D11Texture2D>(0); view = device.CreateRenderTargetView(buffer);
    }
    public void Present(ID3D11Texture2D source, Native.Rect bounds)
    {
        EnsureSize(bounds.Width, bounds.Height);
        context.OMSetRenderTargets(0, Array.Empty<ID3D11RenderTargetView>(), null);
        context.ClearRenderTargetView(view!, new Color4(0, 0, 0, 0));
        context.CopySubresourceRegion(buffer!, 0, 0, 0, 0, source, 0, new Box(0, 0, 0, bounds.Width, bounds.Height, 1));
        swapChain!.Present(0, PresentFlags.None).CheckError();
        if (!previous.Equals(bounds))
        {
            if (!Native.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x4 | 0x10)) throw Native.Error("SetWindowPos");
            previous = bounds;
        }
    }
    public void Dispose()
    {
        context.OMSetRenderTargets(0, Array.Empty<ID3D11RenderTargetView>(), null);
        view?.Dispose(); buffer?.Dispose(); swapChain?.Dispose(); visual.Dispose(); target.Dispose(); composition.Dispose();
    }
}
