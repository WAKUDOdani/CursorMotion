namespace CursorMotion;

// Direct raster for lower-DPI monitors. Render the vectors on the
// destination grid, declare that grid's DPI, and retain all physical motion
// coordinates. This is NOT the rejected global96/NONE scaling experiment.
internal static class CursorTargetRaster
{
    static string Mode => Environment.GetEnvironmentVariable("CURSORMOTION_TARGET_RASTER") ?? "native";
    internal static bool NativeCoordinates => Mode == "native";
    internal static bool Enabled => (Mode == "1" || NativeCoordinates)
        && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    internal static float Scale(int primaryPixels, int targetPixels) =>
        primaryPixels > 0 && targetPixels > 0 ? targetPixels / (float)primaryPixels : 1;
    internal static Settings Geometry(Settings original, float ratio)
    {
        CursorAppearance Look(int shape) => original.AppearanceFor(shape) with { Scale = original.AppearanceFor(shape).Scale * (original.AppearanceFor(shape).ExactSize ? 1 : ratio) };
        // Internal raster geometry may be below the UI's .5 minimum. Do not
        // validate/persist this derived render-only copy as a user profile.
        return original with { Scale = original.Scale * ratio, Arrow = Look(0), Text = Look(1), Wait = Look(2), Hand = Look(3) };
    }
    internal static RenderFrame Frame(RenderFrame frame, float ratio) => frame with { Scale = frame.Scale * ratio };
    internal static RenderFrame Frame(RenderFrame frame, Settings source, Settings target)
    {
        float Blend(Settings s) => (float)(s.AppearanceFor((int)frame.ShapeA).Scale * (1 - frame.Morph) + s.AppearanceFor((int)frame.ShapeB).Scale * frame.Morph);
        return Frame(frame, Blend(target) / Math.Max(.001f, Blend(source)));
    }
    internal readonly struct DpiScope : IDisposable
    {
        readonly uint previous;
        readonly nint previousAwareness;
        internal DpiScope(uint dpi)
        {
            previous = 0;
            previousAwareness = dpi is 96 or 1 ? Native.SetThreadDpiAwarenessContext(-1) : 0;
            if (dpi is 96 or 1 && previousAwareness == 0) throw Native.Error("Enter target cursor DPI context");
            try
            {
                previous = dpi == 0 ? 0 : Native.SetThreadCursorCreationScaling(dpi);
                if (dpi != 0 && previous == 0) throw Native.Error("Set target cursor raster DPI");
            }
            catch { if (previousAwareness != 0) Native.SetThreadDpiAwarenessContext(previousAwareness); throw; }
        }
        public void Dispose()
        {
            if (previous != 0) Native.SetThreadCursorCreationScaling(previous);
            if (previousAwareness != 0) Native.SetThreadDpiAwarenessContext(previousAwareness);
        }
    }
}
