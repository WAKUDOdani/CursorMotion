using System.Numerics;

namespace CursorMotion;

// Select a stable transport once per appearance, using actual vector pixels.
// Small experimental increases can remain 128px when the whole glyph fits.
internal sealed class NativeSizeTransport
{
    Settings? previous;
    uint previousVendor;
    int side;
    internal int Select(GpuRenderer gpu, NativeCursorPresenter presenter, Settings settings, uint? vendorOverride = null)
    {
        uint vendor = vendorOverride ?? gpu.AdapterVendorId;
        if (!NativeCursorLimits.RequiresCompact(vendor)) return NativeCursorLimits.NativeSide;
        if (previous != null && previousVendor == vendor && NativeCursorTheme.SameAppearance(previous, settings) && (previous.Tilt == 0) == (settings.Tilt == 0)) return side;
        previous = settings; previousVendor = vendor; side = 128;
        float footprint = gpu.NativePixelFootprint;
        try
        {
            gpu.NativePixelFootprint = Math.Max(footprint, 1.5f);
            foreach (int shape in Enumerable.Range(0, 8))
            {
                int selected = settings.Style >= 0 ? settings.Style : shape;
                var look = settings.AppearanceFor(selected);
                if (!look.ExactSize) continue; // Legacy requests retain their safe fit.
                foreach (float tilt in settings.Tilt == 0 ? new[] { 0f } : new[] { 0f, -.524f, .524f })
                {
                    var frame = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, (float)look.Scale, tilt, selected, selected, 1, .3f, 1, 1, true);
                    var bounds = NativeCursorPresenter.Bounds(frame, settings);
                    gpu.Render(frame, settings, bounds);
                    if (presenter.PrepareFrame(gpu.Pixels(bounds.Width, bounds.Height), bounds, true, vendor, 128) == null) return side = 256;
                }
            }
        }
        finally { gpu.NativePixelFootprint = footprint; }
        return side;
    }
}
