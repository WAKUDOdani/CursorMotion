using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace CursorMotion;

// MANUAL/CONSENT-ONLY diagnostic. NEVER called by the engine, self-tests or CI.
// Desktop Duplication briefly acquires a frame to access POINTER SHAPE metadata.
// Its desktop texture is immediately discarded, never mapped, copied or saved.
internal static class PointerShapeProbe
{
    [DllImport("shcore.dll")] static extern int GetScaleFactorForMonitor(nint monitor, out int scale);
    [StructLayout(LayoutKind.Sequential)] struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(nint bitmap, int size, out Bitmap info);
    static void SavePng(string path, byte[] pixels, int width, int height, int stride, bool premultiplied)
    {
        var image = BitmapSource.Create(width, height, 96, 96, premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    static object WindowsCursor(string output)
    {
        var ci = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
        if (!Native.GetCursorInfo(ref ci) || !Native.GetIconInfo(ci.Cursor, out var info)) return new { available = false };
        try
        {
            if (info.Color == 0 || GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0) return new { available = false };
            int width = bitmap.Width, height = Math.Abs(bitmap.Height);
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048) throw new InvalidDataException("Invalid cursor bitmap");
            nint dc = Native.CreateCompatibleDC(0), color = 0, previous = 0;
            try
            {
                var dib = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
                color = Native.CreateDIBSection(dc, ref dib, 0, out var bits, 0, 0); previous = Native.SelectObject(dc, color);
                byte[] pixels = new byte[width * height * 4]; Marshal.Copy(pixels, 0, bits, pixels.Length);
                if (!Native.DrawIconEx(dc, 0, 0, ci.Cursor, width, height, 0, 0, 3)) throw Native.Error("Draw private cursor sample");
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                SavePng(Path.Combine(output, "windows-cursor.png"), pixels, width, height, width * 4, true);
            }
            finally { if (previous != 0) Native.SelectObject(dc, previous); if (color != 0) Native.DeleteObject(color); Native.DeleteDC(dc); }
            return new { available = true, width, height, bitmap.WidthBytes, info.HotX, info.HotY, ci.Flags };
        }
        finally { if (info.Color != 0) Native.DeleteObject(info.Color); if (info.Mask != 0) Native.DeleteObject(info.Mask); }
    }
    public static void Run(string output, bool waitSecondary = false)
    {
        if (waitSecondary)
        {
            var secondary = Native.Displays().FirstOrDefault(d => !d.Primary)
                ?? throw new InvalidOperationException("No secondary monitor found");
            var deadline = DateTime.UtcNow.AddSeconds(45);
            bool found = false;
            while (DateTime.UtcNow < deadline)
            {
                var cursor = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
                if (Native.GetCursorInfo(ref cursor) && cursor.Flags == 1 && secondary.Bounds.Contains(cursor.Position))
                { found = true; break; }
                Thread.Sleep(100);
            }
            if (!found) throw new TimeoutException("Cursor did not enter the secondary monitor; no desktop frame acquired");
        }
        Directory.CreateDirectory(output);
        var windows = WindowsCursor(output);
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 }, out var device, out _, out var context).CheckError();
        using (device) using (context)
        using (var dxgi = device.QueryInterface<IDXGIDevice>())
        using (var adapter = dxgi.GetAdapter())
        {
            var records = new List<object>();
            for (uint index = 0; index < 8; index++)
            {
                if (adapter.EnumOutputs(index, out var display).Failure) break;
                using (display)
                {
                    var description = display.Description;
                    GetScaleFactorForMonitor(description.Monitor, out int scale);
                    try
                    {
                        using var output1 = display.QueryInterface<IDXGIOutput1>();
                        using var duplication = output1.DuplicateOutput(device);
                        var result = duplication.AcquireNextFrame(500, out var frame, out var desktop);
                        if (result.Failure) { desktop?.Dispose(); records.Add(new { index, description.DeviceName, scale, error = result.Code }); continue; }
                        try
                        {
                            // NO QueryInterface<Texture2D>, staging copy, Map or screenshot.
                            desktop.Dispose();
                            if (frame.PointerShapeBufferSize == 0) { records.Add(new { index, scale, shapeBytes = 0u }); continue; }
                            if (frame.PointerShapeBufferSize > 16 * 1024 * 1024) throw new InvalidDataException("Pointer data exceeds diagnostic budget");
                            byte[] buffer = new byte[frame.PointerShapeBufferSize]; var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                            try
                            {
                                duplication.GetFramePointerShape((uint)buffer.Length, pinned.AddrOfPinnedObject(), out uint used, out var shape).CheckError();
                                if (used > buffer.Length) throw new InvalidDataException("Pointer buffer changed size");
                                File.WriteAllBytes(Path.Combine(output, $"output-{index}-cursor.bin"), buffer);
                                bool color = (uint)shape.Type == 2;
                                bool layout = color && shape.Width > 0 && shape.Height > 0 && shape.Pitch >= shape.Width * 4 && (ulong)shape.Pitch * shape.Height <= (ulong)buffer.Length;
                                if (layout) SavePng(Path.Combine(output, $"output-{index}-cursor.png"), buffer, (int)shape.Width, (int)shape.Height, (int)shape.Pitch, false);
                                records.Add(new { index, description.DeviceName, scale, type = (uint)shape.Type, shape.Width, shape.Height, shape.Pitch, shape.HotSpot, used, bufferBytes = buffer.Length, validColorLayout = layout });
                            }
                            finally { pinned.Free(); }
                        }
                        finally { duplication.ReleaseFrame(); }
                    }
                    catch (Exception ex) { records.Add(new { index, description.DeviceName, scale, error = ex.Message }); }
                }
            }
            File.WriteAllText(Path.Combine(output, "pointer-shapes.json"), JsonSerializer.Serialize(new { desktopImageRead = false, desktopImageSaved = false, cursorOnly = true, windows, records }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
    }
}
