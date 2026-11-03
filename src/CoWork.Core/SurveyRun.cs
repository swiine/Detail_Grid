namespace CoWork.Core;

/// <summary>
/// "Survey Run": the game behind the Easter egg. It's an endless runner: a survey instrument on its tripod runs along
/// the road, jumps cones, pegs and barriers, and ducks under drones. It speeds up as it goes. This is only the rules
/// (no drawing), so it can be tested; the window in the add-in draws it.
/// Units are screen pixels; Y is the height above the road (0 = on the road).
/// </summary>
public sealed class SurveyRun
{
    public enum Kind { Cone, Pegs, Barrier, DroneLow, DroneHigh }

    public sealed record Obstacle(Kind Kind, double X, double Y, double W, double H);

    public const double Width = 600;
    public const double PlayerX = 40;
    public const double StandW = 20, StandH = 24, DuckW = 26, DuckH = 14;
    public const double Gravity = 2600, JumpSpeed = 760, StartSpeed = 320, MaxSpeed = 820;

    private readonly Random _rnd;
    private readonly List<Obstacle> _obstacles = new();
    private double _nextGap;

    public SurveyRun(int seed = 0)
    {
        _rnd = seed == 0 ? new Random() : new Random(seed);
        _nextGap = 260;
    }

    public double Y { get; private set; }
    public double Vy { get; private set; }
    public bool Ducking { get; private set; }
    public bool Over { get; private set; }
    public double Speed { get; private set; } = StartSpeed;
    public double Distance { get; private set; }
    public double Time { get; private set; }
    public int Score => (int)(Distance / 10);
    public bool OnGround => Y <= 0;
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    /// <summary>Set for one step when the score passes a hundred (for a beep and a flash).</summary>
    public bool Milestone { get; private set; }
    /// <summary>Set for one step when a jump starts (for a sound).</summary>
    public bool Hopped { get; private set; }

    public double PlayerW => Ducking && OnGround ? DuckW : StandW;
    public double PlayerH => Ducking && OnGround ? DuckH : StandH;

    public void Jump()
    {
        if (Over || !OnGround) return;
        Vy = JumpSpeed;
        Hopped = true;
    }

    /// <summary>Letting go of jump early makes a shorter hop.</summary>
    public void Release()
    {
        if (Vy > 320) Vy = 320;
    }

    public void Duck(bool down) => Ducking = down;

    /// <summary>Adds an obstacle straight away (tests).</summary>
    public void Add(Kind kind, double x) => _obstacles.Add(Make(kind, x));

    public static Obstacle Make(Kind kind, double x) => kind switch
    {
        Kind.Cone => new(kind, x, 0, 14, 20),
        Kind.Pegs => new(kind, x, 0, 20, 24),
        Kind.Barrier => new(kind, x, 0, 32, 18),
        Kind.DroneLow => new(kind, x, 15, 24, 10),   // duck under it
        _ => new(kind, x, 44, 24, 10),               // DroneHigh: only hits you if you jump into it
    };

    public void Step(double dt)
    {
        Milestone = false;
        Hopped = false;
        if (Over || dt <= 0) return;
        dt = Math.Min(dt, 0.05);
        Time += dt;

        // Jumping and falling (ducking in the air drops you faster).
        if (!OnGround || Vy > 0)
        {
            Vy -= Gravity * (Ducking ? 2.2 : 1) * dt;
            Y += Vy * dt;
            if (Y <= 0) { Y = 0; Vy = 0; }
        }

        int before = Score;
        Distance += Speed * dt;
        Speed = Math.Min(MaxSpeed, StartSpeed + Distance * 0.018);
        if (Score / 100 > before / 100) Milestone = true;

        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle o = _obstacles[i] with { X = _obstacles[i].X - Speed * dt };
            if (o.X + o.W < 0) _obstacles.RemoveAt(i);
            else _obstacles[i] = o;
        }

        double lastEnd = _obstacles.Count == 0 ? double.NegativeInfinity : _obstacles.Max(o => o.X + o.W);
        if (Width - lastEnd >= _nextGap) Spawn();

