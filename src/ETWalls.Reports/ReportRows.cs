using System.Globalization;
using ETWalls.Core.Design;

namespace ETWalls.Reports;

public sealed record InputRow(string Group, string Name, string Value, string Unit, string Source);

/// <summary>The inputs as listed in both reports (section 3 / the Inputs sheet).</summary>
public static class ReportRows
{
    static string G(double v) => v.ToString(CultureInfo.InvariantCulture);

    public static List<InputRow> Inputs(DesignRun run)
    {
        var i = run.Input; var rows = new List<InputRow>();
        void Add(string g, string n, string v, string u, string s = "") => rows.Add(new(g, n, v, u, s));
        string auto = i.Custom ? "Custom" : "Automatic";
        Add("Wall", "Wall type", DesignRun.TypeName(i.Type), "");
        Add("Wall", "Retained height", G(i.H), "m", "Entered");
        Add("Wall", "Backfill slope", G(i.Slope), "°", "Entered");
        Add("Wall", "Wall length", G(i.Length), "m", "Entered");
        Add("Wall", "Site", i.Site == Site.Cut ? "Cut (digging out behind)" : "Fill (building up behind)", "", "Entered");
        if (i.Type == WallType.Cantilever) Add("Wall", "Footing", i.Footing == Footing.Toe ? "In front only (no heel)" : "Under the retained soil (heel)", "", "Entered");
        Add("Wall", "Drawing direction", i.FacesRight ? "Faces right" : "Faces left", "", "Entered");
        switch (i.Type)
        {
            case WallType.Cantilever:
                Add("Wall", "Depth below front ground", G(i.D), "m", auto);
                Add("Wall", "Base thickness", G(i.Tb), "m", auto);
                Add("Wall", "Toe length", G(i.Toe), "m", auto);
                if (i.Footing == Footing.Heel) Add("Wall", "Heel length", G(i.Heel), "m", auto);
                Add("Wall", "Stem thickness, top", G(i.T1), "m", auto);
                Add("Wall", "Stem thickness, bottom", G(i.T2), "m", auto);
                Add("Wall", "Base width", G(Math.Round(i.BaseWidth, 3)), "m", auto);
                break;
            case WallType.Post:
                Add("Wall", "Post spacing", G(i.S), "m", auto);
                Add("Wall", "Pier diameter", G(i.Dia), "m", auto);
                Add("Wall", "Pier embedment", G(i.Dp), "m", auto);
                Add("Wall", "Post size", i.Post, "", auto);
                break;
            default:
                Add("Wall", "Depth below front ground", G(i.D), "m", auto);
                Add("Wall", "Base width", G(i.Bg), "m", auto);
                Add("Wall", "Top width", G(i.Bt), "m", auto);
                Add("Wall", "Wall unit weight", G(i.GammaWall), "kN/m³", auto);
                break;
        }
        Add("Soil", "Backfill friction angle φ", G(i.Phi), "°", "Entered");
        Add("Soil", "Backfill unit weight γ", G(i.Gamma), "kN/m³", "Entered");
        Add("Soil", "Foundation φ", G(i.PhiF), "°", "Entered");
        Add("Soil", "Foundation γ", G(i.GammaF), "kN/m³", "Entered");
        if (i.Type != WallType.Post)
        {
            Add("Soil", "Foundation cohesion c'", G(i.CF), "kPa", "Entered");
            Add("Soil", "Base friction angle δ", G(i.Delta), "°", "Entered");
            Add("Soil", "Allowable bearing", G(i.Qa), "kPa", "Entered");
        }
        Add("Loads", "Surcharge behind", G(i.Q), "kPa", "Entered");
        Add("Loads", "Drainage", i.Drained ? "Drained backfill, ag pipe" : $"Not drained, water {G(i.Hw)} m", "", "Entered");
        Add("Loads", "Earthquake coefficient kh", G(i.Kh), "", "Entered");
        if (i.Type != WallType.Post) Add("Loads", "Passive soil in front", i.UsePassive ? "Half passive counted" : "Ignored", "", "Entered");
        if (i.Type == WallType.Cantilever)
        {
            Add("Concrete", "Concrete f'c", G(i.Fc), "MPa", "Entered");
            Add("Concrete", "Cover to bars", G(i.Cover), "mm", "Entered");
            Add("Concrete", "Bar size", i.Bar == 0 ? "Auto" : "N" + i.Bar, "", "Entered");
        }
        Add("Design", "Spare capacity", DesignRun.MarginText(i.Margin), "", "Entered");
        var d = Rates.Defaults;
        foreach (var (name, unit, v, dv) in RateList(i))
            Add("Rates", name, G(v), unit, Math.Abs(v - dv) < 1e-9 ? "Default rate" : "Entered");
        return rows;
    }

    /// <summary>The rates a wall type uses: name, unit, value, default.</summary>
    public static IEnumerable<(string Name, string Unit, double Value, double Default)> RateList(WallInput i)
    {
        var r = i.Rates; var d = Rates.Defaults;
        if (i.Type == WallType.Block) yield return ("Block wall", "$/m³", r.Blocks, d.Blocks);
        if (i.Type == WallType.Post) { yield return ("Steel posts", "$/kg", r.PostSteel, d.PostSteel); yield return ("Pier drilling (Ø600)", "$/m", r.Drilling, d.Drilling); }
        yield return ("Concrete", "$/m³", r.Concrete, d.Concrete);
        if (i.Type is WallType.Cantilever or WallType.Post) yield return ("Reinforcement", "$/kg", r.Steel, d.Steel);
        if (i.Type == WallType.Post) yield return ("Sleepers", "$/m²", r.Sleepers, d.Sleepers);
        if (i.Type is WallType.Cantilever or WallType.Gravity) yield return ("Formwork", "$/m²", r.Formwork, d.Formwork);
        if (i.Type != WallType.Post) { yield return ("Excavation", "$/m³", r.Excavation, d.Excavation); yield return ("Compacted backfill", "$/m³", r.Backfill, d.Backfill); }
        yield return ("Drainage gravel", "$/m³", r.Drainage, d.Drainage);
        yield return ("Ag pipe", "$/m", r.AgPipe, d.AgPipe);
    }

    public static string Money(double v) => "$" + Math.Round(v).ToString("#,0", CultureInfo.GetCultureInfo("en-AU"));
    public static string Rate(double v) => v < 20 ? "$" + v.ToString("0.00", CultureInfo.InvariantCulture) : Money(v);
    public static string Fmt(double v, string unit) => double.IsFinite(v) ? v.ToString(unit is "kPa" or "mm" ? "0" : "0.00", CultureInfo.InvariantCulture) : "∞";
}
