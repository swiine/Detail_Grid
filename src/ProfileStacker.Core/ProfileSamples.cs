namespace ProfileStacker.Core;

/// <summary>
/// Station/elevation points of the profiles drawn in the long view (PVIs plus regular samples), read once
/// so every plan update can find the lowest and highest point in any station range without the drawing.
/// </summary>
public sealed class ProfileSamples
{
    private readonly List<(double Station, double Elevation)[]> _lines = new();

    public int ProfileCount => _lines.Count;

    /// <summary>Adds one profile's points; they are sorted by station and duplicates dropped.</summary>
    public void Add(IEnumerable<(double Station, double Elevation)> points)
    {
        var line = points
            .Where(p => double.IsFinite(p.Station) && double.IsFinite(p.Elevation))
            .OrderBy(p => p.Station)
            .ToArray();
        if (line.Length > 0)
            _lines.Add(line);
    }

    /// <summary>
    /// Lowest and highest elevation of all profiles between two stations, including the interpolated
    /// elevations at the two ends. Null when no profile has data in the range.
    /// </summary>
    public (double Low, double High)? Range(double start, double end)
    {
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (var line in _lines)
        {
            if (line[^1].Station < start || line[0].Station > end)
                continue;
            Include(At(line, Math.Max(start, line[0].Station)));
            Include(At(line, Math.Min(end, line[^1].Station)));
            foreach (var p in line)
                if (p.Station > start && p.Station < end)
                    Include(p.Elevation);
        }
        return double.IsInfinity(low) ? null : (low, high);

        void Include(double e)
        {
            if (e < low) low = e;
            if (e > high) high = e;
        }
    }

    private static double At((double Station, double Elevation)[] line, double station)
    {
        int i = Array.BinarySearch(line, (station, double.NegativeInfinity), Comparer<(double, double)>.Create((a, b) => a.Item1.CompareTo(b.Item1)));
        if (i >= 0)
        {
            // Several points can share a station (a vertical step); any of them is on the line.
            return line[i].Elevation;
        }
        i = ~i;
        if (i == 0) return line[0].Elevation;
        if (i >= line.Length) return line[^1].Elevation;
        var (s0, e0) = line[i - 1];
        var (s1, e1) = line[i];
        return e0 + (e1 - e0) * (station - s0) / (s1 - s0);
    }
}
