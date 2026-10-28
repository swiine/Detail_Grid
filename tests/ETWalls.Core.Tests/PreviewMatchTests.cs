using System.Text.Json;
using ETWalls.Core.Design;

namespace ETWalls.Core.Tests;

/// <summary>
/// The app must give the same answers as the preview page (calc.js). golden-preview.json holds the preview's
/// results for a spread of walls: every check, the bar schedule, the cost and the automatic sizing.
/// </summary>
public class PreviewMatchTests
{
    static readonly JsonElement Golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden-preview.json"))).RootElement;

    public static IEnumerable<object[]> Runs() => Golden.GetProperty("runs").EnumerateObject().Select(p => new object[] { p.Name });
    public static IEnumerable<object[]> Opts() => Golden.GetProperty("opts").EnumerateObject().Select(p => new object[] { p.Name });

    internal static WallInput FromJs(JsonElement j)
    {
        double N(string k) => j.GetProperty(k).GetDouble();
        bool B(string k) => j.GetProperty(k).GetBoolean();
        var type = j.GetProperty("type").GetString() switch { "block" => WallType.Block, "gravity" => WallType.Gravity, "post" => WallType.Post, _ => WallType.Cantilever };
        var bar = j.GetProperty("bar");
        return new WallInput
        {
            Type = type,
            Footing = j.GetProperty("footing").GetString() == "toe" ? Footing.Toe : Footing.Heel,
            Site = j.GetProperty("site").GetString() == "fill" ? Site.Fill : Site.Cut,
            H = N("H"), Slope = N("slope"), D = N("D"), Tb = N("tb"), Toe = N("toe"), Heel = N("heel"), T1 = N("t1"), T2 = N("t2"),
            Bg = N("Bg"), Bt = N("Bt"), GammaWall = N("gammaWall"), S = N("s"), Dia = N("dia"), Dp = N("Dp"), Post = j.GetProperty("post").GetString()!,
            Phi = N("phi"), Gamma = N("gamma"), PhiF = N("phiF"), GammaF = N("gammaF"), CF = N("cF"), Delta = N("delta"), Qa = N("qa"), Q = N("q"),
            Drained = B("drained"), Hw = N("hw"), Kh = N("kh"), UsePassive = B("usePassive"), Fc = N("fc"), Cover = N("cover"),
            Bar = bar.ValueKind == JsonValueKind.Number ? bar.GetInt32() : 0,
        };
    }

    static void Close(double expected, double actual, string what)
    {
        double tol = Math.Max(1e-6, Math.Abs(expected) * 1e-9);
        Assert.True(Math.Abs(expected - actual) <= tol, $"{what}: preview {expected}, app {actual}");
    }

    [Theory, MemberData(nameof(Runs))]
    public void ChecksScheduleAndCostMatchThePreview(string name)
    {
        var g = Golden.GetProperty("runs").GetProperty(name);
        var inp = FromJs(g.GetProperty("input"));
        var r = WallDesign.RunAll(inp);

        var checks = g.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(checks.Select(c => c.GetProperty("id").GetString()), r.Checks.Select(c => c.Id));
        for (int i = 0; i < checks.Count; i++)
        {
            Close(checks[i].GetProperty("value").GetDouble(), r.Checks[i].Value, $"{name} {r.Checks[i].Id} value");
            Close(checks[i].GetProperty("limit").GetDouble(), r.Checks[i].Limit, $"{name} {r.Checks[i].Id} limit");
            Close(checks[i].GetProperty("util").GetDouble(), r.Checks[i].Util, $"{name} {r.Checks[i].Id} util");
        }

        var sched = g.GetProperty("schedule").EnumerateArray().ToList();
        var mine = r.R.Reo?.Schedule ?? new();
        Assert.Equal(sched.Count, mine.Count);
        for (int i = 0; i < sched.Count; i++)
        {
            Assert.Equal(sched[i].GetProperty("mark").GetString(), mine[i].Mark);
            Assert.Equal(sched[i].GetProperty("bars").GetString(), mine[i].Bars);
            Close(sched[i].GetProperty("kg").GetDouble(), mine[i].Kg, $"{name} {mine[i].Mark} kg");
        }
        var cut = g.GetProperty("cut");
        if (cut.ValueKind == JsonValueKind.Number) Close(cut.GetDouble(), r.R.Reo!.Cut!.Value, "cut"); else Assert.Null(r.R.Reo?.Cut);

        var cost = Costs.Estimate(inp, r.R);
        var items = g.GetProperty("costItems").EnumerateArray().ToList();
        Assert.Equal(items.Select(i => i.GetProperty("key").GetString()), cost.Items.Select(i => i.Key));
        for (int i = 0; i < items.Count; i++) Close(items[i].GetProperty("qty").GetDouble(), cost.Items[i].Qty, $"{name} {cost.Items[i].Key} qty");
        Close(g.GetProperty("costPerM").GetDouble(), cost.PerMetre, $"{name} cost per m");
    }

    [Theory, MemberData(nameof(Opts))]
    public void AutomaticSizingPicksTheSameWall(string name)
    {
        var g = Golden.GetProperty("opts").GetProperty(name);
        var inp = FromJs(g.GetProperty("input"));
        var res = Optimiser.Optimise(inp, g.GetProperty("margin").GetDouble());
        Assert.Equal(g.GetProperty("ok").GetBoolean(), res.Ok);
        Assert.Equal(g.GetProperty("tried").GetInt32(), res.Tried);
        var opts = g.GetProperty("options").EnumerateArray().ToList();
        Assert.Equal(opts.Select(o => o.GetProperty("label").GetString()), res.Options.Select(o => o.Label));
        for (int i = 0; i < opts.Count; i++)
        {
            Close(opts[i].GetProperty("cost").GetDouble(), res.Options[i].CostPerMetre, $"{name} option {i} cost");
            var applied = res.Options[i].Sizes.Apply(inp);
            foreach (var p in opts[i].GetProperty("g").EnumerateObject())
            {
                object mineV = p.Name switch
                {
                    "D" => applied.D, "t1" => applied.T1, "t2" => applied.T2, "tb" => applied.Tb, "toe" => applied.Toe, "heel" => applied.Heel,
                    "Bg" => applied.Bg, "Bt" => applied.Bt, "gammaWall" => applied.GammaWall, "s" => applied.S, "dia" => applied.Dia, "Dp" => applied.Dp,
                    "post" => applied.Post, _ => throw new InvalidOperationException(p.Name),
                };
                if (p.Value.ValueKind == JsonValueKind.String) Assert.Equal(p.Value.GetString(), mineV);
                else Close(p.Value.GetDouble(), (double)mineV, $"{name} option {i} {p.Name}");
            }
        }
    }

    [Theory]
    [InlineData("2.4", ValueKind.Length, 2.4)]
    [InlineData("2400", ValueKind.Length, 2.4)]
    [InlineData("2400mm", ValueKind.Length, 2.4)]
    [InlineData("240 cm", ValueKind.Length, 2.4)]
    [InlineData("30°", ValueKind.Angle, 30)]
    [InlineData("10kPa", ValueKind.Number, 10)]
    [InlineData("1,000", ValueKind.Number, 1000)]
    public void ReadsTypedValues(string text, ValueKind kind, double expected)
    {
        var r = Units.Read(text, kind);
        Assert.True(r.Ok);
        Assert.Equal(expected, r.Value, 9);
    }

    [Fact]
    public void RejectsText() => Assert.False(Units.Read("two metres", ValueKind.Length).Ok);
}
