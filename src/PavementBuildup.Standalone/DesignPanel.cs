using System.Globalization;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

/// <summary>
/// Pavement design calculations for engineers (the company workbooks): pick a calculation, fill in the
/// inputs, read the worked result. Results can be copied or saved for the design report, and a design
/// can be sent to the Detail tab to draw.
/// </summary>
internal sealed class DesignPanel : UserControl
{
    private sealed record Input(string Key, string Label, string Unit, double Default, string Hint = "", (string Text, double Value)[]? Choices = null);

    private sealed record Calculation(string Group, string Name, Input[] Inputs, Func<Func<string, double>, CalcResult> Run);

    private static readonly (string, double)[] Grades = { ("25 MPa", 25), ("32 MPa", 32), ("40 MPa", 40), ("50 MPa", 50) };

    private static readonly Calculation[] Calculations =
    {
        new("Flexible", "Flexible pavement - Austroads (granular, thin AC)", new Input[]
        {
            new("cbr", "Subgrade CBR", "%", 5, "Design CBR of the subgrade"),
            new("desa", "Design traffic DESA", "ESA", 1e5, "Design equivalent standard axles, e.g. 100000 or 1e5", new[]
            {
                ("1 x 10^4  minor road, infrequent heavy vehicles", 1e4), ("5 x 10^4  minor road, occasional heavy vehicles", 5e4),
                ("1 x 10^5  local access, infrequent heavy vehicles", 1e5), ("5 x 10^5  local access, buses & trucks", 5e5),
                ("1 x 10^6  collector road", 1e6), ("5 x 10^6  main road", 5e6), ("1 x 10^7  main road", 1e7),
            }),
        }, v => FlexibleDesign.Calc(v("cbr"), v("desa"))),

        new("Concrete (T48)", "Wheel loading - interior", WheelInputs(0.85, 10, 1.2, 1.5, 2, 15),
            v => ConcreteDesign.Wheel(false, v("fc"), v("k1"), v("passes"), v("years"), v("k3"), v("s"), v("h"), v("e"), v("p"))),
        new("Concrete (T48)", "Wheel loading - edge", WheelInputs(0.9, 40, 1.05, 1.8, 9, 31.8),
            v => ConcreteDesign.Wheel(true, v("fc"), v("k1"), v("passes"), v("years"), v("k3"), v("s"), v("h"), v("e"), v("p"))),
        new("Concrete (T48)", "Post (rack) loading - interior", PostInputs(0.85),
            v => ConcreteDesign.Post(false, v("fc"), v("k1"), v("k2"), v("x"), v("y"), v("h"), v("e"), v("p"))),
        new("Concrete (T48)", "Post (rack) loading - edge", PostInputs(1),
            v => ConcreteDesign.Post(true, v("fc"), v("k1"), v("k2"), v("x"), v("y"), v("h"), v("e"), v("p"))),
        new("Concrete (T48)", "Punching shear - interior post", new Input[]
        {
            new("fc", "f'c concrete", "MPa", 32, "", Grades),
            new("b", "Base plate width b", "mm", 165),
            new("t", "Pavement thickness t", "mm", 150),
            new("p", "Factored load P* (1.2G + 1.5Q)", "kN", 84),
        }, v => ConcreteDesign.PunchingShear(v("fc"), v("b"), v("t"), v("p"))),
        new("Concrete (T48)", "UDL - thickness", new Input[]
        {
            new("fc", "f'c concrete", "MPa", 32, "", Grades),
            new("k1", "k1 material factor", "", 0.8, "0.75-0.85"),
            new("n", "N repetitions (0 = no calc)", "", 1000, "Loadings per day x 5 x 52 x years"),
            new("w", "Stack or aisle width W", "m", 4),
            new("h", "Soil depth H", "m", 7),
            new("e", "Soil modulus E", "MPa", 20.7, "Long-term Es (use Equivalent soil modulus)"),
            new("p", "UDL p", "kPa", 30),
        }, v => ConcreteDesign.Udl(v("fc"), v("k1"), v("n"), v("w"), v("h"), v("e"), v("p"))),
        new("Concrete (T48)", "UDL - deflection", new Input[]
        {
            new("ec", "Ec concrete modulus", "MPa", 30100, "AS3600: 25→26700, 32→30100, 40→32800, 50→34800"),
            new("t", "Pavement thickness t", "mm", 150),
            new("p", "UDL p", "kPa", 30),
            new("w", "Loaded width W", "m", 4),
            new("vs", "Soil Poisson's ratio vs", "", 0.25),
            new("cbr", "Subgrade CBR", "%", 5),
            new("h", "Soil depth H (2.5-20 m)", "m", 7.5),
        }, v => ConcreteDesign.UdlDeflection(v("ec"), v("t"), v("p"), v("w"), v("vs"), v("cbr"), v("h"))),

        new("Concrete (T34)", "Wheel stress check - Westergaard / Ioannides / Pickett", new Input[]
        {
            new("pos", "Wheel position", "", 0, "", new[] { ("Interior (Westergaard)", 0.0), ("Close to edge (Ioannides)", 1.0), ("On the edge (Ioannides)", 2.0), ("On the corner (Pickett)", 3.0) }),
            new("p", "Single axle load P", "tonne", 11),
            new("cbr", "Subgrade CBR", "%", 5),
            new("fc", "f'c concrete", "MPa", 32, "", Grades),
            new("mf", "90-day flexural factor", "", 1, "1.0 (28 day) or 1.1 (90 day)"),
            new("v", "Concrete Poisson's ratio", "", 0.15),
            new("h", "Slab thickness h", "mm", 170),
            new("tyre", "Tyre pressure", "kPa", 700),
            new("n", "N repetitions", "", 40000, "400,000 or more = unlimited"),
        }, v => ConcreteDesign.WheelStress((ConcreteDesign.WheelPosition)(int)v("pos"), v("p"), v("cbr"), v("fc"), v("mf"), v("v"), v("h"), v("tyre"), v("n"))),

        new("Soil", "Soil: CBR → k → Es", new Input[] { new("cbr", "CBR", "%", 5) }, v => ConcreteDesign.Soil(v("cbr"))),
        new("Soil", "Equivalent soil modulus (up to 4 layers)", EquivalentInputs(), v =>
        {
            var layers = new List<ConcreteDesign.SoilLayer>();
            for (int i = 1; i <= 4; i++)
                if (v($"h{i}") > 0 || i == 1)
                    layers.Add(new ConcreteDesign.SoilLayer(v($"h{i}"), v($"cbr{i}"), v($"n{i}"), v($"pi{i}"), v($"qc{i}"), v($"a{i}"), v($"b{i}")));
            return ConcreteDesign.EquivalentModulus(layers, v("sw"), v("sp"), v("su"));
        }),
    };

