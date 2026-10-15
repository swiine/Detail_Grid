using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

internal static class StandaloneTraining
{
    public static bool On { get; set; }
    public static event Action<string>? Hit;
    public static void Done(string step) { if (On) Hit?.Invoke(step); }
}

internal sealed class TrainingPanel : Panel
{
    public const string SampleBuildup = "[TYPE A - CARPARK]\n40mm AC10 wearing course\nprime\n150mm DGB20 base\n200mm DGS20 subbase\nsubgrade CBR 5%";

    private readonly Dictionary<string, CheckBox> _checks = new();

    public event Action<string>? FillBuildup;
    public event Action<DesignFile>? OpenDesign;

    public TrainingPanel()
    {
        Dock = DockStyle.Right;
        Width = Brand.Px(330);
        BackColor = Brand.Notice;
        Padding = new Padding(10, 8, 10, 8);
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1 };
        list.Controls.Add(new Label { Text = "TRAINING", AutoSize = true, Font = Brand.Sized(12f, FontStyle.Bold), ForeColor = Brand.Heading });
        list.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(Brand.Px(290), 0), ForeColor = Brand.Muted, Margin = new Padding(3, 2, 3, 8),
            Text = "Files go to a temporary folder and nothing is counted. Steps tick themselves when you do them.",
        });
        Step(list, "buildup", "Paste a build-up", "Box ① on the Detail tab. Any wording works.", ("Fill in the sample", () => FillBuildup?.Invoke(SampleBuildup)));
        Step(list, "scale", "Change the scale to 1:20", "Drawing options, Scale. The picture updates.", null);
        Step(list, "dxf", "Save the DXF", "③ Save DXF… (goes to the training folder).", null);
        Step(list, "pdf", "Save a PDF", "Save PDF… next to it. Pick A4 or A3 under Drawing.", null);
        Step(list, "result", "Design a road", "Design tab: go through steps 1-4, or let it fill in a sample.", ("Open a sample design", () => OpenDesign?.Invoke(SampleDesign())));
        Step(list, "report", "Make the calculation report", "Result page: Calculation report… (HTML + Excel).", null);
        Step(list, "batch", "Batch from Excel", "Detail tab: Batch from Excel… > Make the template > Open it > Save all.", null);
        Step(list, "register", "Project register", "Design tab: Project register… lists the saved designs.", null);
        Step(list, "timesaved", "Time saved", "Detail tab: Time saved… (your real numbers - training isn't counted).", null);
        Controls.Add(list);
        StandaloneTraining.Hit += key => { if (_checks.TryGetValue(key, out var c) && !c.IsDisposed) c.Checked = true; };
    }

    private void Step(TableLayoutPanel list, string key, string title, string text, (string Label, Action Do)? action)
    {
        var check = new CheckBox { Text = title, AutoSize = true, Font = Brand.Sized(10f, FontStyle.Bold), ForeColor = Brand.Heading, Margin = new Padding(3, 8, 3, 0), AutoCheck = false };
        var help = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Brand.Px(280), 0), Margin = new Padding(20, 2, 3, 2) };
        check.CheckedChanged += (_, _) => { help.ForeColor = check.Checked ? Brand.Muted : Brand.Text; check.ForeColor = check.Checked ? Brand.Success : Brand.Heading; };
        _checks[key] = check;
        list.Controls.Add(check);
        list.Controls.Add(help);
        if (action is { } a)
        {
            var b = new Button { Text = a.Label, AutoSize = true, Margin = new Padding(20, 2, 3, 2) };
            b.Click += (_, _) => a.Do();
            list.Controls.Add(b);
        }
    }

    private static DesignFile SampleDesign() => new()
    {
        Kind = "Road", Project = "Training - Smith St carpark", JobNo = "TRAIN01", PavementName = "Type A - Carpark", Engineer = "Trainee",
        Desa = "1 x 10^5", Fields = { ["cbr"] = "4", ["area"] = "1200" },
    };
}
