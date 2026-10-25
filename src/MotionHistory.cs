using System.Numerics;

namespace CursorMotion;

// Processed physical pointer positions only. Never changes Windows input.
internal sealed class MotionHistory
{
    const int Capacity = 4096;
    readonly object gate = new();
    readonly double[] times = new double[Capacity];
    readonly Vector2[] positions = new Vector2[Capacity];
    int next, count;
    internal bool Add(double time, Vector2 position)
    {
        lock (gate)
        {
            int last = (next + Capacity - 1) % Capacity;
            if (count > 0 && time < times[last]) return false;
            if (count > 0 && time == times[last]) { bool changed = positions[last] != position; positions[last] = position; return changed; }
            times[next] = time; positions[next] = position; next = (next + 1) % Capacity;
            count = Math.Min(Capacity, count + 1);
            return true;
        }
    }
    Vector2 At(double time, double now, Vector2 current)
    {
        if (count == 0) return current;
        int oldest = (next - count + Capacity) % Capacity;
        int last = (next - 1 + Capacity) % Capacity;
        if (time <= times[oldest]) return positions[oldest];
        if (time >= times[last])
        {
            float t = now > times[last] ? (float)Math.Clamp((time - times[last]) / (now - times[last]), 0, 1) : 1;
            return Vector2.Lerp(positions[last], current, t);
        }
        int low = 0, high = count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (times[(oldest + mid) % Capacity] <= time) low = mid; else high = mid;
        }
        int a = (oldest + low) % Capacity, b = (oldest + high) % Capacity;
        float fraction = (float)((time - times[a]) / Math.Max(.000001, times[b] - times[a]));
        return Vector2.Lerp(positions[a], positions[b], fraction);
    }
    internal ExposurePath? Exposure(double now, Vector2 current, double duration)
    {
        if (duration <= 0) return null;
        lock (gate)
        {
            if (count < 2) return null;
            var points = new Vector2[ExposurePath.Capacity];
            for (int i = 0; i < points.Length; i++)
                points[i] = At(now - duration * (1 - i / (double)(points.Length - 1)), now, current) - current;
            points[^1] = Vector2.Zero;
            var exposure = new ExposurePath(points, (float)(duration * 1000));
            return exposure.Length >= .35f ? exposure : null;
        }
    }
}