        if (_obstacles.Any(Hits)) Over = true;
    }

    private void Spawn()
    {
        Kind kind;
        int roll = _rnd.Next(100);
        if (Score > 250 && roll < 22) kind = _rnd.Next(2) == 0 ? Kind.DroneLow : Kind.DroneHigh;
        else kind = (roll % 3) switch { 0 => Kind.Cone, 1 => Kind.Pegs, _ => Kind.Barrier };
        _obstacles.Add(Make(kind, Width + 10));
        // Room to land and jump again: the faster it goes, the further apart.
        double min = Speed * 0.62 + 90;
        _nextGap = min + _rnd.NextDouble() * (220 + Speed * 0.3);
    }

    /// <summary>Touching, with a little forgiveness at the edges.</summary>
    public bool Hits(Obstacle o)
    {
        const double give = 3;
        double px0 = PlayerX + give, px1 = PlayerX + PlayerW - give, py0 = Y + give, py1 = Y + PlayerH - give;
        double ox0 = o.X + give, ox1 = o.X + o.W - give, oy0 = o.Y + give, oy1 = o.Y + o.H - give;
        return px0 < ox1 && px1 > ox0 && py0 < oy1 && py1 > oy0;
    }

    // ------------------------------------------------------------------ pixel art

    /// <summary>
    /// Sprites, drawn at 2 screen pixels per art pixel. Letters: a = accent, b = second accent, t = text colour,
    /// d = dim, . = see-through - so they follow the colour scheme.
    /// </summary>
    public static class Art
    {
        public const int Scale = 2;

        // The instrument: telescope and yellow-green eyepiece on a tribrach, three tripod legs (two run frames).
        public static readonly string[] RunA =
        {
            "...tttt...",
            "...tbbtttt",
            "..tttttt..",
            "...tbbt...",
            "...tttt...",
            "....tt....",
            "...aaaa...",
            "...a.a.a..",
            "..a..a..a.",
            "..a..a...a",
            ".a...a....",
            ".a...a....",
        };

        public static readonly string[] RunB =
        {
            "...tttt...",
            "...tbbtttt",
            "..tttttt..",
            "...tbbt...",
            "...tttt...",
            "....tt....",
            "...aaaa...",
            "..a.a.a...",
            "..a.a..a..",
            ".a..a..a..",
            ".a..a...a.",
            "....a...a.",
        };

        public static readonly string[] Duck =
        {
            ".......tttt..",
            ".......tbbtttt",
            ".......tttt..",
            "....aaaaaa...",
            "..aa..a...aa.",
            ".a....a.....a",
            "a.....a......",
        };

        public static readonly string[] Bonked =
        {
            "..t.tttt.t",
            "...tbbtttt",
            "..tttttt..",
            "...t..t...",
            "...tttt...",
            "....tt....",
            "...aaaa...",
            "...a.a.a..",
            "..a..a..a.",
            "..a..a...a",
            ".a...a....",
            ".a...a....",
        };

        public static readonly string[] Cone =
        {
            "...a...",
            "...a...",
            "..ttt..",
            "..aaa..",
            "..aaa..",
            ".ttttt.",
            ".aaaaa.",
            ".aaaaa.",
            "aaaaaaa",
            "ddddddd",
        };

        // A survey peg with flagging tape (two of them make an obstacle).
        public static readonly string[] Peg =
        {
            "bbb.",
            "bbbb",
            "bt..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
            ".t..",
        };

        public static readonly string[] Barrier =
        {
            "aaaaaaaaaaaaaaaa",
            "attaattaattaatta",
            "aaaaaaaaaaaaaaaa",
            ".t............t.",
            ".t............t.",
            ".t............t.",
            ".t............t.",
            ".t............t.",
            "tt............tt",
        };

        // A survey drone, rotors spinning (two frames).
        public static readonly string[] DroneA =
        {
            "tttt....tttt",
            "..t......t..",
            "..tttttttt..",
            "...tbbbbt...",
            "....t..t....",
        };

        public static readonly string[] DroneB =
        {
            ".tt......tt.",
            "..t......t..",
            "..tttttttt..",
            "...tbbbbt...",
            "....t..t....",
        };

        public static string[] For(Kind k, double time) => k switch
        {
            Kind.Cone => Cone,
            Kind.Pegs => Peg,
            Kind.Barrier => Barrier,
            _ => ((int)(time * 12)) % 2 == 0 ? DroneA : DroneB,
        };

        public static string[] Player(SurveyRun g) =>
            g.Over ? Bonked : g.Ducking && g.OnGround ? Duck : !g.OnGround ? RunA : ((int)(g.Distance / 28)) % 2 == 0 ? RunA : RunB;

        public static string? Colour(char c, Palette p) => c switch
        {
            'a' => p.Accent,
            'b' => p.Accent2,
            't' => p.Text,
            'd' => p.Dim,
            _ => null,
        };
    }

    // ------------------------------------------------------------------ office high scores

    public sealed record Best(string User, string Name, int Score);

    public static string ScoresFolder(string dataFolder) => Path.Combine(dataFolder, "egg");

    /// <summary>Saves your score if it beats your best. Returns true when it's a new personal best.</summary>
    public static bool SaveBest(string dataFolder, string user, string name, int score)
    {
        string file = Path.Combine(ScoresFolder(dataFolder), Rooms.Safe(user) + ".txt");
        if (Read(file) is { } old && old.Score >= score) return false;
        Directory.CreateDirectory(ScoresFolder(dataFolder));
        File.WriteAllText(file, $"{score}|{name.Replace("|", " ").Replace("\n", " ")}");
        return true;
    }

    /// <summary>The office's best scores, highest first.</summary>
    public static List<Best> TopScores(string dataFolder, int count = 5)
    {
        var all = new List<Best>();
        string dir = ScoresFolder(dataFolder);
        try
        {
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.txt"))
                    if (Read(f) is { } b) all.Add(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return all.OrderByDescending(b => b.Score).ThenBy(b => b.Name).Take(count).ToList();
    }

    private static Best? Read(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            string[] parts = File.ReadAllText(file).Split('|', 2);
            // Scores are honour-system, but keep them sane.
            return int.TryParse(parts[0], out int s) && s >= 0 && s < 1_000_000
                ? new Best(Path.GetFileNameWithoutExtension(file), parts.Length > 1 ? parts[1].Trim() : "", s) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