    private static Input[] WheelInputs(double k1, double passes, double k3, double s, double h, double e) => new Input[]
    {
        new("fc", "f'c concrete", "MPa", 32, "Preferably 32 MPa or more", Grades),
        new("k1", "k1 material factor", "", k1, "Wheels 0.85-0.95"),
        new("passes", "Passes per day", "", passes, "Repetitions = passes x 5 days x 52 weeks x years"),
        new("years", "Design life", "years", 20),
        new("k3", "k3 geotechnical calibration", "", k3),
        new("s", "Wheel spacing S", "m", s),
        new("h", "Soil depth H", "m", h),
        new("e", "Soil modulus E", "MPa", e, "Short-term Es (use Equivalent soil modulus)"),
        new("p", "Axle load P (50-600)", "kN", 100, "10 kN = 1 tonne"),
    };

    private static Input[] PostInputs(double k2) => new Input[]
    {
        new("fc", "f'c concrete", "MPa", 32, "Preferably 32 MPa or more", Grades),
        new("k1", "k1 material factor", "", 0.8, "Posts 0.75-0.85"),
        new("k2", "k2 repetitions factor", "", k2, "T48: 1; use the wheel k2 if racks are loaded often"),
        new("x", "Post spacing x", "m", 2),
        new("y", "Post spacing y", "m", 1.5),
        new("h", "Soil depth H", "m", 5),
        new("e", "Soil modulus E", "MPa", 30, "Long-term Es"),
        new("p", "Post load P (unfactored)", "kN", 70),
    };

    private static Input[] EquivalentInputs()
    {
        var list = new List<Input>
        {
            new("sw", "Wheel spacing", "m", 1.8),
            new("sp", "Post spacing", "m", 2),
            new("su", "UDL spacing", "m", 2),
        };
        double[] h = { 1.5, 2.5, 2, 3 }, cbr = { 5, 0, 0, 0 }, n = { 0, 12, 22, 35 }, pi = { 0, 0, 20, 20 }, b = { 1, 0.8, 0.6, 0.6 };
        for (int i = 0; i < 4; i++)
        {
            string l = $"Layer {i + 1}: ";
            list.Add(new($"h{i + 1}", l + "depth H", "m", h[i], i == 0 ? "Layers with H = 0 are left out" : ""));
            list.Add(new($"cbr{i + 1}", l + "CBR", "%", cbr[i], "0 if unknown"));
            list.Add(new($"n{i + 1}", l + "SPT N", "", n[i], "0 if unknown"));
            list.Add(new($"pi{i + 1}", l + "PI", "", pi[i]));
            list.Add(new($"qc{i + 1}", l + "CPT qc", "MPa", 0, "0 if unknown"));
            list.Add(new($"a{i + 1}", l + "a factor (Es = a qc)", "", 0, "T48 Table 1.10"));
            list.Add(new($"b{i + 1}", l + "b factor (EsL = Ess / b)", "", b[i], "T48 Table 1.8"));
        }
        return list.ToArray();
    }

