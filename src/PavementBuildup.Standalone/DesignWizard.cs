using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

/// <summary>
/// The design, one step at a time: ① Project → ② Ground → ③ Loads / traffic → ④ Result. Inputs are entered
/// once, read the way engineers type them ("20t", "1e6", "5%"), checked as you go, and the result updates
/// live. The result can be drawn, reported (HTML, prints to PDF), copied, or saved and opened again.
/// </summary>
internal sealed class DesignWizard : UserControl
{
    private enum Kind { Road, Slab }

    /// <summary>A number box with its unit, help line and live check.</summary>
    private sealed class Field
    {
        public required string Key { get; init; }
        public required string Unit { get; init; }
        public required TextBox Box { get; init; }
        public required Label Help { get; init; }
        public required string HelpText { get; init; }
        public double Value { get; set; }
        public bool Ok { get; set; } = true;
    }

    private static readonly string[] StepNames = { "Project", "Ground", "Loads", "Result" };

    private readonly Dictionary<string, Field> _fields = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly Label[] _stepLabels = new Label[4];
    private readonly Panel[] _pages = new Panel[4];
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Button _back = new() { Text = "◀  Back", AutoSize = true };
    private readonly Button _next = new() { Text = "Next  ▶", AutoSize = true, Tag = Brand.PrimaryTag };
    private readonly Label _live = new() { AutoSize = true, Margin = new Padding(16, 12, 3, 3) };
    private readonly ToolTip _tips = new();
    private int _step;
    private Kind _kind = Kind.Road;
    private CalcResult? _result;
    private string? _error;

    // Step 1
    private readonly TextBox _project = new() { Width = 340, PlaceholderText = "e.g. 123 Smith St carpark" };
    private readonly TextBox _jobNo = new() { Width = 140, PlaceholderText = "e.g. 241001" };
    private readonly TextBox _engineer = new() { Width = 220 };
    private readonly TextBox _checker = new() { Width = 220 };
    private readonly RadioButton _road = new() { AutoSize = true, Checked = true };
    private readonly RadioButton _slab = new() { AutoSize = true };

    // Step 2
    private readonly Label _soilDerived = new() { AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(3, 2, 3, 8) };
    private readonly Panel _slabGround = new() { AutoSize = true, Dock = DockStyle.Top };

