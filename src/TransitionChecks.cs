using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class TransitionChecks
{
    public static void Run(string output, bool enforce)
    {
        Directory.CreateDirectory(output); using var gpu = new GpuRenderer();
        var settings = new Settings { Arrow = new() { Scale = 4.2 }, Text = new() { Scale = 1.6 }, Wait = new() { Scale = 1.6 }, Hand = new() { Scale = 1.6, MorphTime = 240 }, Shutter = 0, Tilt = 0 };
        var records = new List<object>(); bool valid = true;
        foreach (int from in Enumerable.Range(0,4))
        foreach (int to in Enumerable.Range(0,4).Where(target => target != from))
        {
            var sim = new Simulation(); sim.NativeReappeared(); RenderFrame frame = default;
            for (int i=0;i<90;i++) frame=sim.Step(1d/120,new(Vector2.Zero,true,from,false),settings,nativeEffects:true);
            var canvases = new HashSet<string>(); double minMass=double.MaxValue, maxMass=0; int empty=0, maxOriginalMorphError=0, reversalError=0;
            for(int i=0;i<65;i++)
            {
                frame=NativeCursorPresenter.Anchored(sim.Step(1d/120,new(Vector2.Zero,true,to,false),settings,nativeEffects:true));
                var bounds=NativeCursorPresenter.Bounds(frame,settings);canvases.Add($"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");
                gpu.Render(frame,settings,bounds);var pixels=gpu.Pixels(bounds.Width,bounds.Height);
                double mass=0;int solid=0;
                for(int p=3;p<pixels.Length;p+=4){mass+=pixels[p];if(pixels[p]>32)solid++;}
                if(frame.Morph>0&&frame.Morph<1)
                {
                    gpu.Render(frame with { LockHead=false },settings,bounds);
                    var original=gpu.Pixels(bounds.Width,bounds.Height);
                    for(int p=0;p<pixels.Length;p++)maxOriginalMorphError=Math.Max(maxOriginalMorphError,Math.Abs(pixels[p]-original[p]));
                }
                minMass=Math.Min(minMass,mass);maxMass=Math.Max(maxMass,mass);if(solid==0)empty++;
                if(i==7)
                {
                    gpu.Render(frame,settings,bounds);
                    gpu.SavePng(Path.Combine(output,$"transition-{from}-{to}.png"),bounds.Width,bounds.Height);
                    gpu.Render(frame with { ShapeA=frame.ShapeB, ShapeB=frame.ShapeA, Morph=1-frame.Morph }, settings, bounds);
                    var reversed=gpu.Pixels(bounds.Width,bounds.Height);
                    for(int p=0;p<pixels.Length;p++)reversalError=Math.Max(reversalError,Math.Abs(pixels[p]-reversed[p]));
                }
            }
            bool pass=canvases.Count==1&&empty==0&&maxOriginalMorphError==0&&reversalError<=2;valid&=pass;
            records.Add(new {from,to,nativeCanvasVariants=canvases.Count,emptyFrames=empty,minMass,maxMass,maxOriginalMorphError,reversalError,pass});
        }
        var threshold = new List<object>();
        foreach (var pack in CursorPacks.All)
        {
            var look = CursorPacks.Apply(new Settings { Scale = 4.2 }, pack.Id);
            var frame = NativeCursorPresenter.Anchored(new(Vector2.Zero, Vector2.Zero, Vector2.Zero, 4.2f, 0, 0, 0, 1, 0, 1, 1));
            var bounds = NativeCursorPresenter.Bounds(frame, look);
            gpu.Render(frame, look, bounds); var sharp = gpu.Pixels(bounds.Width, bounds.Height);
            gpu.Render(frame with { Start = new(-.51f,0), Control = new(-.255f,0), Samples = 2 }, look, bounds); var onset = gpu.Pixels(bounds.Width, bounds.Height);
            double sharpMass=0,onsetMass=0;
            for(int i=3;i<sharp.Length;i+=4){sharpMass+=sharp[i];onsetMass+=onset[i];}
            double ratio=onsetMass/sharpMass;bool pass=ratio is > .97 and < 1.03;valid&=pass;
            threshold.Add(new{material=pack.Name,alphaMassRatio=ratio,pass});
        }
        File.WriteAllText(Path.Combine(output,"transitions.json"),JsonSerializer.Serialize(new{pass=valid,records,exposureOnset=threshold,noGlobalChanges=true},new JsonSerializerOptions{WriteIndented=true}));
        if(enforce&&!valid)throw new InvalidOperationException("Shape transitions resize the native canvas/hotspot or erase the icon");
    }
}