    // ------------------------------------------------------------------ UI -------------------

    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, FormattingEnabled = true, BackColor = Brand.Surface };
    private readonly TableLayoutPanel _inputs = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
    private readonly Label _headline = new() { AutoSize = true, Font = Brand.Sized(11.5f, FontStyle.Bold), Margin = new Padding(3, 4, 3, 6) };
    private readonly DataGridView _results = new();
    private readonly Label _notes = new() { AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(3, 6, 3, 3) };
    private readonly TextBox _project = new() { Width = 260, PlaceholderText = "Project / job" };
    private readonly TextBox _engineer = new() { Width = 180, PlaceholderText = "Engineer" };
    private readonly Button _use = new() { Text = "Draw this build-up", AutoSize = true, Tag = Brand.PrimaryTag };
    private readonly ToolTip _tips = new();
    private readonly Dictionary<string, Control> _fields = new();
    private Calculation? _current;
    private CalcResult? _result;

    /// <summary>Raised with build-up text when the engineer sends a design to the Detail tab.</summary>
    public event Action<string>? UseBuildup;

    public DesignPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Brand.Bg;
        Padding = new Padding(0, 8, 0, 0);

        foreach (var c in Calculations) _list.Items.Add(c);
        _list.Format += (_, e) => e.Value = e.ListItem is Calculation c ? $"{c.Group}:  {c.Name}" : "";
        _list.SelectedIndexChanged += (_, _) => Select(_list.SelectedItem as Calculation);

        var left = new Panel { Dock = DockStyle.Left, Width = Brand.Px(330), Padding = new Padding(0, 0, 12, 0) };
        left.Controls.Add(_list);
        left.Controls.Add(Brand.Step("① Choose a calculation"));
        left.Controls[1].Dock = DockStyle.Top;

        var inputsScroll = new Panel { Dock = DockStyle.Left, Width = Brand.Px(420), AutoScroll = true, Padding = new Padding(0, 0, 12, 0) };
        inputsScroll.Controls.Add(_inputs);
        var inputsStep = Brand.Step("② Inputs");
        inputsStep.Dock = DockStyle.Top;
        inputsScroll.Controls.Add(inputsStep);
        _inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Brand.Px(150)));
        _inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        SetUpGrid();
        var copy = new Button { Text = "Copy calculation", AutoSize = true };
        copy.Click += (_, _) => { if (_result is not null) Clipboard.SetText(_result.ToText(_project.Text, _engineer.Text)); };
        var save = new Button { Text = "Save calculation…", AutoSize = true };
        save.Click += (_, _) => SaveReport();
        _use.Click += (_, _) => { if (_result?.BuildupText is { } t) UseBuildup?.Invoke(t); };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 6, 0, 0) };
        actions.Controls.AddRange(new Control[] { _use, copy, save, _project, _engineer });
        foreach (Control c in actions.Controls) c.Margin = new Padding(0, 3, 8, 3);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(_results);
        right.Controls.Add(_notes);
        right.Controls.Add(_headline);
        var resultStep = Brand.Step("③ Result");
        resultStep.Dock = DockStyle.Top;
        _headline.Dock = DockStyle.Top;
        _notes.Dock = DockStyle.Bottom;
        right.Controls.Add(resultStep);
        right.Controls.Add(actions);
        _results.Dock = DockStyle.Fill;
        right.Resize += (_, _) => _notes.MaximumSize = new Size(Math.Max(200, right.Width - 10), 0);

        Controls.Add(right);
        Controls.Add(inputsScroll);
        Controls.Add(left);
        _list.SelectedIndex = 0;
    }

    private void SetUpGrid()
    {
        _results.ReadOnly = true;
        _results.AllowUserToAddRows = false;
        _results.AllowUserToDeleteRows = false;
        _results.RowHeadersVisible = false;
        _results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.Columns.Add("label", "Step");
        _results.Columns.Add("value", "Value");
        _results.Columns.Add("unit", "Unit");
        _results.Columns.Add("ref", "Note");
        _results.Columns[0].FillWeight = 230;
        _results.Columns[1].FillWeight = 70;
        _results.Columns[2].FillWeight = 55;
        _results.Columns[3].FillWeight = 90;
        _results.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private void Select(Calculation? calc)
    {
        if (calc is null) return;
        _current = calc;
        _inputs.SuspendLayout();
        _inputs.Controls.Clear();
        _inputs.RowStyles.Clear();
        _fields.Clear();
        foreach (var input in calc.Inputs)
        {
            var label = new Label { Text = input.Label, AutoSize = true, Margin = new Padding(3, 8, 6, 3) };
            Control field;
            if (input.Choices is { } choices)
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, BackColor = Brand.Surface, FlatStyle = FlatStyle.Flat };
                foreach (var (text, _) in choices) combo.Items.Add(text);
                int match = Array.FindIndex(choices, c => Math.Abs(c.Value - input.Default) < 1e-9);
                if (match >= 0) combo.SelectedIndex = match; else combo.Text = Format(input.Default);
                if (input.Key is "fc" or "pos") combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.TextChanged += (_, _) => Recalculate();
                combo.SelectedIndexChanged += (_, _) => Recalculate();
                field = combo;
            }
            else
            {
                var box = new TextBox { Text = Format(input.Default), Dock = DockStyle.Fill, BackColor = Brand.Surface, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right };
                box.TextChanged += (_, _) => Recalculate();
                field = box;
            }
            field.Margin = new Padding(3, 4, 3, 3);
            if (input.Hint.Length > 0) { _tips.SetToolTip(field, input.Hint); _tips.SetToolTip(label, input.Hint); }
            _fields[input.Key] = field;
            _inputs.Controls.Add(label);
            _inputs.Controls.Add(field);
            _inputs.Controls.Add(new Label { Text = input.Unit, AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(3, 8, 3, 3) });
        }
        _inputs.ResumeLayout();
        Recalculate();
    }

    private static string Format(double v) => Math.Abs(v) >= 1e5 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The number in a field: plain numbers, "1e5", "1 x 10^5", "100,000", or a listed choice.</summary>
    private double Value(Input input)
    {
        var control = _fields[input.Key];
        if (input.Choices is { } choices && control is ComboBox combo)
        {
            int i = Array.FindIndex(choices, c => c.Text == combo.Text);
            if (i >= 0) return choices[i].Value;
        }
        var text = control.Text.Trim().Replace(",", "").Replace(" ", "").ToLowerInvariant();
        var power = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+(?:\.\d+)?)x10\^(\d+)");
        if (power.Success)
            return double.Parse(power.Groups[1].Value, CultureInfo.InvariantCulture) * Math.Pow(10, int.Parse(power.Groups[2].Value));
        var number = System.Text.RegularExpressions.Regex.Match(text, @"^-?\d+(?:\.\d+)?(?:e[+-]?\d+)?");
        if (number.Success && double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        throw new FormatException($"\"{control.Text}\" is not a number ({input.Label}).");
    }

    private void Recalculate()
    {
        if (_current is null) return;
        _results.Rows.Clear();
        try
        {
            _result = _current.Run(key => Value(_current.Inputs.First(i => i.Key == key)));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            _result = null;
            _headline.ForeColor = Brand.Failure;
            _headline.Text = ex.Message;
            _notes.Text = "";
            _use.Enabled = false;
            return;
        }

        foreach (var line in _result.Lines.Where(l => l.Kind != LineKind.Input))
        {
            int row = _results.Rows.Add(line.Label, line.Value, line.Unit, line.Reference);
            var style = _results.Rows[row].DefaultCellStyle;
            switch (line.Kind)
            {
                case LineKind.Result: style.Font = Brand.Body(FontStyle.Bold); break;
                case LineKind.Ok: style.ForeColor = Brand.Success; style.Font = Brand.Body(FontStyle.Bold); break;
                case LineKind.Fail: style.ForeColor = Brand.Failure; style.Font = Brand.Body(FontStyle.Bold); break;
                case LineKind.Warning: style.ForeColor = Brand.Failure; break;
            }
        }
        _headline.ForeColor = _result.Pass == false ? Brand.Failure : Brand.Success;
        _headline.Text = (_result.Pass == false ? "✖ " : "✔ ") + _result.Headline;
        _notes.Text = string.Join(Environment.NewLine, _result.Notes.Distinct().Select(n => "• " + n)) +
                      (_result.Reference.Length > 0 ? Environment.NewLine + "Reference: " + _result.Reference : "");

        // Concrete thickness → a simple build-up for the detail.
        if (_result.BuildupText is null && _result.ThicknessMm is { } t && _fields.ContainsKey("fc") && _result.Pass != false)
            _result.BuildupText = $"[{_current.Name.ToUpperInvariant()}]\n{t:0}mm F'c {Value(_current.Inputs.First(i => i.Key == "fc")):0}MPa CONCRETE PAVEMENT\nSUBGRADE";
        _use.Enabled = _result.BuildupText is not null;
    }

    private void SaveReport()
    {
        if (_result is null) return;
        using var dlg = new SaveFileDialog { InitialDirectory = AppFolders.Exports, Filter = "Text file (*.txt)|*.txt", FileName = _result.Title.Replace(" - ", " ").Replace("/", "-") + ".txt", Title = "Save the calculation" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dlg.FileName, _result.ToText(_project.Text, _engineer.Text)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, "TTW Pavement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