    // Step 3
    private readonly Panel _roadLoads = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly Panel _slabLoads = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly RadioButton _knownDesa = new() { Text = "I know the design traffic (DESA)", AutoSize = true, Checked = true };
    private readonly RadioButton _countDesa = new() { Text = "Work it out from traffic counts", AutoSize = true };
    private readonly ComboBox _desa = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Panel _countsPanel = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly ComboBox _grade = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Panel _advanced = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };

    // Step 4
    private readonly Label _headline = new() { AutoSize = true, Font = Brand.Sized(13f, FontStyle.Bold), Margin = new Padding(3, 4, 3, 8) };
    private readonly DataGridView _grid = new();
    private readonly TextBox _tables = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };
    private readonly PreviewPanel _picture = new() { Dock = DockStyle.Fill };

    /// <summary>Raised with build-up text when the engineer sends the design to the Detail tab.</summary>
    public event Action<string>? UseBuildup;

    public DesignWizard()
    {
        Dock = DockStyle.Fill;
        BackColor = Brand.Bg;

        Controls.Add(_pageHost);
        Controls.Add(BuildStepBar());
        Controls.Add(BuildFooter());
        _pageHost.BringToFront();

        _pages[0] = BuildProject();
        _pages[1] = BuildGround();
        _pages[2] = BuildLoads();
        _pages[3] = BuildResult();
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _pageHost.Controls.Add(page);
        }
        ShowStep(0);
        Recalculate();
    }

    // ================================================================ frame ====================

    private Control BuildStepBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 8) };
        for (int i = 0; i < 4; i++)
        {
            int step = i;
            var label = _stepLabels[i] = new Label
            {
                AutoSize = true, Font = Brand.Sized(11f, FontStyle.Bold), Padding = new Padding(10, 6, 12, 6),
                Margin = new Padding(0, 0, 4, 0), Cursor = Cursors.Hand, Text = $"{i + 1}  {StepNames[i]}",
            };
            label.Click += (_, _) => GoTo(step);
            bar.Controls.Add(label);
            if (i < 3)
                bar.Controls.Add(new Label { Text = "›", AutoSize = true, Font = Brand.Sized(14f), ForeColor = Brand.Muted, Margin = new Padding(0, 2, 4, 0) });
        }
        return bar;
    }

    private Control BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = Brand.Px(56), Padding = new Padding(0, 10, 0, 0) };
        _back.MinimumSize = _next.MinimumSize = new Size(Brand.Px(110), Brand.Px(36));
        _back.Click += (_, _) => GoTo(_step - 1);
        _next.Click += (_, _) => GoTo(_step + 1);
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        left.Controls.Add(_back);
        left.Controls.Add(_next);
        left.Controls.Add(_live);
        bar.Controls.Add(left);
        return bar;
    }

    private void GoTo(int step)
    {
        step = Math.Clamp(step, 0, 3);
        // Moving forward: the steps before must be filled in correctly.
        for (int s = _step; s < step; s++)
        {
            var problem = ProblemOnStep(s);
            if (problem is not null)
            {
                ShowStep(s);
                _live.ForeColor = Brand.Failure;
                _live.Text = "✖ " + problem;
                return;
            }
        }
        ShowStep(step);
    }

    private void ShowStep(int step)
    {
        _step = step;
        for (int i = 0; i < 4; i++)
        {
            _pages[i].Visible = i == step;
            var label = _stepLabels[i];
            label.BackColor = i == step ? Brand.Accent : i < step ? Brand.Selection : Brand.Bg;
            label.ForeColor = i == step ? Brand.AccentFg : i < step ? Brand.Accent : Brand.Muted;
            label.Text = (i < step ? "✔ " : $"{i + 1}  ") + StepNames[i] + (i == 2 ? (_kind == Kind.Road ? " (traffic)" : " (wheels, racks, storage)") : "");
        }
        _back.Enabled = step > 0;
        _next.Visible = step < 3;
        _next.Text = step == 2 ? "See the result  ▶" : "Next  ▶";
        if (step == 3) ShowResult();
        UpdateLive();
    }

    /// <summary>What stops the engineer leaving a step, or null.</summary>
    private string? ProblemOnStep(int step)
    {
        var keys = step switch
        {
            1 => _kind == Kind.Road ? new[] { "cbr" } : new[] { "cbr", "h", "es" },
            2 => _kind == Kind.Road ? RoadLoadKeys() : SlabLoadKeys(),
            _ => Array.Empty<string>(),
        };
        foreach (var key in keys)
            if (_fields.TryGetValue(key, out var f) && !f.Ok)
                return $"Check \"{f.Help.Tag}\": {f.Help.Text}";
        if (step == 2 && _error is not null) return _error;
        return null;
    }

    private string[] RoadLoadKeys() => _knownDesa.Checked ? Array.Empty<string>() : new[] { "aadt", "hv", "growth", "years", "esahv", "df", "ldf" };

    private string[] SlabLoadKeys()
    {
        var keys = new List<string>();
        if (_checks["wheels"].Checked) keys.AddRange(new[] { "axle", "ws", "passes", "life", "tyre" });
        if (_checks["posts"].Checked) keys.AddRange(new[] { "post", "px", "py", "plate", "lf" });
        if (_checks["udl"].Checked) keys.AddRange(new[] { "udl", "aisle", "udlreps" });
        if (_advanced.Visible) keys.AddRange(new[] { "k1w", "k3i", "k3e", "k1p", "k2p", "k1u" });
        return keys.ToArray();
    }

    // ================================================================ step 1: project ==========

    private Panel BuildProject()
    {
        var page = Page(out var t);
        t.Controls.Add(Brand.Step("Which job is this for?"));
        t.Controls.Add(Row(Lbl("Project"), _project, Lbl("Job no."), _jobNo));
        t.Controls.Add(Row(Lbl("Designed by"), _engineer, Lbl("Checked by"), _checker));
        t.Controls.Add(Hint("These go on the calculation report."));

        t.Controls.Add(Brand.Step("What are you designing?"));
        t.Controls.Add(Choice(_road, "Road, car park or driveway",
            "Asphalt over crushed rock (granular with thin bituminous surfacing). Austroads + the company design table."));
        t.Controls.Add(Choice(_slab, "Concrete slab",
            "Warehouse floor, hardstand, loading dock: forklifts / trucks, racks and storage loads. CCAA T48 + T34 checks."));
        _road.CheckedChanged += (_, _) => SetKind(_road.Checked ? Kind.Road : Kind.Slab);

        var open = new Button { Text = "Open a saved design…", AutoSize = true };
        open.Click += (_, _) => OpenDesign();
        var advanced = new Button { Text = "Single checks (advanced)…", AutoSize = true };
        advanced.Click += (_, _) => OpenSingleChecks();
        _tips.SetToolTip(advanced, "Each spreadsheet calculation on its own: wheels, posts, punching shear, UDL, deflection, T34, soil.");
        t.Controls.Add(Row(open, advanced));
        return page;
    }

    private Control Choice(RadioButton radio, string title, string text)
    {
        radio.Text = title;
        radio.Font = Brand.Sized(11f, FontStyle.Bold);
        radio.ForeColor = Brand.Heading;
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(3, 4, 3, 8) };
        panel.Controls.Add(radio);
        var help = Hint(text);
        help.Margin = new Padding(22, 0, 3, 0);
        panel.Controls.Add(help);
        return panel;
    }

    private void SetKind(Kind kind)
    {
        _kind = kind;
        _slabGround.Visible = kind == Kind.Slab;
        _roadLoads.Visible = kind == Kind.Road;
        _slabLoads.Visible = kind == Kind.Slab;
        ShowStep(_step);
        Recalculate();
    }

    // ================================================================ step 2: ground ===========

    private Panel BuildGround()
    {
        var page = Page(out var t);
        t.Controls.Add(Brand.Step("What is the ground like?"));
        t.Controls.Add(FieldRow("cbr", "Subgrade CBR", "%", "5", "From the geotechnical report. Quick picks: "));
        var picks = Row();
        foreach (var (label, cbr) in new[] { ("Soft clay  2%", "2"), ("Clay  3%", "3"), ("Silty / sandy clay  5%", "5"), ("Sand  10%", "10"), ("Gravel  15%", "15") })
        {
            var b = new Button { Text = label, AutoSize = true };
            b.Click += (_, _) => _fields["cbr"].Box.Text = cbr;
            picks.Controls.Add(b);
        }
        t.Controls.Add(picks);
        t.Controls.Add(_soilDerived);

        var slab = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top };
        slab.Controls.Add(FieldRow("h", "Depth of soil to rock / firm layer", "m", "5", "T48 soil depth H. Use 7 m or more if unknown."));
        slab.Controls.Add(FieldRow("es", "Soil modulus Es (optional)", "MPa", "", "Leave blank to work it out from the CBR. Enter it if you have a layered profile (Single checks → Equivalent soil modulus)."));
        _slabGround.Controls.Add(slab);
        _slabGround.Visible = false;
        t.Controls.Add(_slabGround);
        return page;
    }

    // ================================================================ step 3: loads ============

    private Panel BuildLoads()
    {
        var page = Page(out var t);

        // Road: traffic.
        var road = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top };
        road.Controls.Add(Brand.Step("How much traffic over the design life?"));
        road.Controls.Add(_knownDesa);
        foreach (var (desa, label, description) in FlexibleDesign.TrafficRows)
            _desa.Items.Add($"{label}  -  {description}");
        _desa.SelectedIndex = 2;
        _desa.TextChanged += (_, _) => Recalculate();
        var desaRow = Row(_desa);
        desaRow.Margin = new Padding(22, 0, 3, 0);
        road.Controls.Add(desaRow);
        var desaHint = Hint("Pick a level or type your own, e.g. 2e5 or 1 x 10^6 ESA.");
        desaHint.Margin = new Padding(25, 0, 3, 6);
        road.Controls.Add(desaHint);
        road.Controls.Add(_countDesa);
        var counts = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Margin = new Padding(22, 0, 3, 0) };
        counts.Controls.Add(FieldRow("aadt", "Traffic, both directions (AADT)", "veh/day", "2000", "Average vehicles per day in the opening year."));
        counts.Controls.Add(FieldRow("hv", "Heavy vehicles", "%", "10", "Share of the traffic that is trucks / buses."));
        counts.Controls.Add(FieldRow("growth", "Growth", "%", "2", "Per year."));
        counts.Controls.Add(FieldRow("years", "Design life", "years", "20", "Usually 20 to 40 years."));
        counts.Controls.Add(FieldRow("esahv", "ESA per heavy vehicle", "", "1", "From project traffic data if you have it."));
        counts.Controls.Add(FieldRow("df", "Direction factor", "", "0.5", "Share in the busier direction (0.5 for two-way)."));
        counts.Controls.Add(FieldRow("ldf", "Lane factor", "", "1", "Share in the design lane (1 for one lane each way)."));
        _countsPanel.Controls.Add(counts);
        _countsPanel.Enabled = false;
        road.Controls.Add(_countsPanel);
        _knownDesa.CheckedChanged += (_, _) => { _desa.Enabled = _knownDesa.Checked; _countsPanel.Enabled = !_knownDesa.Checked; Recalculate(); };
        _roadLoads.Controls.Add(road);

        // Slab: wheels, racks, storage.
        var slab = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top };
        slab.Controls.Add(Brand.Step("What will the slab carry?"));
        foreach (var g in ConcreteDesign.Grades) _grade.Items.Add($"{g:0} MPa");
        _grade.SelectedIndex = 1;
        _grade.SelectedIndexChanged += (_, _) => Recalculate();
        slab.Controls.Add(Row(Lbl("Concrete f'c"), _grade, Check("edges", "Check free edges too", true, "Loads at joints without dowels and at slab edges."),
            Check("t34", "T34 cross-checks", true, "Also check wheels with Westergaard / Ioannides / Pickett.")));

        slab.Controls.Add(Section("wheels", "Forklifts / trucks (wheel loads)", true, new[]
        {
            FieldRow("axle", "Heaviest axle load", "tonne", "10", "Loaded forklift front axle or truck axle. Type tonnes or kN (e.g. 98kN)."),
            FieldRow("ws", "Wheel spacing", "m", "1.5", "Centre to centre across the axle."),
            FieldRow("passes", "Passes per day", "", "10", "Over the busiest point."),
            FieldRow("life", "Design life", "years", "20", "Repetitions = passes x 5 days x 52 weeks x years."),
            FieldRow("tyre", "Tyre pressure", "kPa", "700", "For the T34 checks. Solid tyres can be 1000+ kPa."),
        }));
        slab.Controls.Add(Section("posts", "Racks (post loads)", false, new[]
        {
            FieldRow("post", "Post load (unfactored)", "kN", "70", "Working load on one rack leg."),
            FieldRow("px", "Post spacing x", "m", "2", "Along the rack."),
            FieldRow("py", "Post spacing y", "m", "1.5", "Across the rack (back to back)."),
            FieldRow("plate", "Base plate width", "mm", "165", "For punching shear."),
            FieldRow("lf", "Load factor for punching", "", "1.5", "P* = factor x P (e.g. 1.5 for live load)."),
        }));
        slab.Controls.Add(Section("udl", "Storage (distributed load)", false, new[]
        {
            FieldRow("udl", "Distributed load", "kPa", "30", "Block-stacked goods, bulk storage."),
            FieldRow("aisle", "Stack or aisle width", "m", "4", ""),
            FieldRow("udlreps", "Loadings over the life", "", "1000", "0 if not known (uses 0.75)."),
        }));

        var showAdvanced = new LinkLabel { Text = "Advanced factors ▾", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        showAdvanced.LinkClicked += (_, _) =>
        {
            _advanced.Visible = !_advanced.Visible;
            showAdvanced.Text = _advanced.Visible ? "Advanced factors ▴" : "Advanced factors ▾";
        };
        slab.Controls.Add(showAdvanced);
        var adv = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top };
        adv.Controls.Add(FieldRow("k1w", "k1 wheels", "", "0.85", "Material factor, 0.85-0.95."));
        adv.Controls.Add(FieldRow("k3i", "k3 interior", "", "1.2", "Geotechnical calibration (T48)."));
        adv.Controls.Add(FieldRow("k3e", "k3 edge", "", "1.05", "Geotechnical calibration (T48)."));
        adv.Controls.Add(FieldRow("k1p", "k1 posts", "", "0.8", "0.75-0.85."));
        adv.Controls.Add(FieldRow("k2p", "k2 posts", "", "0.85", "T48 uses 1; lower if racks are loaded and unloaded often."));
        adv.Controls.Add(FieldRow("k1u", "k1 distributed load", "", "0.8", "0.75-0.85."));
        _advanced.Controls.Add(adv);
        slab.Controls.Add(_advanced);
        _slabLoads.Controls.Add(slab);
        _slabLoads.Visible = false;

        t.Controls.Add(_roadLoads);
        t.Controls.Add(_slabLoads);
        return page;
    }

    private Control Section(string key, string title, bool on, Control[] rows)
    {
        var box = new GroupBox { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 4, 8, 6), Margin = new Padding(3, 8, 3, 3), Text = "" };
        var inner = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        var check = Check(key, title, on, "");
        check.Font = Brand.Sized(10.5f, FontStyle.Bold);
        check.ForeColor = Brand.Heading;
        inner.Controls.Add(check);
        var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Top, Visible = on, Margin = new Padding(18, 0, 3, 0) };
        foreach (var r in rows) body.Controls.Add(r);
        inner.Controls.Add(body);
        check.CheckedChanged += (_, _) => body.Visible = check.Checked;
        box.Controls.Add(inner);
        return box;
    }

    // ================================================================ step 4: result ===========

    private Panel BuildResult()
    {
        var page = new Panel { Padding = new Padding(4) };
        var draw = new Button { Text = "Draw this build-up", AutoSize = true, Tag = Brand.PrimaryTag };
        draw.Click += (_, _) => { if (_result?.BuildupText is { } t) UseBuildup?.Invoke(t); };
        var report = new Button { Text = "Calculation report…", AutoSize = true };
        report.Click += (_, _) => SaveReport();
        _tips.SetToolTip(report, "A formatted report (inputs, every check, what-if tables, sign-off). Opens in your browser; print it to PDF.");
        var copy = new Button { Text = "Copy as text", AutoSize = true };
        copy.Click += (_, _) => { if (_result is not null) Clipboard.SetText(_result.ToText(ProjectLine(), _engineer.Text)); };
        var save = new Button { Text = "Save design…", AutoSize = true };
        save.Click += (_, _) => SaveDesign();
        var again = new Button { Text = "Start a new design", AutoSize = true };
        again.Click += (_, _) => ShowStep(0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
        actions.Controls.AddRange(new Control[] { draw, report, copy, save, again });
        foreach (Control c in actions.Controls) c.Margin = new Padding(0, 3, 8, 3);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add("label", "Check / step");
        _grid.Columns.Add("value", "Value");
        _grid.Columns.Add("unit", "Unit");
        _grid.Columns.Add("note", "Note");
        _grid.Columns[0].FillWeight = 220;
        _grid.Columns[1].FillWeight = 60;
        _grid.Columns[2].FillWeight = 45;
        _grid.Columns[3].FillWeight = 120;
        _grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Brand.Bg };
        page.HandleCreated += (_, _) => { if (split.Width > 400) split.SplitterDistance = (int)(split.Width * 0.58); };
        var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Brand.Bg };
        left.Panel1.Controls.Add(_grid);
        _tables.Dock = DockStyle.Fill;
        _tables.BackColor = Brand.Surface;
        left.Panel2.Controls.Add(_tables);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_picture);
        split.Panel2.Padding = new Padding(8, 0, 0, 0);

        page.Controls.Add(split);
        page.Controls.Add(actions);
        page.Controls.Add(_headline);
        _headline.Dock = DockStyle.Top;
        return page;
    }

    private void ShowResult()
    {
        Recalculate();
        _grid.Rows.Clear();
        if (_result is null)
        {
            _headline.ForeColor = Brand.Failure;
            _headline.Text = "✖ " + (_error ?? "Fill in the steps first.");
            _tables.Text = "";
            return;
        }
        _headline.ForeColor = _result.Pass == false ? Brand.Failure : Brand.Success;
        _headline.Text = (_result.Pass == false ? "✖ " : "✔ ") + _result.Headline;
        foreach (var line in _result.Lines.Where(l => l.Kind != LineKind.Input))
        {
            int row = _grid.Rows.Add(line.Label, line.Value, line.Unit, line.Reference);
            var style = _grid.Rows[row].DefaultCellStyle;
            switch (line.Kind)
            {
                case LineKind.Result: style.Font = Brand.Body(FontStyle.Bold); break;
                case LineKind.Ok: style.ForeColor = Brand.Success; style.Font = Brand.Body(FontStyle.Bold); break;
                case LineKind.Fail or LineKind.Warning: style.ForeColor = Brand.Failure; style.Font = Brand.Body(FontStyle.Bold); break;
            }
        }
        var sb = new System.Text.StringBuilder();
        foreach (var (title, columns, rows) in _result.Tables)
        {
            sb.AppendLine(title.ToUpperInvariant());
            var widths = columns.Select((c, i) => Math.Max(c.Length, rows.Max(r => r[i].Length)) + 2).ToArray();
            sb.AppendLine(string.Concat(columns.Select((c, i) => c.PadRight(widths[i]))));
            foreach (var r in rows) sb.AppendLine(string.Concat(r.Select((c, i) => c.PadRight(widths[i]))));
            sb.AppendLine();
        }
        if (_result.Notes.Count > 0)
        {
            sb.AppendLine("NOTES");
            foreach (var n in _result.Notes.Distinct()) sb.AppendLine("• " + n);
        }
        _tables.Text = sb.ToString().Replace("\n", Environment.NewLine);

        if (_result.BuildupText is { } text)
        {
            var read = SmartReader.Read(text, new List<string>());
            _picture.UpdatePreview(read.Parsed.ToBuildup(read.Parsed.Name ?? "PAVEMENT"), new DetailSettings { ScaleDenominator = 10 }, CadStandard.CreateDefault());
        }
    }

    // ================================================================ calculation ==============

    private double V(string key) => _fields[key].Value;

    private double? Optional(string key) => _fields[key].Box.Text.Trim().Length == 0 ? null : _fields[key].Value;

    private double Desa()
    {
        var text = _desa.Text;
        int i = _desa.Items.IndexOf(text);
        if (i >= 0) return FlexibleDesign.TrafficRows[i].Desa;
        var head = text.Split(new[] { "  -  " }, StringSplitOptions.None)[0];
        if (Inputs.TryRead(head, "ESA", out var v, out var error)) return v;
        throw new ArgumentException("Design traffic: " + error);
    }

    private void Recalculate()
    {
        if (_fields.Count == 0 || !_checks.ContainsKey("wheels")) return;
        _error = null;
        try
        {
            if (!_fields["cbr"].Ok) throw new ArgumentException("Check the subgrade CBR (step 2).");
            string? title = string.IsNullOrWhiteSpace(_project.Text) ? null : _project.Text.Trim().ToUpperInvariant();
            if (_kind == Kind.Road)
            {
                var traffic = _knownDesa.Checked
                    ? new TrafficInputs(Desa())
                    : new TrafficInputs(0, V("aadt"), V("hv"), V("df"), V("ldf"), V("growth"), V("years"), V("esahv"));
                _result = RoadDesign.Design(V("cbr"), traffic, title);
            }
            else
            {
                double fc = ConcreteDesign.Grades[Math.Max(0, _grade.SelectedIndex)];
                var s = new SlabInputs
                {
                    Fc = fc, Cbr = V("cbr"), EsOverride = Optional("es") ?? 0, SoilDepth = V("h"),
                    CheckEdges = _checks["edges"].Checked, T34Checks = _checks["t34"].Checked,
                    Wheels = _checks["wheels"].Checked, AxleTonnes = V("axle"), WheelSpacing = V("ws"), PassesPerDay = V("passes"),
                    Years = V("life"), TyreKpa = V("tyre"), K1Wheel = V("k1w"), K3Interior = V("k3i"), K3Edge = V("k3e"),
                    Posts = _checks["posts"].Checked, PostKn = V("post"), PostX = V("px"), PostY = V("py"), BasePlateMm = V("plate"),
                    LoadFactor = V("lf"), K1Post = V("k1p"), K2Post = V("k2p"),
                    Udl = _checks["udl"].Checked, UdlKpa = V("udl"), AisleWidth = V("aisle"), UdlRepetitions = V("udlreps"), K1Udl = V("k1u"),
                };
                _result = SlabDesign.Design(s, title);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            _result = null;
            _error = ex.Message;
        }
        UpdateDerived();
        UpdateLive();
    }

    private void UpdateDerived()
    {
        var cbr = _fields["cbr"];
        _soilDerived.Text = cbr.Ok && cbr.Value > 0
            ? $"→ k ≈ {ConcreteDesign.KFromCbr(cbr.Value):0.0} kPa/mm,  Es ≈ {ConcreteDesign.EsFromCbr(cbr.Value):0.0} MPa" +
              (cbr.Value < 3 ? "   ·  CBR < 3%: subgrade stabilisation is generally required." : "")
            : "";
    }

    private void UpdateLive()
    {
        if (_step == 0)
        {
            _live.ForeColor = Brand.Muted;
            _live.Text = "Fill in the job, choose what you're designing, then Next.";
            return;
        }
        if (_result is null)
        {
            _live.ForeColor = Brand.Failure;
            _live.Text = "✖ " + (_error ?? "");
            return;
        }
        _live.ForeColor = _result.Pass == false ? Brand.Failure : Brand.Success;
        _live.Text = (_step < 3 ? "Live result: " : "") + _result.Headline;
    }

    // ================================================================ report / save / open =====

    private string ProjectLine() =>
        string.Join("  ·  ", new[] { _project.Text.Trim(), _jobNo.Text.Trim().Length > 0 ? "Job " + _jobNo.Text.Trim() : "" }.Where(s => s.Length > 0));

    private void SaveReport()
    {
        if (_result is null) return;
        using var dlg = new SaveFileDialog
        { InitialDirectory = AppFolders.Today,
            Filter = "Calculation report (*.html)|*.html", Title = "Save the calculation report",
            FileName = SafeName($"{_jobNo.Text} {_project.Text} {(_kind == Kind.Road ? "pavement" : "slab")} design".Trim()) + ".html",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.TidyToday(); // cancelled: no empty date folder left behind
            return;
        }
        try
        {
            File.WriteAllText(dlg.FileName, CalcReport.Html(_result, ProjectLine(), _engineer.Text, _checker.Text));
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not save or open the report: " + ex.Message, "TTW Pavement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed class SavedDesign
    {
        public string Kind { get; set; } = "Road";
        public string Project { get; set; } = "";
        public string JobNo { get; set; } = "";
        public string Engineer { get; set; } = "";
        public string Checker { get; set; } = "";
        public string Desa { get; set; } = "";
        public bool FromCounts { get; set; }
        public int Grade { get; set; } = 1;
        public Dictionary<string, string> Fields { get; set; } = new();
        public Dictionary<string, bool> Checks { get; set; } = new();
    }

    private void SaveDesign()
    {
        var saved = new SavedDesign
        {
            Kind = _kind.ToString(), Project = _project.Text, JobNo = _jobNo.Text, Engineer = _engineer.Text, Checker = _checker.Text,
            Desa = _desa.Text, FromCounts = _countDesa.Checked, Grade = _grade.SelectedIndex,
            Fields = _fields.ToDictionary(f => f.Key, f => f.Value.Box.Text),
            Checks = _checks.ToDictionary(c => c.Key, c => c.Value.Checked),
        };
        using var dlg = new SaveFileDialog
        { InitialDirectory = AppFolders.Today,
            Filter = "TTW pavement design (*.ttwdesign)|*.ttwdesign", Title = "Save the design (to open and change later)",
            FileName = SafeName($"{_jobNo.Text} {_project.Text}".Trim() is { Length: > 0 } n ? n : "pavement design") + ".ttwdesign",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.TidyToday(); // cancelled: no empty date folder left behind
            return;
        }
        try { File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, "TTW Pavement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenDesign()
    {
        using var dlg = new OpenFileDialog { InitialDirectory = AppFolders.Exports, Filter = "TTW pavement design (*.ttwdesign)|*.ttwdesign", Title = "Open a saved design" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedDesign>(File.ReadAllText(dlg.FileName)) ?? throw new InvalidDataException("The file is empty.");
            _project.Text = saved.Project;
            _jobNo.Text = saved.JobNo;
            _engineer.Text = saved.Engineer;
            _checker.Text = saved.Checker;
            foreach (var (key, text) in saved.Fields)
                if (_fields.TryGetValue(key, out var f)) f.Box.Text = text;
            foreach (var (key, on) in saved.Checks)
                if (_checks.TryGetValue(key, out var c)) c.Checked = on;
            _desa.Text = saved.Desa;
            _countDesa.Checked = saved.FromCounts;
            _knownDesa.Checked = !saved.FromCounts;
            if (saved.Grade >= 0 && saved.Grade < _grade.Items.Count) _grade.SelectedIndex = saved.Grade;
            (saved.Kind == "Slab" ? _slab : _road).Checked = true;
            SetKind(saved.Kind == "Slab" ? Kind.Slab : Kind.Road);
            GoTo(3);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not open the design: " + ex.Message, "TTW Pavement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenSingleChecks()
    {
        using var form = new Form { Text = "Single checks (advanced)", Size = new Size(Brand.Px(1150), Brand.Px(720)), StartPosition = FormStartPosition.CenterParent };
        var panel = new DesignPanel();
        panel.UseBuildup += text => { form.Close(); UseBuildup?.Invoke(text); };
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };
        host.Controls.Add(panel);
        form.Controls.Add(host);
        Brand.Decorate(form, "Single checks", "Each workbook calculation on its own");
        host.BringToFront();
        form.ShowDialog(this);
    }

    private static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "design" : s;
    }

    // ================================================================ building blocks ==========

    private static Panel Page(out TableLayoutPanel table)
    {
        var page = new Panel { AutoScroll = true, Padding = new Padding(4, 0, 4, 0) };
        table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        page.Controls.Add(table);
        return page;
    }

    /// <summary>"Label  [ box ] unit" with a help line underneath that turns red when the value is wrong.</summary>
    private Control FieldRow(string key, string label, string unit, string value, string help)
    {
        var box = new TextBox { Text = value, Width = Brand.Px(130), TextAlign = HorizontalAlignment.Right, BackColor = Brand.Surface, BorderStyle = BorderStyle.FixedSingle };
        var helpLabel = new Label { Text = help, AutoSize = true, ForeColor = Brand.Muted, Font = Brand.Sized(8.5f), Tag = label, MaximumSize = new Size(Brand.Px(620), 0) };
        var field = new Field { Key = key, Unit = unit, Box = box, Help = helpLabel, HelpText = help };
        _fields[key] = field;
        box.TextChanged += (_, _) => { Validate(field); Recalculate(); };
        Validate(field);

        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 2) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Brand.Px(250)));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 6, 0) }, 0, 0);
        t.Controls.Add(box, 1, 0);
        t.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(4, 6, 3, 0) }, 2, 0);
        t.Controls.Add(helpLabel, 1, 1);
        t.SetColumnSpan(helpLabel, 2);
        return t;
    }

    private static void Validate(Field f)
    {
        var text = f.Box.Text.Trim();
        if (text.Length == 0 && f.Key == "es")
        {
            f.Ok = true;
            f.Value = 0;
        }
        else if (Inputs.TryRead(text, f.Unit is "veh/day" or "years" or "" ? "" : f.Unit, out var v, out var error))
        {
            f.Ok = v >= 0;
            f.Value = v;
            if (!f.Ok) error = "Can't be negative.";
            f.Help.Text = f.Ok ? f.HelpText : error;
        }
        else
        {
            f.Ok = false;
            f.Help.Text = error;
        }
        if (text.Length == 0 && f.Key == "es") f.Help.Text = f.HelpText;
        f.Help.ForeColor = f.Ok ? Brand.Muted : Brand.Failure;
        f.Box.BackColor = f.Ok ? Brand.Surface : Color.FromArgb(251, 236, 232);
    }

    private CheckBox Check(string key, string text, bool on, string tip)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Checked = on, Margin = new Padding(12, 6, 3, 3) };
        if (tip.Length > 0) _tips.SetToolTip(c, tip);
        c.CheckedChanged += (_, _) => Recalculate();
        _checks[key] = c;
        return c;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        foreach (var c in controls) c.Margin = new Padding(3, c is Label ? 7 : 3, 10, 3);
        f.Controls.AddRange(controls);
        return f;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true };

    private static Label Hint(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Brand.Muted, Font = Brand.Sized(8.5f), MaximumSize = new Size(Brand.Px(700), 0), Margin = new Padding(3, 0, 3, 6),
    };
}
