using System.Numerics;

namespace CursorMotion;

internal sealed class ExposurePath
{
    internal const int Capacity = 16;
    internal readonly Vector2[] Points;
    internal readonly float Length, DurationMs;
    internal ExposurePath(Vector2[] points, float durationMs)
    {
        if (points.Length is < 2 or > Capacity) throw new ArgumentException("Exposure requires 2..16 knots");
        Points = points; DurationMs = durationMs;
        float length = 0;
        for (int i = 1; i < points.Length; i++) length += Vector2.Distance(points[i - 1], points[i]);
        Length = length;
    }
    internal Vector2 At(float t)
    {
        float p = Math.Clamp(t, 0, 1) * (Points.Length - 1);
        int i = Math.Min((int)p, Points.Length - 2);
        return Vector2.Lerp(Points[i], Points[i + 1], p - i);
    }
    internal bool Same(ExposurePath? other)
    {
        if (other == null || Points.Length != other.Points.Length) return false;
        for (int i = 0; i < Points.Length; i++) if (Vector2.DistanceSquared(Points[i], other.Points[i]) > .0001f) return false;
        return true;
    }
    internal (Vector2 Min, Vector2 Max) Bounds()
    {
        Vector2 min = Vector2.Zero, max = Vector2.Zero;
        foreach (var p in Points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return (min, max);
    }
    internal ExposurePath Recent(float fraction)
    {
        fraction = Math.Clamp(fraction, .01f, 1);
        var points = new Vector2[Points.Length];
        for (int i = 0; i < points.Length; i++) points[i] = At(1 - fraction + fraction * i / (points.Length - 1));
        return new(points, DurationMs * fraction);
    }
}
