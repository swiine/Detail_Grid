namespace CoWork.Core;

/// <summary>
/// The "Drafted in CAD" Easter egg: the timeline (when each point is picked, each key typed, when it hatches) and its
/// sound track, made from the same times so the clicks land exactly on the picks. The window that draws it is in the
/// add-in (EasterEgg); this part has no drawing so it can be tested.
/// </summary>
public static class DraftedScript
{
    public const double Zoom = 0.6;          // ZOOM EXTENTS at the start
    public const double StartA = 0.75;       // first pick of the first bracket
    public const double Seg = 0.22;          // time per polyline segment
    public const double Gap = 0.35;          // between the two polylines (Enter, PLINE again)
    public static double StartC => StartA + 6 * Seg + Gap;
    public static double DrawnAt => StartC + 6 * Seg;
    public static double HatchTyped => DrawnAt + 0.08;
    public static double PickA => HatchTyped + 0.42;
    public static double PickC => PickA + 0.12;
    public const double HatchSweep = 0.5;
    public static double Final => PickC + HatchSweep + 0.12;  // "_CoWork", the regen flash and the chime
    public static double Close => Final + 2.1;

    /// <summary>The logo's two outlines, in 16ths (the Interlock mark), each closed.</summary>
    public static readonly (double X, double Y)[] BracketA = { (3, 3), (10, 3), (10, 5.3), (5.3, 5.3), (5.3, 10), (3, 10), (3, 3) };
    public static readonly (double X, double Y)[] BracketC = { (13, 13), (6, 13), (6, 10.7), (10.7, 10.7), (10.7, 6), (13, 6), (13, 13) };

    /// <summary>Times of the typed keys: (time, isEnter).</summary>
    public static List<(double T, bool Enter)> Keys()
    {
        var k = new List<(double, bool)>();
        void Type(string s, double t0, double each, bool enter)
        {
            for (int i = 0; i < s.Length; i++) k.Add((t0 + i * each, false));
            if (enter) k.Add((t0 + s.Length * each + 0.06, true));
        }
        Type("PLINE", 0.08, 0.075, true);
        k.Add((StartA + 6 * Seg + 0.1, true));               // C (close) ...
        k.Add((StartA + 6 * Seg + 0.2, true));               // ... and Enter again for the next PLINE
        Type("HATCH", HatchTyped, 0.06, true);
        Type("_CoWork", Final - 0.42, 0.05, false);
        return k;
    }

    /// <summary>Times of the mouse picks (every corner, then inside each bracket for the hatch).</summary>
    public static List<double> Picks()
    {
        var p = new List<double>();
        for (int i = 0; i <= 6; i++) p.Add(StartA + i * Seg);
        for (int i = 0; i <= 6; i++) p.Add(StartC + i * Seg);
        p.Add(PickA);
        p.Add(PickC);
        return p;
    }

    /// <summary>What the command line says at a moment (the line above, and the one being typed).</summary>
    public static (string Above, string Now) CommandLine(double t)
    {
        static string Typed(string s, double t, double t0, double each) => s[..Math.Clamp((int)((t - t0) / each) + 1, 0, s.Length)];
        if (t < 0.08) return ("", "Command:");
        if (t < StartA) return ("", "Command: " + Typed("PLINE", t, 0.08, 0.075));
        if (t < StartC - 0.05)
            return ("Command: PLINE", t < StartA + 0.05 ? "Specify start point:" : "Specify next point or [Arc/Halfwidth/Length/Undo/Width]:");
        if (t < DrawnAt + 0.05) return ("Command: PLINE", "Specify next point or [Arc/Close/Halfwidth/Length/Undo/Width]:");
        if (t < PickA - 0.05) return ("Command:", "Command: " + Typed("HATCH", t, HatchTyped, 0.06));
        if (t < Final - 0.42) return ("Command: HATCH", "Pick internal point or [Select objects/Undo/seTtings]:");
        if (t < Final) return ("Command: HATCH", "Command: " + Typed("_CoWork", t, Final - 0.42, 0.05));
        return ("Command: _CoWork", "Made with a lot of coffee.");
    }

    // ------------------------------------------------------------------ sound

    /// <summary>The whole sound track as a WAV (clicks, keys, the hatch whoosh and the chime), in time with the animation.</summary>
    public static byte[] Wav(int rate = 22050, double volume = 0.5)
    {
        int n = (int)(rate * (Close + 0.3));
        var buf = new double[n];
        var rnd = new Random(7);

        void Add(double t0, double seconds, Func<double, double> f)
        {
            int s0 = (int)(t0 * rate);
            for (int i = 0; i < (int)(seconds * rate) && s0 + i < n; i++)
                if (s0 + i >= 0) buf[s0 + i] += f((double)i / rate);
        }

        // Mouse pick: a short plastic click plus a tiny "snap" blip.
        foreach (double t in Picks())
        {
            Add(t, 0.02, x => (rnd.NextDouble() * 2 - 1) * Math.Exp(-x * 400) * 0.55 + Math.Sin(2 * Math.PI * 2600 * x) * Math.Exp(-x * 250) * 0.35);
            Add(t + 0.012, 0.05, x => Math.Sin(2 * Math.PI * 3400 * x) * Math.Exp(-x * 90) * 0.10);
        }
        // Keys: softer, lower clacks; Enter is a bit heavier.
        foreach (var (t, enter) in Keys())
        {
            double pitch = enter ? 1100 : 1500 + rnd.Next(-150, 150);
            double amp = enter ? 0.45 : 0.28;
            Add(t, 0.03, x => ((rnd.NextDouble() * 2 - 1) * 0.6 + Math.Sin(2 * Math.PI * pitch * x) * 0.4) * Math.Exp(-x * (enter ? 160 : 260)) * amp);
        }
        // Hatch: a rising, airy whoosh across the sweep.
        double h0 = PickC, hs = HatchSweep + 0.15;
        double lp = 0;
        Add(h0, hs, x =>
        {
            double u = x / hs, env = Math.Sin(Math.PI * u);
            lp += 0.08 * ((rnd.NextDouble() * 2 - 1) - lp); // soft noise
            return (lp * 1.6 + Math.Sin(2 * Math.PI * (280 + 700 * u) * x) * 0.25) * env * 0.35;
        });
        // Chime: four bell notes (C E G C), each ringing out.
        double[] notes = { 1046.5, 1318.5, 1568.0, 2093.0 };
        for (int i = 0; i < notes.Length; i++)
        {
            double f = notes[i];
            Add(Final + i * 0.085, 1.4, x => (Math.Sin(2 * Math.PI * f * x) + 0.35 * Math.Sin(2 * Math.PI * f * 2.01 * x)) * Math.Exp(-x * 3.2) * 0.22);
        }

        double peak = buf.Max(Math.Abs);
        double scale = peak > 0 ? volume / peak : 0;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        foreach (double v in buf) w.Write((short)Math.Clamp(v * scale * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
