using System.Globalization;
using System.Text;
using ETWalls.Core.Design;
using ETWalls.Reports;
using WallSite = ETWalls.Core.Design.Site;

namespace ETWalls.Standalone;

/// <summary>
/// The Design tab: 1 Project, 2 Wall, 3 Soil &amp; loads, 4 Result. The wall is sized for the lowest cost and
/// checked again as you type; the result page has every check, the bar schedule, the cost, the options and the
/// what-if tables. Same steps as the preview page.
/// </summary>
internal sealed class DesignWizard : UserControl
{
    private readonly DesignState _state;
    private readonly Settings _settings;
    private readonly ToolTip _tips = new();
    private int _step;
    private bool _loading;

    private readonly Button[] _stepButtons = new Button[4];
    private readonly Panel[] _pages = new Panel[4];
    private readonly FlowLayoutPanel[] _lefts = new FlowLayoutPanel[4];
    private readonly SectionView[] _previews = new SectionView[4];
    private readonly Button _back = new() { Text = "◀  Back", AutoSize = true };
    private readonly Button _next = new() { Text = "Next  ▶", AutoSize = true, Tag = Brand.PrimaryTag };

    /// <summary>"Draw this wall": show it on the Detail tab.</summary>
    public event Action? DrawRequested;
    /// <summary>A message for the status line.</summary>
    public event Action<string>? Status;

    // Step 1
    private readonly TextBox _project = Box(), _job = Box(), _wall = Box(), _by = Box(), _chk = Box();
    private readonly System.Windows.Forms.Label _folder = new() { AutoSize = true, ForeColor = Brand.Muted, Font = new Font("Consolas", 9f) };
    private readonly RadioButton[] _types = new RadioButton[4];

