using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace CursorMotion;

internal sealed unsafe class GpuRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct Constants
    {
        public Vector4 Box, Path0, Path1, Motion, Shape, Fill, Outline, Shadow, Look, Skins;
    }
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly ID3D11VertexShader vertex;
    readonly ID3D11PixelShader pixel;
    readonly ID3D11Buffer constantBuffer;
    readonly ID3D11RasterizerState rasterizer;
    ID3D11Texture2D? target, staging;
    ID3D11RenderTargetView? targetView;
    nint bitmap, oldBitmap, bits;
    readonly nint memoryDc;
    int capacityWidth, capacityHeight;
    CompositionPresenter? composition;
    public bool UsesComposition => composition != null;
    public long ReadbackCount { get; private set; }
    public void AttachComposition(nint hwnd) => composition = new(device, context, hwnd);
    static readonly Lazy<(byte[] Vertex, byte[] Pixel)> ShaderCode = new(() =>
    {
        using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorMotion.Cursor.hlsl")!);
        string source = reader.ReadToEnd();
        return (LoadShader(source, "Vertex", "VSMain", "vs_5_0"), LoadShader(source, "Pixel", "PSMain", "ps_5_0"));
    });
    static byte[] LoadShader(string source, string name, string entry, string target)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CursorMotion.Assets.{name}.cso");
        if (stream != null)
        {
            using var memory = new MemoryStream(); stream.CopyTo(memory); byte[] binary = memory.ToArray();
            if (binary.Length > 32 && binary.AsSpan(0, 32).SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(source)))) return binary[32..];
        }
        return Compile(source, entry, target);
    }
    internal static void SaveCompiledShaders(string directory)
    {
        Directory.CreateDirectory(directory);
        using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorMotion.Cursor.hlsl")!);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(reader.ReadToEnd()));
        File.WriteAllBytes(Path.Combine(directory, "Vertex.cso"), hash.Concat(ShaderCode.Value.Vertex).ToArray());
        File.WriteAllBytes(Path.Combine(directory, "Pixel.cso"), hash.Concat(ShaderCode.Value.Pixel).ToArray());
    }
    public string AdapterName { get; }
    public uint AdapterVendorId { get; }
    public bool RequiresCompactNativeCursor => NativeCursorLimits.RequiresCompact(AdapterVendorId);
    internal static bool AllowSoftwareForDiagnostics { get; set; }
    internal static bool ForceSoftwareForDiagnostics { get; set; }
    public GpuRenderer()
    {
        var result = D3D11CreateDevice(null, ForceSoftwareForDiagnostics ? DriverType.Warp : DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 }, out device!, out _, out context!);
        if (result.Failure && AllowSoftwareForDiagnostics && !ForceSoftwareForDiagnostics)
        {
            context?.Dispose(); device?.Dispose();
            result = D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0 }, out device!, out _, out context!);
        }
        result.CheckError();
        using (var dxgi = device.QueryInterface<IDXGIDevice>())
        using (var adapter = dxgi.GetAdapter())
        { var description = adapter.Description; AdapterName = description.Description; AdapterVendorId = description.VendorId; }
        vertex = device.CreateVertexShader(ShaderCode.Value.Vertex);
        pixel = device.CreatePixelShader(ShaderCode.Value.Pixel);
        constantBuffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)sizeof(Constants), BindFlags = BindFlags.ConstantBuffer, Usage = ResourceUsage.Default });
        rasterizer = device.CreateRasterizerState(new RasterizerDescription { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true });
        memoryDc = Native.CreateCompatibleDC(0);
        if (memoryDc == 0) throw Native.Error("CreateCompatibleDC");
    }
    static Vector4 Color(string text)
    {
        Settings.TryColor(text, out uint c);
        float a = (c >> 24) / 255f;
        return new(((c >> 16) & 255) / 255f * a, ((c >> 8) & 255) / 255f * a, (c & 255) / 255f * a, a);
    }
    static int RoundSize(int n) => (n + 63) / 64 * 64;
    void EnsureSize(int width, int height)
    {
        if (width <= capacityWidth && height <= capacityHeight) return;
        // Hard ceiling protects against bad coordinates or pathological desktop layouts.
        if (width > 8192 || height > 8192 || (long)width * height > 16_777_216) throw new InvalidOperationException("Слишком большая область размытия. Уменьшите выдержку или сглаживание.");
        context.OMSetRenderTargets(0, Array.Empty<ID3D11RenderTargetView>(), null);
        targetView?.Dispose(); target?.Dispose(); staging?.Dispose();
        if (bitmap != 0) { Native.SelectObject(memoryDc, oldBitmap); Native.DeleteObject(bitmap); bitmap = 0; }
        capacityWidth = Math.Max(capacityWidth, RoundSize(width)); capacityHeight = Math.Max(capacityHeight, RoundSize(height));
        if ((long)capacityWidth * capacityHeight > 16_777_216) { capacityWidth = RoundSize(width); capacityHeight = RoundSize(height); }
        Texture2DDescription description = new() { Width = (uint)capacityWidth, Height = (uint)capacityHeight, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget };
        target = device.CreateTexture2D(description); targetView = device.CreateRenderTargetView(target);
        staging = null;
    }
    void Readback(int width, int height)
    {
        if (staging == null)
        {
        var description = target!.Description;
        description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None; description.CPUAccessFlags = CpuAccessFlags.Read;
        staging = device.CreateTexture2D(description);
        Native.BitmapInfo info = new() { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = capacityWidth, Height = -capacityHeight, Planes = 1, BitCount = 32 };
        bitmap = Native.CreateDIBSection(memoryDc, ref info, 0, out bits, 0, 0);
        if (bitmap == 0) throw Native.Error("CreateDIBSection");
        oldBitmap = Native.SelectObject(memoryDc, bitmap);
        }
        context.CopySubresourceRegion(staging!, 0, 0, 0, 0, target!, 0, new Vortice.Mathematics.Box(0, 0, 0, width, height, 1));
        var mapped = context.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (int row = 0; row < height; row++)
                Buffer.MemoryCopy((byte*)mapped.DataPointer + row * mapped.RowPitch, (byte*)bits + row * capacityWidth * 4, capacityWidth * 4, width * 4);
        }
        finally { context.Unmap(staging!, 0); }
        ReadbackCount++;
    }
    void DrawFrame(RenderFrame f, Settings s, Native.Rect bounds)
    {
        int width = bounds.Width, height = bounds.Height;
        var a = s.AppearanceFor((int)f.ShapeA); var b = s.AppearanceFor((int)f.ShapeB);
        float Blend(double x, double y) => (float)(x + (y - x) * f.Morph);
        Constants c = new()
        {
            Box = new(bounds.Left, bounds.Top, width, height), Path0 = new(f.Start.X, f.Start.Y, f.Control.X, f.Control.Y),
            Path1 = new(f.End.X, f.End.Y, f.Scale, f.Samples), Motion = new(MathF.Cos(f.Tilt), MathF.Sin(f.Tilt), (float)s.TrailFalloff, f.Opacity),
            Shape = new(f.ShapeA, f.ShapeB, f.Morph, f.Animation),
            Fill = Vector4.Lerp(Color(a.FillColor), Color(b.FillColor), f.Morph),
            Outline = Vector4.Lerp(Color(a.OutlineColor), Color(b.OutlineColor), f.Morph),
            Shadow = Vector4.Lerp(Color(a.ShadowColor), Color(b.ShadowColor), f.Morph),
            Look = new(Blend(a.OutlineWidth, b.OutlineWidth), Blend(a.ShadowBlur, b.ShadowBlur), Blend(a.ShadowX, b.ShadowX), Blend(a.ShadowY, b.ShadowY)),
            Skins = new(a.Skin, b.Skin, f.LockHead ? 1 : 0, 0)
        };
        context.UpdateSubresource(constantBuffer, 0, null, (nint)(&c), 0, 0);
        context.Draw(3, 0);
    }
    public void Render(RenderFrame f, Settings s, Native.Rect bounds)
    {
        int width = bounds.Width, height = bounds.Height;
        EnsureSize(width, height);
        context.OMSetRenderTargets(targetView!);
        context.RSSetState(rasterizer);
        context.RSSetViewport(0, 0, width, height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vertex); context.PSSetShader(pixel); context.PSSetConstantBuffer(0, constantBuffer);
        context.OMSetBlendState(null);
        // Restore the user's preferred continuous SDF shape morph. Native
        // canvas/hotspot stability is handled independently by Bounds.
        DrawFrame(f, s, bounds);
        if (composition == null) Readback(width, height);
    }
    public void Present(nint hwnd, Native.Rect bounds)
    {
        if (composition != null) { composition.Present(target!, bounds); return; }
        Native.Point dest = new(bounds.Left, bounds.Top), source = new(0, 0);
        Native.Size size = new(bounds.Width, bounds.Height);
        Native.Blend blend = new() { Alpha = 255, Format = 1 };
        if (!Native.UpdateLayeredWindow(hwnd, 0, ref dest, ref size, memoryDc, ref source, 0, ref blend, 2)) throw Native.Error("UpdateLayeredWindow");
    }
    public byte[] Pixels(int width, int height)
    {
        if (composition != null) Readback(width, height);
        byte[] result = new byte[width * height * 4];
        for (int y = 0; y < height; y++) Marshal.Copy(bits + y * capacityWidth * 4, result, y * width * 4, width * 4);
        return result;
    }
    public void SavePng(string path, int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, Pixels(width, height), width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    internal static float AppearanceMargin(float scale, CursorAppearance a, CursorAppearance b) =>
        AppearanceMargin(scale, Math.Max(a.OutlineWidth, b.OutlineWidth), Math.Max(a.ShadowBlur, b.ShadowBlur), Math.Max(Math.Abs(a.ShadowX), Math.Abs(b.ShadowX)), Math.Max(Math.Abs(a.ShadowY), Math.Abs(b.ShadowY)));
    internal static float AppearanceMargin(float scale, double outline, double softness, double x, double y) =>
        (float)((24 + outline + x + y + softness * 2) * scale + 2);
    public static Native.Rect Bounds(RenderFrame f, Settings s, Native.Rect region)
    {
        var a = s.AppearanceFor((int)f.ShapeA); var b = s.AppearanceFor((int)f.ShapeB);
        float margin = AppearanceMargin(f.Scale, a, b);
        Vector2 min = Vector2.Min(Vector2.Min(f.Start, f.Control), f.End) - new Vector2(margin);
        Vector2 max = Vector2.Max(Vector2.Max(f.Start, f.Control), f.End) + new Vector2(margin);
        return new() { Left = Math.Max(region.Left, (int)MathF.Floor(min.X)), Top = Math.Max(region.Top, (int)MathF.Floor(min.Y)),
            Right = Math.Min(region.Right, (int)MathF.Ceiling(max.X)), Bottom = Math.Min(region.Bottom, (int)MathF.Ceiling(max.Y)) };
    }
    public void Dispose()
    {
        composition?.Dispose(); context.ClearState(); targetView?.Dispose(); target?.Dispose(); staging?.Dispose(); rasterizer.Dispose();
        constantBuffer.Dispose(); vertex.Dispose(); pixel.Dispose(); context.Dispose(); device.Dispose();
        if (bitmap != 0) { Native.SelectObject(memoryDc, oldBitmap); Native.DeleteObject(bitmap); }
        Native.DeleteDC(memoryDc);
    }

    [ComImport, Guid("8BA5FB08-5195-40E2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBlob { [PreserveSig] nint GetBufferPointer(); [PreserveSig] nuint GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int D3DCompile(byte[] source, nuint length, string sourceName, nint defines, nint include, string entry, string target, uint flags, uint effectFlags, out IBlob? code, out IBlob? errors);
    static byte[] Compile(string source, string entry, string profile)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        int hr = D3DCompile(utf8, (nuint)utf8.Length, "Cursor.hlsl", 0, 0, entry, profile, 1u << 15, 0, out var code, out var errors);
        try
        {
            if (hr < 0) throw new InvalidOperationException("Ошибка HLSL: " + (errors == null ? $"0x{hr:X}" : Marshal.PtrToStringAnsi(errors.GetBufferPointer())));
            byte[] result = new byte[(int)code!.GetBufferSize()]; Marshal.Copy(code.GetBufferPointer(), result, 0, result.Length); return result;
        }
        finally { if (code != null) Marshal.ReleaseComObject(code); if (errors != null) Marshal.ReleaseComObject(errors); }
    }
}
