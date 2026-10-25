using System.Numerics;

namespace CursorMotion;

internal static class MotionControlChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static RenderFrame[] Frames(Settings settings)
    {
        var simulation = new Simulation(); var history = new MotionHistory(); var builder = new NativeMotionBuilder();
        var frames = new RenderFrame[240];
        for (int i = 0; i < frames.Length; i++)
        {
            double time = i / 240d;
            var point = new Vector2(80 * MathF.Sin((float)time * 8), 50 * MathF.Cos((float)time * 6));
            history.Add(time, point);
            var pose = NativeCursorPresenter.Anchored(simulation.Step(1d / 240, new(point, true, 0, i is >= 50 and < 70), settings, nativeEffects: true));
            frames[i] = builder.Build(pose, history, time, point, settings, 1d / 240);
        }
        return frames;
    }
    internal static void Run()
    {
        var settings = new Settings { Smoothing = 0, Shutter = 0, Tilt = 4 };
        Require(Frames(settings with { Damping = .3 }).SequenceEqual(Frames(settings with { Damping = 1.5 })),
            "Damping unexpectedly affects the native cursor when smoothing is off");
        var a = Frames(settings with { Smoothing = 140, Damping = .3 });
        var b = Frames(settings with { Smoothing = 140, Damping = 1.5 });
        Require(a.Zip(b).Any(pair => Math.Abs(pair.First.Tilt - pair.Second.Tilt) > .001f), "Enabled tilt damping has no visible effect");
        Require(Frames(settings with { Smoothing = 140, Tilt = 0, Damping = .3 }).SequenceEqual(Frames(settings with { Smoothing = 140, Tilt = 0, Damping = 1.5 })),
            "Damping controls something other than tilt");
        Require(Frames(settings with { ClickScale = .5 }).Zip(Frames(settings with { ClickScale = 1 })).Any(pair => Math.Abs(pair.First.Scale - pair.Second.Scale) > .1f),
            "Click scale no longer affects the native pose");
        var history = new MotionHistory();
        for (int i = 0; i <= 1000; i++) history.Add(i / 1000d, new(i * 1.2f, 0));
        var pose = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 2, 0, 0, 0, 1, 0, 1, 1);
        RenderFrame Build(Settings current) => new NativeMotionBuilder().Build(pose, history, 1, new(1200, 0), current, 1d / 120);
        float Length(Settings current) => Build(current).Exposure?.Length ?? 0;
        settings = settings with { Shutter = 270, ShutterFps = 60 };
        Require(Length(settings with { Smoothing = 180 }) > Length(settings) * 2.7f, "Smoothing does not extend the new exposure");
        Require(Length(settings with { Shutter = 540 }) > Length(settings) * 1.9f, "Shutter does not control exposure length");
        Require(Length(settings with { ShutterFps = 120 }) < Length(settings) * .51f, "Exposure base no longer controls duration");
        Require(Build(settings with { Shutter = 0 }).Exposure == null, "Zero shutter did not disable motion blur");
        Require(Build(settings with { MaxSamples = 4 }).Samples == 4 && Build(settings with { MaxSamples = 128 }).Samples > 4,
            "Quality limit has no effect on temporal sampling");
    }
}
