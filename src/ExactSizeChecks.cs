using System.Numerics;

namespace CursorMotion;

internal static class ExactSizeChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run()
    {
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        {
            int previousExtent = 0;
            foreach (double scale in new[] { 1, 2.8, 3.2, 4, Settings.MaximumCursorScale })
            {
                var settings = CursorPacks.Apply(new Settings(), pack.Id);
                for (int i = 0; i < 4; i++) settings = settings.WithAppearance(i, settings.AppearanceFor(i) with { Scale = scale, ExactSize = true });
                Require(CursorPacks.Apply(settings, pack.Id).AppearanceFor(0).ExactSize, "Pack application discarded exact sizing");
                var geometry = CursorTargetRaster.Geometry(settings, 2f / 3);
                var pose = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, (float)scale, 0, shape, shape, 1, .3f, 1, 1, true);
                Require(CursorTargetRaster.Frame(pose, settings, geometry).Scale == pose.Scale, "Exact size changed between monitor raster grids");
                var budget = new NativeFrameBudget();
                var frame = budget.Constrain(pose, settings, geometry, 0);
                int selectedSide = new NativeSizeTransport().Select(gpu, presenter, settings, 0x10DE);
                var prepared = budget.Prepare(gpu, presenter, frame, geometry, new NativeMotionBuilder(), true, 0x10DE, selectedSide);
                Require(Math.Abs(prepared.Frame.Scale - scale) < .0001, "Visible exact size was silently reduced");
                int extent = Math.Max(presenter.RequiredWidth, presenter.RequiredHeight);
                Require(extent > previousExtent, "Increasing the size slider stopped enlarging the cursor"); previousExtent = extent;
                int expectedSide = selectedSide;
                Require(scale != 2.8 || selectedSide == 128, "Verified size left the NVIDIA-compatible transport");
                Require(prepared.Sprite.Width == expectedSide && prepared.Sprite.Height == expectedSide, "Unexpected experimental transport size");
                Require(prepared.Sprite.Width <= 256, "Unsafe oversized pointer transport");
                if (scale == Settings.MaximumCursorScale)
                {
                    var handles = presenter.CreatePublication(prepared.Sprite.Pixels, expectedSide, expectedSide, prepared.Sprite.HotX, prepared.Sprite.HotY, 1);
                    try { NativeCursorChecks.VerifyWindowsPixels(handles[0], prepared.Sprite); NativeCursorChecks.VerifyWindowsMask(handles[0], prepared.Sprite); }
                    finally { Native.DestroyCursor(handles[0]); }
                }
            }
        }
    }
}