    // Step 2
    private readonly NumberField _h, _slope, _length, _d, _tb, _toe, _heel, _t1, _t2, _key, _bg, _bt, _gw, _s, _dia, _dp;
    private readonly RadioButton _keyAuto = Seg("Use one if it's cheaper"), _keyNone = Seg("No shear key");
    private readonly FlowLayoutPanel _keyRow = Row();
    private readonly RadioButton _cut = Seg("Cut: digging out behind"), _fill = Seg("Fill: building up behind");
    private readonly RadioButton _heelFoot = Seg("Under the retained soil (heel)"), _toeFoot = Seg("In front only (no heel)");
    private readonly RadioButton _facesLeft = Seg("Wall faces left"), _facesRight = Seg("Wall faces right");
    private readonly FlowLayoutPanel _footingRow = Row();
    private readonly System.Windows.Forms.Label _footingHint = Hint("");
    private readonly System.Windows.Forms.Label _autoBox = new() { AutoSize = true, Padding = new Padding(10), BackColor = Brand.Surface, BorderStyle = BorderStyle.FixedSingle, MaximumSize = new Size(Brand.Px(560), 0) };
    private readonly LinkLabel _customLink = new() { AutoSize = true, Text = "Custom wall size ▾", Margin = new Padding(3, 8, 3, 3) };
    private readonly FlowLayoutPanel _customPanel = Column();
    private readonly ComboBox _post = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(160) };

    // Step 3
    private readonly NumberField _phi, _gamma, _phiF, _gammaF, _cF, _delta, _qa, _q, _hw, _kh, _fc, _cover;
    private readonly CheckBox _drained = new() { Text = "Drained backfill (ag pipe and free-draining fill)", AutoSize = true, Checked = true };
    private readonly CheckBox _passive = new() { Text = "Count soil in front of the wall (half passive)", AutoSize = true };
    private readonly ComboBox _bar = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(260) };
    private readonly ComboBox _margin = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(300) };
    private readonly LinkLabel _ratesLink = new() { AutoSize = true, Text = "Rates for the cost estimate ▾", Margin = new Padding(3, 8, 3, 3) };
    private readonly FlowLayoutPanel _ratesPanel = Column();
    private readonly Dictionary<string, NumberField> _rates = new();
    private readonly FlowLayoutPanel _concrete = Column();

    // Step 4
    private readonly System.Windows.Forms.Label _verdict = new() { AutoSize = true, Padding = new Padding(12), MaximumSize = new Size(Brand.Px(640), 0), Font = Brand.Sized(10.5f) };
    private readonly DataGridView _checks = Grid(("Check", 170, false), ("Value", 110, true), ("Needs", 90, true), ("Used", 60, true), ("", 60, false), ("Working (click a row)", 360, false));
    private readonly DataGridView _reo = Grid(("", 30, false), ("Where", 230, false), ("Bars", 110, false), ("Needs", 70, true), ("Gives", 70, true), ("kg", 60, true), ("Note", 300, false));
    private readonly DataGridView _cost = Grid(("Item", 260, false), ("Per metre", 100, true), ("Rate", 90, true), ("$ per m", 90, true), ("$ for the wall", 110, true));
    private readonly DataGridView _options = Grid(("Option", 200, false), ("Size (mm)", 330, false), ("$ per m", 80, true), ("vs now", 80, true), ("Used", 60, true));
    private readonly DataGridView _whatif = Grid(("", 120, true));
    private readonly DataGridView _phiGrid = Grid(("Backfill φ", 110, false));
    private readonly System.Windows.Forms.Label _reoNote = Hint(""), _costNote = Hint(""), _optionsNote = Hint("All of these pass every check. \"Used\" is the highest check.");

    public DesignWizard(DesignState state, Settings settings)
    {
        _state = state; _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Brand.Bg;

        // Limits, as in the preview page.
        string? Pos(double v) => v <= 0 ? "Must be more than 0." : null;
        string? NonNeg(double v) => v < 0 ? "Can't be negative." : null;
        string? Friction(double v) => v is < 10 or > 50 ? "Friction angle should be 10° to 50°." : null;
        _h = F("Retained height", "m", "Ground in front to ground behind, at the wall.", ValueKind.Length, v => v <= 0 ? "Must be more than 0." : v > 8 ? "Over 8 m: get a specialist design." : null, (i, v) => i.H = v);
        _slope = F("Backfill slope", "°", "0 for level ground behind the wall.", ValueKind.Angle, v => v < 0 || v >= _state.Input.Phi ? "Slope must be flatter than the backfill φ." : null, (i, v) => i.Slope = v);
        _length = F("Wall length", "m", "Along the wall. Used for the total cost.", ValueKind.Number, Pos, (i, v) => i.Length = v);
        _d = F("Depth below front ground", "m", "Underside of the footing below the ground in front.", ValueKind.Length, NonNeg, (i, v) => i.D = v);
        _tb = F("Base thickness", "m", "", ValueKind.Length, Pos, (i, v) => i.Tb = v);
        _toe = F("Toe length", "m", "Footing in front of the stem.", ValueKind.Length, NonNeg, (i, v) => i.Toe = v);
        _heel = F("Heel length", "m", "Footing behind the stem. Longer heel = more soil holding it down.", ValueKind.Length, NonNeg, (i, v) => i.Heel = v);
        _t1 = F("Stem thickness, top", "m", "", ValueKind.Length, Pos, (i, v) => i.T1 = v);
        _t2 = F("Stem thickness, bottom", "m", "", ValueKind.Length, v => v <= 0 ? "Must be more than 0." : v < _state.Input.T1 ? "Bottom can't be thinner than the top." : null, (i, v) => i.T2 = v);
        _key = F("Shear key depth", "m", "Below the underside of the base, under the stem. 0 = no key.", ValueKind.Length, v => v < 0 ? "Can't be negative." : v > 1.5 ? "Keep the key under 1.5 m deep." : null, (i, v) => i.Key = v);
        _bg = F("Base width", "m", "At the bottom of the wall.", ValueKind.Length, Pos, (i, v) => i.Bg = v);
        _bt = F("Top width", "m", "Front face is battered, back face vertical.", ValueKind.Length, v => v <= 0 ? "Must be more than 0." : v > _state.Input.Bg ? "Top can't be wider than the base." : null, (i, v) => i.Bt = v);
        _gw = F("Wall unit weight", "kN/m³", "Mass concrete 23-24, filled blocks 20-22.", ValueKind.Number, Pos, (i, v) => i.GammaWall = v);
        _s = F("Post spacing", "m", "Centre to centre (= sleeper span).", ValueKind.Length, Pos, (i, v) => i.S = v);
        _dia = F("Pier diameter", "m", "Bored concrete pier around the post.", ValueKind.Length, Pos, (i, v) => i.Dia = v);
        _dp = F("Pier embedment", "m", "Below the ground in front.", ValueKind.Length, Pos, (i, v) => i.Dp = v);
        _phi = F("Backfill friction angle φ", "°", "", ValueKind.Angle, Friction, (i, v) => i.Phi = v);
        _gamma = F("Backfill unit weight γ", "kN/m³", "", ValueKind.Number, Pos, (i, v) => i.Gamma = v);
        _phiF = F("Foundation φ", "°", "Soil under the footing / around the piers.", ValueKind.Angle, Friction, (i, v) => i.PhiF = v);
        _gammaF = F("Foundation γ", "kN/m³", "", ValueKind.Number, Pos, (i, v) => i.GammaF = v);
        _cF = F("Foundation cohesion c'", "kPa", "0 unless the geotech report gives one.", ValueKind.Number, NonNeg, (i, v) => i.CF = v);
        _delta = F("Base friction angle δ", "°", "Concrete cast on soil: usually φ. Precast or blocks: 2/3 φ.", ValueKind.Angle, Friction, (i, v) => i.Delta = v);
        _qa = F("Allowable bearing", "kPa", "From the geotechnical report.", ValueKind.Number, Pos, (i, v) => i.Qa = v);
        _q = F("Surcharge behind", "kPa", "5 landscaping, 10 cars, 20 trucks.", ValueKind.Number, NonNeg, (i, v) => i.Q = v);
        _hw = F("Water height behind", "m", "Above the underside of the footing.", ValueKind.Length, NonNeg, (i, v) => i.Hw = v);
        _kh = F("Earthquake coefficient kh", "", "0 to skip. AS 1170.4: often 0.05-0.15 (kh = kp Z).", ValueKind.Number, v => v is < 0 or > 0.5 ? "Use 0 to 0.5." : null, (i, v) => i.Kh = v);
        _fc = F("Concrete f'c", "MPa", "", ValueKind.Number, Pos, (i, v) => i.Fc = v);
        _cover = F("Cover to bars", "mm", "50 cast against formwork in soil, 65-75 cast on ground.", ValueKind.Number, NonNeg, (i, v) => i.Cover = v);
        foreach (var (key, name, unit, help, set) in RateFields())
            _rates[key] = F(name, unit, help, ValueKind.Number, NonNeg, (i, v) => set(i.Rates, v));

        var host = new Panel { Dock = DockStyle.Fill };
        for (int i = 0; i < 4; i++)
        {
            _lefts[i] = Column();
            _lefts[i].Padding = new Padding(0, 0, 12, 12);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(_lefts[i]);
            int idx = i;
            scroll.Resize += (_, _) => FitWidths(_lefts[idx], scroll.ClientSize.Width - Brand.Px(16));
            _previews[i] = new SectionView { Dock = DockStyle.Fill, Margin = new Padding(8, 8, 0, 8) };
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(22, 4, 22, 0) };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            split.Controls.Add(scroll, 0, 0);
            var previewBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Brand.Border, Margin = new Padding(8, 12, 0, 12) };
            previewBox.Controls.Add(_previews[i]);
            split.Controls.Add(previewBox, 1, 0);
            _pages[i] = new Panel { Dock = DockStyle.Fill, Visible = i == 0 };
            _pages[i].Controls.Add(split);
            host.Controls.Add(_pages[i]);
        }
        BuildProject(_lefts[0]);
        BuildWall(_lefts[1]);
        BuildSoil(_lefts[2]);
        BuildResult(_lefts[3]);

        Controls.Add(host);
        Controls.Add(BuildFooter());
        Controls.Add(BuildStepBar());

        _state.Validate = FirstProblem;
        _state.Computed += OnComputed;
        LoadFrom(_state.Input);
    }

    // ---------- building the steps ----------

    private Control BuildStepBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22, 12, 22, 0), WrapContents = false };
        string[] names = { "Project", "Wall", "Soil & loads", "Result" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            _stepButtons[i] = new Button { Text = $"{i + 1}  {names[i]}", AutoSize = true, Tag = Brand.SelfPaintTag, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 4, 0), Padding = new Padding(10, 4, 10, 4), Cursor = Cursors.Hand };
            _stepButtons[i].Click += (_, _) => Go(idx);
            bar.Controls.Add(_stepButtons[i]);
        }
        return bar;
    }

    private Control BuildFooter()
    {
        var foot = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(22, 8, 22, 8) };
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foot.Paint += (_, e) => { using var p = new Pen(Brand.Border); e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
        _back.Click += (_, _) => Go(_step - 1);
        _next.Click += (_, _) => Go(_step + 1);
        _back.Anchor = AnchorStyles.Left; _next.Anchor = AnchorStyles.Right;
        foot.Controls.Add(_back, 0, 0);
        foot.Controls.Add(_next, 1, 0);
        return foot;
    }

    private void BuildProject(FlowLayoutPanel p)
    {
        p.Controls.Add(Brand.Step("Which job is this for?"));
        p.Controls.Add(TextRow("Project", _project, (i, t) => i.Project = t));
        p.Controls.Add(TextRow("Job no.", _job, (i, t) => i.JobNumber = t));
        p.Controls.Add(TextRow("Wall name", _wall, (i, t) => i.WallName = t));
        p.Controls.Add(TextRow("Designed by", _by, (i, t) => { i.DesignedBy = t; _settings.DesignedBy = t; }));
        p.Controls.Add(TextRow("Checked by", _chk, (i, t) => i.CheckedBy = t));
        p.Controls.Add(Hint("These go on the calculation report. Files are saved in:"));
        p.Controls.Add(_folder);

        p.Controls.Add(Brand.Step("What kind of wall?"));
        (WallType t, string name, string blurb)[] types =
        {
            (WallType.Cantilever, "Cantilever concrete wall", "Reinforced concrete stem on a footing with toe and heel. AS 3600 stem design."),
            (WallType.Block, "Block wall (gravity)", "Segmental or core-filled blocks acting as a gravity wall. No geogrid."),
            (WallType.Gravity, "Mass gravity wall", "Mass concrete or rock-filled. Weight alone holds the soil back."),
            (WallType.Post, "Sleeper and post wall", "Steel posts in bored piers with sleepers between. Broms embedment."),
        };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        for (int i = 0; i < 4; i++)
        {
            var (t, name, blurb) = types[i];
            var rb = new RadioButton
            {
                Text = name + Environment.NewLine + blurb, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.TopLeft,
                Size = new Size(Brand.Px(300), Brand.Px(70)), Margin = new Padding(0, 0, 8, 8), Padding = new Padding(8, 6, 8, 6), Tag = Brand.SelfPaintTag, Cursor = Cursors.Hand,
            };
            rb.FlatAppearance.BorderColor = Brand.Border;
            rb.FlatAppearance.CheckedBackColor = Brand.Selection;
            rb.BackColor = Brand.Surface;
            rb.CheckedChanged += (_, _) =>
            {
                rb.FlatAppearance.BorderColor = rb.Checked ? Brand.Orange : Brand.Border;
                if (rb.Checked && !_loading) { _state.Edit(x => x.Type = t); UpdateVisibility(); }
            };
            _types[i] = rb;
            grid.Controls.Add(rb, i % 2, i / 2);
        }
        p.Controls.Add(grid);
        var open = new Button { Text = "Open a saved design…", AutoSize = true };
        open.Click += (_, _) => OpenDesign();
        var save = new Button { Text = "Save design…", AutoSize = true };
        save.Click += (_, _) => SaveDesign();
        var row = Row(); row.Controls.Add(open); row.Controls.Add(save);
        p.Controls.Add(row);
    }

    private void BuildWall(FlowLayoutPanel p)
    {
        p.Controls.Add(Brand.Step("How high is the wall?"));
        p.Controls.Add(Hint("Type m or mm (2.4 or 2400mm)."));
        p.Controls.Add(_h); p.Controls.Add(_slope); p.Controls.Add(_length);
        p.Controls.Add(Choice("Site", _cut, _fill, "Cut sites pay for digging behind the wall and backfilling it.", () => _state.Edit(i => i.Site = _cut.Checked ? WallSite.Cut : WallSite.Fill)));
        var footing = Choice("Footing", _heelFoot, _toeFoot, "", () => { _state.Edit(i => i.Footing = _toeFoot.Checked ? Footing.Toe : Footing.Heel); UpdateVisibility(); });
        _footingRow.Controls.Add(footing);
        p.Controls.Add(_footingRow);
        _keyRow.Controls.Add(Choice("Shear key", _keyAuto, _keyNone, "A key under the base stops the wall sliding. Often cheaper than a longer heel or toe.", () => _state.Edit(i => i.AutoKey = _keyAuto.Checked)));
        p.Controls.Add(_keyRow);
        p.Controls.Add(Choice("Drawing direction", _facesLeft, _facesRight, "Only mirrors the drawing. The design is the same.", () => _state.Edit(i => i.FacesRight = _facesRight.Checked)));
        p.Controls.Add(_autoBox);
        _customLink.LinkClicked += (_, _) => ToggleCustom();
        p.Controls.Add(_customLink);
        _customPanel.Controls.Add(Hint("Started from the automatic sizes. Click \"Go back to automatic sizes\" to undo."));
        foreach (var f in new[] { _d, _tb, _toe, _heel, _t1, _t2, _key, _bg, _bt, _gw, _s, _dia, _dp }) _customPanel.Controls.Add(f);
        _post.Items.AddRange(WallDesign.Posts.Keys.Cast<object>().ToArray());
        _post.SelectedIndexChanged += (_, _) => { if (!_loading && _post.SelectedItem is string s) _state.Edit(i => i.Post = s); };
        var postRow = Row(); postRow.Controls.Add(new System.Windows.Forms.Label { Text = "Post size", AutoSize = true, Width = Brand.Px(190), Margin = new Padding(0, 6, 6, 0), MinimumSize = new Size(Brand.Px(190), 0) }); postRow.Controls.Add(_post);
        postRow.Name = "postRow";
        _customPanel.Controls.Add(postRow);
        _customPanel.Visible = false;
        p.Controls.Add(_customPanel);
    }

    private void BuildSoil(FlowLayoutPanel p)
    {
        p.Controls.Add(Brand.Step("What is the soil like?"));
        p.Controls.Add(Hint("Quick picks for the backfill:"));
        var picks = Row();
        foreach (var (name, phi, gamma) in new[] { ("Clay", 24, 18), ("Sandy clay", 28, 19), ("Sand", 32, 19), ("Gravel / crushed rock", 38, 21) })
        {
            var b = new Button { Text = $"{name} {phi}° / {gamma}", AutoSize = true, Margin = new Padding(0, 0, 4, 4) };
            b.Click += (_, _) => { _phi.Set(phi); _gamma.Set(gamma); _state.Edit(i => { i.Phi = phi; i.Gamma = gamma; }); };
            picks.Controls.Add(b);
        }
        p.Controls.Add(picks);
        foreach (var f in new[] { _phi, _gamma, _phiF, _gammaF, _cF, _delta, _qa }) p.Controls.Add(f);
        p.Controls.Add(Brand.Step("What loads are on it?"));
        p.Controls.Add(_q);
        _drained.CheckedChanged += (_, _) => { if (_loading) return; _state.Edit(i => i.Drained = _drained.Checked); UpdateVisibility(); };
        p.Controls.Add(_drained);
        p.Controls.Add(_hw);
        p.Controls.Add(_kh);
        _passive.CheckedChanged += (_, _) => { if (!_loading) _state.Edit(i => i.UsePassive = _passive.Checked); };
        p.Controls.Add(_passive);

        _concrete.Controls.Add(Brand.Step("Concrete"));
        _concrete.Controls.Add(_fc); _concrete.Controls.Add(_cover);
        _bar.Items.AddRange(new object[] { "Auto (lightest that works)", "N12 everywhere it's needed", "N16 everywhere it's needed", "N20 everywhere it's needed", "N24 everywhere it's needed" });
        _bar.SelectedIndexChanged += (_, _) => { if (!_loading) _state.Edit(i => i.Bar = _bar.SelectedIndex == 0 ? 0 : new[] { 0, 12, 16, 20, 24 }[_bar.SelectedIndex]); };
        _concrete.Controls.Add(LabelRow("Bar size", _bar));
        _concrete.Controls.Add(Hint("Bar size and spacing for every face are worked out for you. Shrinkage bars stay at the minimum."));
        p.Controls.Add(_concrete);

        p.Controls.Add(Brand.Step("Cost and spare capacity"));
        foreach (var m in new[] { 1.0, 0.95, 0.9, 0.85 }) _margin.Items.Add(DesignRun.MarginText(m));
        _margin.SelectedIndexChanged += (_, _) => { if (!_loading) _state.Edit(i => i.Margin = new[] { 1.0, 0.95, 0.9, 0.85 }[Math.Max(0, _margin.SelectedIndex)]); };
        p.Controls.Add(LabelRow("Spare capacity", _margin));
        p.Controls.Add(Hint("Automatic sizing picks the cheapest wall that keeps every check below this."));
        _ratesLink.LinkClicked += (_, _) => { _ratesPanel.Visible = !_ratesPanel.Visible; _ratesLink.Text = _ratesPanel.Visible ? "Hide the rates ▴" : "Rates for the cost estimate ▾"; };
        p.Controls.Add(_ratesLink);
        _ratesPanel.Controls.Add(Hint("Placeholder rates (AUD, supplied and installed). Put in your own."));
        var rateButtons = Row();
        var reset = new Button { Text = "Reset to the default rates", AutoSize = true };
        reset.Click += (_, _) => { _state.Edit(i => i.Rates = Rates.Defaults); LoadFrom(_state.Input); };
        var mine = new Button { Text = "Save as my rates", AutoSize = true };
        _tips.SetToolTip(mine, "Every new design starts with these rates.");
        mine.Click += (_, _) => { _settings.Rates = _state.Input.Rates.Clone(); _settings.Save(); Status?.Invoke("Saved. New designs start with these rates."); };
        rateButtons.Controls.Add(reset); rateButtons.Controls.Add(mine);
        _ratesPanel.Controls.Add(rateButtons);
        foreach (var f in _rates.Values) _ratesPanel.Controls.Add(f);
        _ratesPanel.Visible = false;
        p.Controls.Add(_ratesPanel);
    }

    private void BuildResult(FlowLayoutPanel p)
    {
        p.Controls.Add(_verdict);
        p.Controls.Add(Brand.Step("Every check"));
        p.Controls.Add(Hint("The Working column shows how each check was worked out."));
        p.Controls.Add(_checks);
        p.Controls.Add(Brand.Step("Reinforcement"));
        p.Controls.Add(_reoNote);
        p.Controls.Add(_reo);
        p.Controls.Add(Brand.Step("Cost estimate"));
        p.Controls.Add(Hint("Quantities per metre of wall and your rates (step 3). A guide for comparing options, not a quote."));
        p.Controls.Add(_cost);
        p.Controls.Add(_costNote);
        p.Controls.Add(Brand.Step("Options compared"));
        p.Controls.Add(_optionsNote);
        _options.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Text = "Use this", UseColumnTextForButtonValue = true, Width = Brand.Px(80), FlatStyle = FlatStyle.Flat });
        _options.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 5) UseOption(e.RowIndex); };
        p.Controls.Add(_options);
        p.Controls.Add(Brand.Step("What if…"));
        p.Controls.Add(Hint("Percent of capacity used. Over 100% fails."));
        p.Controls.Add(_whatif);
        p.Controls.Add(_phiGrid);
        p.Controls.Add(new System.Windows.Forms.Label { Text = DesignRun.MethodNote + " An engineer must check and sign off every result.", AutoSize = true, MaximumSize = new Size(Brand.Px(640), 0), BackColor = Brand.Note, Padding = new Padding(8), Margin = new Padding(0, 10, 0, 10) });

        var buttons = Row();
        var draw = new Button { Text = "Draw this wall", AutoSize = true, Tag = Brand.PrimaryTag };
        draw.Click += (_, _) => DrawRequested?.Invoke();
        var report = new Button { Text = "Calculation report…", AutoSize = true };
        _tips.SetToolTip(report, "Saves the report as HTML (opens in your browser; print it to PDF) and as an Excel workbook next to it, linked to each other.");
        report.Click += (_, _) => SaveReport();
        var copy = new Button { Text = "Copy as text", AutoSize = true };
        copy.Click += (_, _) => CopyAsText();
        var save = new Button { Text = "Save design…", AutoSize = true };
        save.Click += (_, _) => SaveDesign();
        var again = new Button { Text = "Start a new design", AutoSize = true };
        again.Click += (_, _) => NewDesign();
        foreach (var b in new[] { draw, report, copy, save, again }) { b.Margin = new Padding(0, 0, 6, 6); buttons.Controls.Add(b); }
        p.Controls.Add(buttons);
    }

    // ---------- steps ----------

    public void Go(int step)
    {
        step = Math.Clamp(step, 0, 3);
        if (step > _step)
        {
            _state.ComputeNow();
            if (_state.Problem is { } problem && _state.ProblemStep < step)
            {
                step = _state.ProblemStep;
                Status?.Invoke("Check this first: " + problem);
            }
        }
        _step = step;
        for (int i = 0; i < 4; i++)
        {
            _pages[i].Visible = i == step;
            var b = _stepButtons[i];
            b.BackColor = i == step ? Brand.Accent : Brand.Surface;
            b.ForeColor = i == step ? Brand.AccentFg : i < step ? Brand.Success : Brand.Muted;
            b.FlatAppearance.BorderColor = i == step ? Brand.Orange : Brand.Border;
            b.FlatAppearance.BorderSize = 1;
            b.Font = new Font(Font, i == step ? FontStyle.Bold : FontStyle.Regular);
            b.Text = (i < step ? "✔ " : $"{i + 1}  ") + new[] { "Project", "Wall", "Soil & loads", "Result" }[i];
        }
        _back.Enabled = step > 0;
        _next.Visible = step < 3;
        _next.Text = step == 2 ? "See the result  ▶" : "Next  ▶";
        RefreshViews();
    }

    private (string?, int) FirstProblem()
    {
        if (string.IsNullOrWhiteSpace(_state.Input.JobNumber)) return ("Job no.: needed for the export folder.", 0);
        foreach (var f in ActiveFields(1)) if (f.Problem is { } p) return ($"{f.Caption}: {p}", 1);
        foreach (var f in ActiveFields(2)) if (f.Problem is { } p) return ($"{f.Caption}: {p}", 2);
        return (null, 0);
    }

    private IEnumerable<NumberField> ActiveFields(int step)
    {
        var i = _state.Input;
        if (step == 1)
        {
            yield return _h; yield return _slope; yield return _length;
            if (!i.Custom) yield break;
            foreach (var f in CustomFields(i)) yield return f;
        }
        else if (step == 2)
        {
            foreach (var f in new[] { _phi, _gamma, _phiF, _gammaF, _q, _kh }) yield return f;
            if (i.Type != WallType.Post) { yield return _cF; yield return _delta; yield return _qa; }
            if (!i.Drained) yield return _hw;
            if (i.Type == WallType.Cantilever) { yield return _fc; yield return _cover; }
            foreach (var f in RateKeys(i.Type).Select(k => _rates[k])) yield return f;
        }
    }

    private IEnumerable<NumberField> CustomFields(WallInput i) => i.Type switch
    {
        WallType.Cantilever => i.Footing == Footing.Toe ? new[] { _d, _tb, _toe, _t1, _t2, _key } : new[] { _d, _tb, _toe, _heel, _t1, _t2, _key },
        WallType.Post => new[] { _s, _dia, _dp },
        _ => new[] { _d, _bg, _bt, _gw },
    };

    private static string[] RateKeys(WallType t) => t switch
    {
        WallType.Cantilever => new[] { "concrete", "steel", "formwork", "excavation", "backfill", "drainage", "agpipe" },
        WallType.Block => new[] { "blocks", "concrete", "excavation", "backfill", "drainage", "agpipe" },
        WallType.Gravity => new[] { "concrete", "formwork", "excavation", "backfill", "drainage", "agpipe" },
        _ => new[] { "postSteel", "drilling", "concrete", "steel", "sleepers", "drainage", "agpipe" },
    };

    private static IEnumerable<(string Key, string Name, string Unit, string Help, Action<Rates, double> Set)> RateFields() => new (string, string, string, string, Action<Rates, double>)[]
    {
        ("concrete", "Concrete", "$/m³", "Supplied and placed.", (r, v) => r.Concrete = v),
        ("steel", "Reinforcement", "$/kg", "Supplied and fixed.", (r, v) => r.Steel = v),
        ("formwork", "Formwork", "$/m²", "", (r, v) => r.Formwork = v),
        ("excavation", "Excavation", "$/m³", "Dig and cart away.", (r, v) => r.Excavation = v),
        ("backfill", "Compacted backfill", "$/m³", "Behind the wall on cut sites.", (r, v) => r.Backfill = v),
        ("drainage", "Drainage gravel", "$/m³", "Free-draining fill behind the wall.", (r, v) => r.Drainage = v),
        ("agpipe", "Ag pipe", "$/m", "", (r, v) => r.AgPipe = v),
        ("blocks", "Block wall", "$/m³", "Supply, lay and fill.", (r, v) => r.Blocks = v),
        ("postSteel", "Steel posts", "$/kg", "Supplied, galvanised and stood.", (r, v) => r.PostSteel = v),
        ("drilling", "Pier drilling (Ø600)", "$/m", "Per metre deep. Scaled for other sizes.", (r, v) => r.Drilling = v),
        ("sleepers", "Sleepers", "$/m²", "Of wall face.", (r, v) => r.Sleepers = v),
    };

    private static double RateOf(Rates r, string key) => key switch
    {
        "concrete" => r.Concrete, "steel" => r.Steel, "formwork" => r.Formwork, "excavation" => r.Excavation, "backfill" => r.Backfill,
        "drainage" => r.Drainage, "agpipe" => r.AgPipe, "blocks" => r.Blocks, "postSteel" => r.PostSteel, "drilling" => r.Drilling, _ => r.Sleepers,
    };

    private void UpdateVisibility()
    {
        var i = _state.Input;
        SuspendLayout();
        _footingRow.Visible = i.Type == WallType.Cantilever;
        _footingHint.Text = i.Footing == Footing.Toe
            ? "For a boundary, or where you can't dig behind. No soil sits on the footing, so the wall needs a much longer toe (sliding usually governs)."
            : "Standard: the soil on the heel helps hold the wall down.";
        var custom = CustomFields(i).ToHashSet();
        foreach (var f in new[] { _d, _tb, _toe, _heel, _t1, _t2, _key, _bg, _bt, _gw, _s, _dia, _dp }) f.Visible = custom.Contains(f);
        _keyRow.Visible = i.Type == WallType.Cantilever && !i.Custom;
        _customPanel.Controls["postRow"]!.Visible = i.Type == WallType.Post;
        _customPanel.Visible = i.Custom;
        _customLink.Text = i.Custom ? "Go back to automatic sizes" : "Custom wall size ▾";
        foreach (var f in new[] { _cF, _delta, _qa }) f.Visible = i.Type != WallType.Post;
        _passive.Visible = i.Type != WallType.Post;
        _hw.Visible = !i.Drained;
        _concrete.Visible = i.Type == WallType.Cantilever;
        var keys = RateKeys(i.Type).ToHashSet();
        foreach (var (k, f) in _rates) f.Visible = keys.Contains(k);
        ResumeLayout();
        _state.Later();
    }

    private void ToggleCustom()
    {
        if (!_state.Input.Custom && _state.Run is { } run)
        {
            // Start the custom size from the automatic one.
            var auto = run.Input;
            _state.Edit(i =>
            {
                i.Custom = true; i.D = auto.D; i.Tb = auto.Tb; i.Toe = auto.Toe; i.Heel = auto.Heel; i.T1 = auto.T1; i.T2 = auto.T2; i.Key = auto.Key;
                i.Bg = auto.Bg; i.Bt = auto.Bt; i.GammaWall = auto.GammaWall; i.S = auto.S; i.Dia = auto.Dia; i.Dp = auto.Dp; i.Post = auto.Post;
            });
        }
        else _state.Edit(i => i.Custom = false);
        LoadFrom(_state.Input);
    }

    private void UseOption(int index)
    {
        if (_state.Run is not { } run || index >= run.Sizing.Options.Count) return;
        var o = run.Sizing.Options[index];
        var applied = o.Sizes.Apply(_state.Input);
        applied.Custom = true;
        _state.Replace(applied);
        LoadFrom(applied);
        Status?.Invoke($"Using \"{o.Label}\". The sizes are now set under Custom wall size in step 2.");
    }

    // ---------- filling the boxes from a design ----------

    public void LoadFrom(WallInput i)
    {
        _loading = true;
        _project.Text = i.Project; _job.Text = i.JobNumber; _wall.Text = i.WallName; _by.Text = i.DesignedBy; _chk.Text = i.CheckedBy;
        _types[(int)i.Type].Checked = true;
        foreach (var rb in _types) rb.FlatAppearance.BorderColor = rb.Checked ? Brand.Orange : Brand.Border;
        _h.Set(i.H); _slope.Set(i.Slope); _length.Set(i.Length);
        (i.Site == WallSite.Fill ? _fill : _cut).Checked = true;
        (i.Footing == Footing.Toe ? _toeFoot : _heelFoot).Checked = true;
        (i.FacesRight ? _facesRight : _facesLeft).Checked = true;
        _d.Set(i.D); _tb.Set(i.Tb); _toe.Set(i.Toe); _heel.Set(i.Heel); _t1.Set(i.T1); _t2.Set(i.T2); _key.Set(i.Key);
        (i.AutoKey ? _keyAuto : _keyNone).Checked = true;
        _bg.Set(i.Bg); _bt.Set(i.Bt); _gw.Set(i.GammaWall); _s.Set(i.S); _dia.Set(i.Dia); _dp.Set(i.Dp);
        _post.SelectedItem = i.Post;
        _phi.Set(i.Phi); _gamma.Set(i.Gamma); _phiF.Set(i.PhiF); _gammaF.Set(i.GammaF); _cF.Set(i.CF); _delta.Set(i.Delta); _qa.Set(i.Qa);
        _q.Set(i.Q); _drained.Checked = i.Drained; _hw.Set(i.Hw); _kh.Set(i.Kh); _passive.Checked = i.UsePassive;
        _fc.Set(i.Fc); _cover.Set(i.Cover);
        _bar.SelectedIndex = i.Bar switch { 12 => 1, 16 => 2, 20 => 3, 24 => 4, _ => 0 };
        _margin.SelectedIndex = i.Margin >= 0.999 ? 0 : i.Margin >= 0.949 ? 1 : i.Margin >= 0.899 ? 2 : 3;
        foreach (var (k, f) in _rates) f.Set(RateOf(i.Rates, k));
        _loading = false;
        UpdateVisibility();
        Go(_step);
    }

    // ---------- results ----------

    private void OnComputed()
    {
        _folder.Text = "Standalone Tool\\_Exports\\" + AppFolders.DayFolder(DateTime.Today) + "\\" + AppFolders.Part(_state.Input.JobNumber, "No job number") + "\\" + AppFolders.Part(_state.Input.WallName, "Untitled") + "\\";
        RefreshViews();
    }

    private void RefreshViews()
    {
        var run = _state.Run;
        var drawing = _state.Drawing(full: false);
        string msg = _state.Problem is { } p ? "Fix this to see the wall: " + p : "Fill in the wall to see it.";
        foreach (var v in _previews) v.SetDrawing(drawing, msg);
        UpdateAutoBox(run);
        if (_step == 3) FillResult(run);
    }

    private void UpdateAutoBox(DesignRun? run)
    {
        if (run is null) { _autoBox.Text = "Sized for you\r\nFill in the height and the soil (step 3) to size the wall."; return; }
        var i = run.Input;
        var sb = new StringBuilder();
        if (i.Custom) sb.AppendLine("Custom size (set below)");
        else sb.AppendLine(run.Sizing.Ok ? "Sized for you: the cheapest wall that passes" : "No standard size works: check the soil and loads, or set the sizes yourself.");
        if (!i.Custom && run.Sizing.Ok) sb.AppendLine($"Picked from {run.Sizing.Tried} sizes that pass every check{(i.Margin < 0.999 ? $" with {Math.Round((1 - i.Margin) * 100)}% spare" : "")}.");
        sb.AppendLine();
        sb.AppendLine(DesignRun.SizeText(i));
        sb.Append($"Estimated cost {ReportRows.Money(run.Cost.PerMetre)} /m, {ReportRows.Money(run.TotalCost)} for {G(i.Length)} m.");
        _autoBox.Text = sb.ToString();
        _autoBox.ForeColor = !i.Custom && !run.Sizing.Ok ? Brand.Failure : Brand.Text;
    }

    private void FillResult(DesignRun? run)
    {
        foreach (var g in new[] { _checks, _reo, _cost, _options, _whatif, _phiGrid }) g.Rows.Clear();
        if (run is null)
        {
            _verdict.Text = "Can't work it out yet. " + (_state.Problem ?? "");
            _verdict.BackColor = Brand.FailureBg; _verdict.ForeColor = Brand.Failure;
            return;
        }
        var i = run.Input; var r = run.Result; var reo = r.R.Reo;
        var failing = r.Checks.Where(c => !c.Pass).ToList();
        _verdict.Text = (r.Pass ? "✔  The wall works." : $"✖  {failing.Count} check{(failing.Count > 1 ? "s fail" : " fails")}: {string.Join(", ", failing.Select(c => c.Name))}.")
            + Environment.NewLine + $"{(string.IsNullOrWhiteSpace(i.WallName) ? "This wall" : i.WallName)} · {DesignRun.TypeName(i.Type)}, {G(i.H)} m retained, {(i.Custom ? "custom size" : "sized automatically for the lowest cost")}."
            + Environment.NewLine + $"Estimated {ReportRows.Money(run.Cost.PerMetre)} per metre ({ReportRows.Money(run.TotalCost)} for {G(i.Length)} m). Governing: {r.Worst.Name} at {Pct(r.Worst.Util)} of capacity."
            + (reo is not null && i.Type == WallType.Cantilever ? $" Main bars: {reo.Bars}, about {Math.Round(reo.TotalKg)} kg of steel per metre." : "");
        _verdict.BackColor = r.Pass ? Brand.SuccessBg : Brand.FailureBg;
        _verdict.ForeColor = r.Pass ? Brand.Success : Brand.Failure;

        foreach (var c in r.Checks)
        {
            int n = _checks.Rows.Add(c.Name, $"{ReportRows.Fmt(c.Value, c.Unit)} {c.Unit}", (c.Unit == "FoS" ? "≥ " : "≤ ") + ReportRows.Fmt(c.Limit, c.Unit), Pct(c.Util), c.Pass ? "PASS" : "FAIL", c.Note);
            Mark(_checks.Rows[n].Cells[4], c.Pass);
        }
        _checks.Rows.Add(i.Type == WallType.Post ? "Sleepers" : "Global stability", "", "", "", "NOTE",
            i.Type == WallType.Post ? "Sleeper bending between posts and the pier concrete are not checked yet." : "Global (overall slope) stability is not checked here. Use Slide or Geo5 where it matters.");

        if (reo is null) _reoNote.Text = "Unreinforced gravity wall: no bars needed.";
        else
        {
            bool post = i.Type == WallType.Post;
            _reoNote.Text = (post ? "Minimum pier steel (the steel post carries the bending)." : "Picked automatically: the lightest bar size and spacing that works for each face, max spacing 300 mm.")
                + " Letters match the drawing. " + (post ? "mm² per pier, kg per pier." : $"mm² per metre, kg per metre of wall. Cover {G(i.Cover)} mm, laps {G(reo.LapA)} mm.");
            foreach (var s in reo.Schedule)
                _reo.Rows.Add(s.Mark, s.Where, s.Bars, s.Req > 0 ? Math.Round(s.Req).ToString(CultureInfo.InvariantCulture) : "–", s.Prov > 0 ? Math.Round(s.Prov).ToString(CultureInfo.InvariantCulture) : "–", s.Kg.ToString("0.0", CultureInfo.InvariantCulture), s.Note);
            int t = _reo.Rows.Add("", "Total steel (laps included, bends ignored)", "", "", "", Math.Round(reo.TotalKg).ToString(CultureInfo.InvariantCulture), post ? "per pier" : "per metre of wall");
            _reo.Rows[t].DefaultCellStyle.Font = new Font(_reo.Font, FontStyle.Bold);
        }
        _reo.Visible = reo is not null;

        foreach (var it in run.Cost.Items)
            _cost.Rows.Add(it.Name, $"{it.Qty.ToString(it.Unit == "kg" ? "0.0" : "0.00", CultureInfo.InvariantCulture)} {it.Unit}", $"{ReportRows.Rate(it.Rate)} /{it.Unit}", ReportRows.Money(it.Cost), ReportRows.Money(it.Cost * i.Length));
        int tot = _cost.Rows.Add("Total", "", "", ReportRows.Money(run.Cost.PerMetre), ReportRows.Money(run.TotalCost));
        _cost.Rows[tot].DefaultCellStyle.Font = new Font(_cost.Font, FontStyle.Bold);
        var opts = run.Sizing.Options;
        _costNote.Text = i.Custom && opts.Count > 0 && run.Cost.PerMetre - opts[0].CostPerMetre > 1
            ? $"This custom size costs {ReportRows.Money(run.Cost.PerMetre - opts[0].CostPerMetre)} more per metre ({ReportRows.Money((run.Cost.PerMetre - opts[0].CostPerMetre) * i.Length)} over {G(i.Length)} m) than the cheapest size that passes."
            : "";
        _costNote.Visible = _costNote.Text.Length > 0;

        var current = Sizes.From(i);
        foreach (var o in opts)
        {
            var oi = o.Sizes.Apply(i);
            double diff = o.CostPerMetre - run.Cost.PerMetre;
            int n = _options.Rows.Add(o.Label, DesignRun.SizeText(oi), ReportRows.Money(o.CostPerMetre), Math.Abs(diff) < 1 ? "–" : (diff > 0 ? "+" : "−") + ReportRows.Money(Math.Abs(diff)), Pct(o.Util));
            if (Sizes.From(oi) == current) { _options.Rows[n].DefaultCellStyle.BackColor = Brand.Selection; _options.Rows[n].Cells[5] = new DataGridViewTextBoxCell { Value = "IN USE" }; }
        }

        var wt = WhatIf.Size(i);
        _whatif.Columns.Clear();
        AddCol(_whatif, wt.Label, 130, true);
        foreach (var c in wt.Columns) AddCol(_whatif, c, 95, true);
        AddCol(_whatif, "Cost /m", 80, true); AddCol(_whatif, "Result", 60, false);
        foreach (var row in wt.Rows)
        {
            var vals = new List<object> { row.Value.ToString("0.00", CultureInfo.InvariantCulture) + (row.Current ? " ◀" : "") };
            vals.AddRange(row.Checks.Select(c => (object)Pct(c.Util)));
            vals.Add(ReportRows.Money(row.CostPerMetre)); vals.Add(row.Pass ? "PASS" : "FAIL");
            int n = _whatif.Rows.Add(vals.ToArray());
            for (int k = 0; k < row.Checks.Count; k++) if (!row.Checks[k].Pass) { _whatif.Rows[n].Cells[k + 1].Style.BackColor = Brand.FailureBg; _whatif.Rows[n].Cells[k + 1].Style.ForeColor = Brand.Failure; }
            Mark(_whatif.Rows[n].Cells[vals.Count - 1], row.Pass);
            if (row.Current) _whatif.Rows[n].DefaultCellStyle.Font = new Font(_whatif.Font, FontStyle.Bold);
        }
        var phi = WhatIf.BackfillPhi(i);
        _phiGrid.Columns.Clear();
        AddCol(_phiGrid, "Backfill φ", 110, false);
        foreach (var x in phi) AddCol(_phiGrid, $"{G(x.Phi)}°", 90, true);
        int w = _phiGrid.Rows.Add(new object[] { "Worst check" }.Concat(phi.Select(x => (object)Pct(x.Worst.Util))).ToArray());
        _phiGrid.Rows.Add(new object[] { "Governs" }.Concat(phi.Select(x => (object)x.Worst.Name.Replace(" (earthquake)", " EQ"))).ToArray());
        for (int k = 0; k < phi.Count; k++) if (!phi[k].Pass) { _phiGrid.Rows[w].Cells[k + 1].Style.BackColor = Brand.FailureBg; _phiGrid.Rows[w].Cells[k + 1].Style.ForeColor = Brand.Failure; }

        foreach (var g in new[] { _checks, _reo, _cost, _options, _whatif, _phiGrid }) FitHeight(g);
    }

    // ---------- files ----------

    private void SaveReport()
    {
        if (_state.Run is not { } run) { Status?.Invoke("Fix this first: " + _state.Problem); return; }
        var folder = AppFolders.ForDesign(run.Input.JobNumber, run.Input.WallName);
        var name = AppFolders.Part(run.Input.WallName, "Wall");
        string html = name + ".html", xlsx = name + ".xlsx";
        try
        {
            File.WriteAllText(Path.Combine(folder, html), HtmlReport.Build(run, xlsx), Encoding.UTF8);
            ExcelReport.Save(run, Path.Combine(folder, xlsx), html);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Couldn't save the report. Is {xlsx} open in Excel? Close it and try again.\n\n{ex.Message}", "ET Walls", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Status?.Invoke($"Saved {html} and {xlsx} in {folder}");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(folder, html)) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no browser registered: the files are still saved */ }
    }

    private void SaveDesign()
    {
        var folder = AppFolders.ForDesign(_state.Input.JobNumber, _state.Input.WallName);
        using var dlg = new SaveFileDialog { Filter = "ET Walls design (*.etwall)|*.etwall", InitialDirectory = folder, FileName = AppFolders.Part(_state.Input.WallName, "Wall") + ".etwall" };
        if (dlg.ShowDialog(this) != DialogResult.OK) { AppFolders.Tidy(folder); return; }
        File.WriteAllText(dlg.FileName, DesignFile.Write(_state.Input));
        Status?.Invoke("Saved " + dlg.FileName);
    }

    private void OpenDesign()
    {
        using var dlg = new OpenFileDialog { Filter = "ET Walls design (*.etwall)|*.etwall", InitialDirectory = AppFolders.Exports };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var input = DesignFile.Read(File.ReadAllText(dlg.FileName));
            _state.Replace(input);
            LoadFrom(input);
            Status?.Invoke("Opened " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Couldn't open that design: " + ex.Message, "ET Walls", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void NewDesign()
    {
        var fresh = MainForm.NewInput(_settings);
        _state.Replace(fresh);
        LoadFrom(fresh);
        Go(0);
    }

    private void CopyAsText()
    {
        if (_state.Run is not { } run) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{run.Input.WallName}: {DesignRun.TypeName(run.Input.Type)}, {G(run.Input.H)} m retained. {DesignRun.SizeText(run.Input)}.");
        foreach (var c in run.Result.Checks) sb.AppendLine($"{c.Name}: {ReportRows.Fmt(c.Value, c.Unit)} {c.Unit} (needs {ReportRows.Fmt(c.Limit, c.Unit)}) {(c.Pass ? "PASS" : "FAIL")}");
        if (run.Result.R.Reo is { } reo) { sb.AppendLine().AppendLine("REINFORCEMENT"); foreach (var s in reo.Schedule) sb.AppendLine($"{s.Mark}  {s.Where}: {s.Bars}. {s.Note}"); }
        sb.AppendLine().AppendLine($"Estimated cost {ReportRows.Money(run.Cost.PerMetre)} per metre, {ReportRows.Money(run.TotalCost)} for {G(run.Input.Length)} m.");
        Clipboard.SetText(sb.ToString());
        Status?.Invoke("Copied the design as text.");
    }

    // ---------- small helpers ----------

    private NumberField F(string label, string unit, string help, ValueKind kind, Func<double, string?> limit, Action<WallInput, double> set)
    {
        var f = new NumberField(label, unit, help, kind, limit);
        f.Changed += () => { if (_loading) return; if (f.Value is double v) _state.Edit(i => set(i, v)); else _state.Later(); };
        return f;
    }

    private Control TextRow(string label, TextBox box, Action<WallInput, string> set)
    {
        box.TextChanged += (_, _) => { if (!_loading) _state.Edit(i => set(i, box.Text.Trim())); };
        return LabelRow(label, box);
    }

    private static Control LabelRow(string label, Control c)
    {
        var row = Row();
        row.Controls.Add(new System.Windows.Forms.Label { Text = label, AutoSize = false, Width = Brand.Px(190), Height = Brand.Px(24), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 2, 0, 0) });
        c.Margin = new Padding(0, 2, 0, 2);
        row.Controls.Add(c);
        return row;
    }

    private Control Choice(string label, RadioButton a, RadioButton b, string hint, Action changed)
    {
        var col = Column();
        var row = Row();
        row.Controls.Add(new System.Windows.Forms.Label { Text = label, AutoSize = false, Width = Brand.Px(190), Height = Brand.Px(24), TextAlign = ContentAlignment.MiddleLeft });
        var pair = Row();
        foreach (var rb in new[] { a, b })
        {
            rb.CheckedChanged += (_, _) =>
            {
                rb.BackColor = rb.Checked ? Brand.Selection : Brand.Surface;
                rb.FlatAppearance.BorderColor = rb.Checked ? Brand.Orange : Brand.Border;
                rb.Font = new Font(rb.Font, rb.Checked ? FontStyle.Bold : FontStyle.Regular);
                if (rb.Checked && !_loading) changed();
            };
            pair.Controls.Add(rb);
        }
        row.Controls.Add(pair);
        col.Controls.Add(row);
        var h = ReferenceEquals(a, _heelFoot) ? _footingHint : Hint(hint);
        h.Margin = new Padding(Brand.Px(190), 0, 0, 6);
        col.Controls.Add(h);
        return col;
    }

    private static RadioButton Seg(string text)
    {
        var rb = new RadioButton { Text = text, Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 4, 0), Padding = new Padding(6, 2, 6, 2), Tag = Brand.SelfPaintTag, Cursor = Cursors.Hand, BackColor = Brand.Surface };
        rb.FlatAppearance.BorderColor = Brand.Border;
        rb.FlatAppearance.CheckedBackColor = Brand.Selection;
        return rb;
    }

    private static TextBox Box() => new() { Width = Brand.Px(300) };
    private static FlowLayoutPanel Row() => new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
    private static FlowLayoutPanel Column() => new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
    private static System.Windows.Forms.Label Hint(string text) => new() { Text = text, AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(620), 0), Margin = new Padding(0, 0, 0, 6) };
    private static string G(double v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Pct(double u) => Math.Round(u * 100).ToString(CultureInfo.InvariantCulture) + "%";

    private static void Mark(DataGridViewCell cell, bool pass)
    {
        cell.Style.ForeColor = pass ? Brand.Success : Brand.Failure;
        cell.Style.BackColor = pass ? Brand.SuccessBg : Brand.FailureBg;
        cell.Style.Font = new Font(cell.DataGridView!.Font, FontStyle.Bold);
    }

    private static DataGridView Grid(params (string Head, int Width, bool Right)[] cols)
    {
        var g = new DataGridView
        {
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ScrollBars = ScrollBars.Horizontal,
            BorderStyle = BorderStyle.None, Margin = new Padding(0, 0, 0, 8), Width = Brand.Px(640), Height = Brand.Px(60),
        };
        foreach (var (h, w, right) in cols) AddCol(g, h, w, right);
        return g;
    }

    private static void AddCol(DataGridView g, string head, int width, bool right)
    {
        var c = new DataGridViewTextBoxColumn { HeaderText = head, Width = Brand.Px(width), SortMode = DataGridViewColumnSortMode.NotSortable };
        c.DefaultCellStyle.Alignment = right ? DataGridViewContentAlignment.TopRight : DataGridViewContentAlignment.TopLeft;
        c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        g.Columns.Add(c);
    }

    private static void FitHeight(DataGridView g) =>
        g.Height = g.ColumnHeadersHeight + g.Rows.Cast<DataGridViewRow>().Sum(r => r.Height) + SystemInformation.HorizontalScrollBarHeight + 2;

    private static void FitWidths(FlowLayoutPanel p, int width)
    {
        foreach (Control c in p.Controls)
            if (c is DataGridView g) g.Width = Math.Max(Brand.Px(400), width);
    }
}
