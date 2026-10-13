using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ProfileStacker.Core;

namespace ProfileStacker;

/// <summary>
/// The Profile View Stacker window: pick the sheet template and layout, check the stations and datum
/// settings, and see the views and sheets it will make (updated as you type) before creating them.
/// </summary>
internal sealed class StackerForm : Form
{
    private readonly SourceView _source;
    private readonly StackerSettings _settings;

    private readonly TextBox _template = new() { Width = 300, ReadOnly = true };
    private readonly ComboBox _layout = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _layerFilter = new() { Width = 120 };
    private readonly TextBox _scaleOverride, _paperClearance;
    private readonly Label _viewportInfo = new() { AutoSize = true, ForeColor = Ui.Muted, MaximumSize = new Size(470, 0) };

    private readonly TextBox _start = new() { Width = 90, TextAlign = HorizontalAlignment.Right };
    private readonly TextBox _end = new() { Width = 90, TextAlign = HorizontalAlignment.Right };
    private readonly TextBox _stationRounding, _overlap;
    private readonly ComboBox _lengthMode = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _roundStart = new() { Text = "Start on a round station", AutoSize = true };
    private readonly CheckBox _trimLast = new() { Text = "Trim the last view to the end station", AutoSize = true };

    private readonly TextBox _datumRounding, _clearBelow, _clearAbove;
    private readonly ComboBox _datumMode = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly TextBox _layoutNames = new() { Width = 260 };
    private readonly TextBox _viewNames = new() { Width = 260 };
    private readonly CheckBox _createLayouts = new() { Text = "Create sheet layouts from the template", AutoSize = true };
    private readonly CheckBox _lockViewports = new() { Text = "Lock the viewports", AutoSize = true };
    private readonly CheckBox _eraseSource = new() { Text = "Erase the long profile view", AutoSize = true };

    private readonly DataGridView _grid = new();
    private readonly SheetPreview _preview = new();
    private readonly Label _warnings = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 64, ForeColor = Ui.Failure, Padding = new Padding(2, 4, 2, 0) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Muted };
    private readonly Button _create = Ui.Button("Create", primary: true);
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };

    private SheetTemplate? _sheet;
    private IReadOnlyList<ViewportSlot> _slots = Array.Empty<ViewportSlot>();
    private bool _loading;

    /// <summary>The plan shown when the user clicked Create.</summary>
    public StackPlan? Plan { get; private set; }

    /// <summary>Settings as they were when the user clicked Create (stations included).</summary>
    public StackerSettings Settings => _settings;

    /// <summary>The open template, handed to the caller (who disposes it) when the dialog returns OK.</summary>
    public SheetTemplate? Template => _sheet;

    public StackerForm(SourceView source, StackerSettings settings)
    {
        _source = source;
        _settings = settings;
        _scaleOverride = Ui.Number(settings.ScaleOverride);
        _paperClearance = Ui.Number(settings.PaperClearance);
        _stationRounding = Ui.Number(settings.StationRounding);
        _overlap = Ui.Number(settings.Overlap);
        _datumRounding = Ui.Number(settings.DatumRounding);
        _clearBelow = Ui.Number(settings.ClearanceBelow);
        _clearAbove = Ui.Number(settings.ClearanceAbove);

        Text = "Profile View Stacker";
        Font = Ui.Body();
        BackColor = Ui.Bg;
        ForeColor = Ui.Text;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(1000, 640);

        BuildLayout();
        LoadValues();
        WireEvents();
        OpenTemplate(settings.TemplatePath, settings.TemplateLayout);
        Replan();
    }

    private void BuildLayout()
    {
        var header = Ui.Header("Profile View Stacker", out var subtitle);
        subtitle.Text = $"{_source.Name}  ·  {_source.AlignmentName}  ·  {Sta(_source.StationStart)} to {Sta(_source.StationEnd)}  ·  "
                        + $"vertical exaggeration {Ui.Format(_source.Frame.VerticalExaggeration)}×";

        // Left: settings. Right: preview of the sheets and the list of views.
        var left = new Panel { Dock = DockStyle.Left, Width = 500, AutoScroll = true, Padding = new Padding(12, 10, 8, 10) };
        var groups = new List<Control>
        {
            TemplateGroup(),
            StationGroup(),
            DatumGroup(),
            OutputGroup(),
        };
        groups.Reverse(); // docked to the top: the last added goes highest
        foreach (var g in groups)
            left.Controls.Add(g);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 10, 12, 10) };
        ConfigureGrid();
        _preview.Station = Sta;
        _preview.Dock = DockStyle.Top;
        _preview.Height = 220;
        var previewCaption = new Label { Dock = DockStyle.Top, Height = 20, Text = "Sheet (click a view below to see its sheet)", ForeColor = Ui.Heading, Font = Ui.Body(FontStyle.Bold) };
        var gridCaption = new Label { Dock = DockStyle.Top, Height = 24, Text = "Views", ForeColor = Ui.Heading, Font = Ui.Body(FontStyle.Bold), Padding = new Padding(0, 6, 0, 0) };
        right.Controls.Add(_grid);
        right.Controls.Add(gridCaption);
        right.Controls.Add(_preview);
        right.Controls.Add(previewCaption);
        right.Controls.Add(_warnings);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 44, ColumnCount = 3, Padding = new Padding(12, 6, 12, 6), BackColor = Ui.Surface };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var cancel = Ui.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(_create, 1, 0);
        footer.Controls.Add(cancel, 2, 0);
        AcceptButton = _create;
        CancelButton = cancel;

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(footer);
        Controls.Add(header);
    }

    private GroupBox TemplateGroup()
    {
        var group = Ui.Group("1  Sheet template");
        var grid = Ui.Grid(3);
        var browse = Ui.Button("Browse…");
        browse.Click += (_, _) => BrowseTemplate();
        grid.Controls.Add(Ui.Caption("Template"), 0, 0);
        grid.Controls.Add(_template, 1, 0);
        grid.Controls.Add(browse, 2, 0);
        grid.Controls.Add(Ui.Caption("Layout"), 0, 1);
        grid.Controls.Add(_layout, 1, 1);
        grid.Controls.Add(Ui.Caption("Viewport layer contains"), 0, 2);
        grid.Controls.Add(Row(_layerFilter, Ui.Caption("(blank = every viewport)")), 1, 2);
        grid.Controls.Add(Ui.Caption("Scale override"), 0, 3);
        grid.Controls.Add(Row(_scaleOverride, Ui.Caption("xp  (0 = the template's viewport scales)")), 1, 3);
        grid.Controls.Add(Ui.Caption("Clearance in viewport"), 0, 4);
        grid.Controls.Add(Row(_paperClearance, Ui.Caption("paper units each side")), 1, 4);
        grid.Controls.Add(_viewportInfo, 0, 5);
        grid.SetColumnSpan(_viewportInfo, 3);
        group.Controls.Add(grid);
        return group;
    }

    private GroupBox StationGroup()
    {
        var group = Ui.Group("2  Stations");
        var grid = Ui.Grid(4);
        _lengthMode.Items.AddRange(new object[] { "Fill each viewport", "Even lengths" });
        grid.Controls.Add(Ui.Caption("From"), 0, 0);
        grid.Controls.Add(_start, 1, 0);
        grid.Controls.Add(Ui.Caption("To"), 2, 0);
        grid.Controls.Add(_end, 3, 0);
        grid.Controls.Add(Ui.Caption("Round lengths to"), 0, 1);
        grid.Controls.Add(_stationRounding, 1, 1);
        grid.Controls.Add(Ui.Caption("Overlap"), 2, 1);
        grid.Controls.Add(_overlap, 3, 1);
        grid.Controls.Add(Ui.Caption("View lengths"), 0, 2);
        grid.Controls.Add(_lengthMode, 1, 2);
        grid.SetColumnSpan(_lengthMode, 3);
        grid.Controls.Add(_roundStart, 0, 3);
        grid.SetColumnSpan(_roundStart, 4);
        grid.Controls.Add(_trimLast, 0, 4);
        grid.SetColumnSpan(_trimLast, 4);
        group.Controls.Add(grid);
        return group;
    }

    private GroupBox DatumGroup()
    {
        var group = Ui.Group("3  Datum (elevations)");
        var grid = Ui.Grid(4);
        _datumMode.Items.AddRange(new object[] { "Under the low point", "Centred on the profile" });
        grid.Controls.Add(Ui.Caption("Datum"), 0, 0);
        grid.Controls.Add(_datumMode, 1, 0);
        grid.SetColumnSpan(_datumMode, 3);
        grid.Controls.Add(Ui.Caption("Round datum to"), 0, 1);
        grid.Controls.Add(_datumRounding, 1, 1);
        grid.Controls.Add(Ui.Caption("Clearance below"), 0, 2);
        grid.Controls.Add(_clearBelow, 1, 2);
        grid.Controls.Add(Ui.Caption("above"), 2, 2);
        grid.Controls.Add(_clearAbove, 3, 2);
        group.Controls.Add(grid);
        return group;
    }

    private GroupBox OutputGroup()
    {
        var group = Ui.Group("4  Output");
        var grid = Ui.Grid(2);
        grid.Controls.Add(Ui.Caption("Layout names"), 0, 0);
        grid.Controls.Add(_layoutNames, 1, 0);
        grid.Controls.Add(Ui.Caption("View names"), 0, 1);
        grid.Controls.Add(_viewNames, 1, 1);
        var tokens = new Label { AutoSize = true, ForeColor = Ui.Muted, Text = "<N> number   <START> <END> stations   <VIEW> long view name   <S> sheet" };
        grid.Controls.Add(tokens, 0, 2);
        grid.SetColumnSpan(tokens, 2);
        grid.Controls.Add(_createLayouts, 0, 3);
        grid.SetColumnSpan(_createLayouts, 2);
        grid.Controls.Add(_lockViewports, 0, 4);
        grid.SetColumnSpan(_lockViewports, 2);
        grid.Controls.Add(_eraseSource, 0, 5);
        grid.SetColumnSpan(_eraseSource, 2);
        group.Controls.Add(grid);
        return group;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = Ui.Surface;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.GridColor = Ui.Border;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Bg;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Heading;
        _grid.DefaultCellStyle.SelectionBackColor = Ui.Selection;
        _grid.DefaultCellStyle.SelectionForeColor = Ui.Text;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (name, weight) in new[] { ("View", 50), ("Sheet", 50), ("Viewport", 60), ("Start", 80), ("End", 80), ("Length", 70), ("Datum", 70), ("Top", 70), ("Note", 150) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.SelectionChanged += (_, _) =>
        {
            if (_grid.CurrentRow?.Tag is ViewPlan v)
                _preview.ShowSheet(v.Sheet);
        };
    }

    private void LoadValues()
    {
        _loading = true;
        var s = _settings;
        _template.Text = s.TemplatePath;
        _layerFilter.Text = s.ViewportLayerFilter;
        _start.Text = Sta(s.StartStation);
        _end.Text = Sta(s.EndStation);
        _lengthMode.SelectedIndex = s.LengthMode == LengthMode.Even ? 1 : 0;
        _roundStart.Checked = s.RoundStart;
        _trimLast.Checked = s.TrimLastView;
        _datumMode.SelectedIndex = s.DatumMode == DatumMode.Centred ? 1 : 0;
        _layoutNames.Text = s.LayoutNamePattern;
        _viewNames.Text = s.ViewNamePattern;
        _createLayouts.Checked = s.CreateLayouts;
        _lockViewports.Checked = s.LockViewports;
        _eraseSource.Checked = s.EraseSourceView;
        _loading = false;
    }

    private void WireEvents()
    {
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Replan();
        };
        foreach (var box in new[] { _layerFilter, _scaleOverride, _paperClearance, _start, _end, _stationRounding, _overlap, _datumRounding, _clearBelow, _clearAbove, _layoutNames, _viewNames })
            box.TextChanged += (_, _) => Changed();
        foreach (var combo in new[] { _lengthMode, _datumMode })
            combo.SelectedIndexChanged += (_, _) => Changed();
        foreach (var check in new[] { _roundStart, _trimLast, _createLayouts, _lockViewports, _eraseSource })
            check.CheckedChanged += (_, _) => Changed();
        _layout.SelectedIndexChanged += (_, _) => Changed();
        _create.Click += (_, _) => Create();
        FormClosed += (_, _) =>
        {
            _debounce.Dispose();
            if (DialogResult != DialogResult.OK)
                _sheet?.Dispose();
        };
    }

    private void Changed()
    {
        if (_loading)
            return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void BrowseTemplate()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Sheet template with the profile viewports",
            Filter = "Drawing templates and drawings (*.dwt;*.dwg)|*.dwt;*.dwg|All files (*.*)|*.*",
        };
        if (File.Exists(_template.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(_template.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            OpenTemplate(dialog.FileName, _layout.SelectedItem as string ?? "");
        Replan();
    }

    private void OpenTemplate(string path, string preferredLayout)
    {
        _loading = true;
        try
        {
            _layout.Items.Clear();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            var opened = SheetTemplate.Open(path);
            _sheet?.Dispose();
            _sheet = opened;
            _template.Text = path;
            foreach (var name in _sheet.LayoutNames())
                _layout.Items.Add(name);
            int index = _layout.Items.IndexOf(preferredLayout);
            if (index < 0)
                index = _layout.Items.Cast<string>().ToList().FindIndex(n => n.Contains("prof", StringComparison.OrdinalIgnoreCase));
            _layout.SelectedIndex = _layout.Items.Count == 0 ? -1 : Math.Max(0, index);
        }
        catch (System.Exception ex)
        {
            _status.ForeColor = Ui.Failure;
            _status.Text = $"Couldn't open the template: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Reads the form into the settings; false (with the reason in the status line) if a value is bad.</summary>
    private bool ReadSettings()
    {
        var s = _settings;
        string? bad = null;

        double Num(TextBox box, string name, double min = 0)
        {
            if (Ui.TryRead(box, out double v) && v >= min)
                return v;
            bad ??= $"{name} must be a number{(min == 0 ? " of 0 or more" : "")}.";
            return 0;
        }

        double StaOf(TextBox box, string name)
        {
            if (Naming.TryParseStation(box.Text, out double v))
                return v;
            bad ??= $"{name} station isn't a station (e.g. 1250 or 1+250).";
            return 0;
        }

        s.TemplatePath = _template.Text;
        s.TemplateLayout = _layout.SelectedItem as string ?? "";
        s.ViewportLayerFilter = _layerFilter.Text.Trim();
        s.ScaleOverride = Num(_scaleOverride, "Scale override");
        s.PaperClearance = Num(_paperClearance, "Clearance in viewport");
        s.StartStation = StaOf(_start, "Start");
        s.EndStation = StaOf(_end, "End");
        s.StationRounding = Num(_stationRounding, "Round lengths to");
        s.Overlap = Num(_overlap, "Overlap");
        s.LengthMode = _lengthMode.SelectedIndex == 1 ? LengthMode.Even : LengthMode.Fill;
        s.RoundStart = _roundStart.Checked;
        s.TrimLastView = _trimLast.Checked;
        s.DatumRounding = Num(_datumRounding, "Round datum to");
        s.ClearanceBelow = Num(_clearBelow, "Clearance below");
        s.ClearanceAbove = Num(_clearAbove, "Clearance above");
        s.DatumMode = _datumMode.SelectedIndex == 1 ? DatumMode.Centred : DatumMode.LowPoint;
        s.LayoutNamePattern = _layoutNames.Text;
        s.ViewNamePattern = _viewNames.Text;
        s.CreateLayouts = _createLayouts.Checked;
        s.LockViewports = _lockViewports.Checked;
        s.EraseSourceView = _eraseSource.Checked;

        if (bad is not null)
            Fail(bad);
        return bad is null;
    }

    private void Replan()
    {
        Plan = null;
        _grid.Rows.Clear();
        _warnings.Text = "";
        _preview.Set(Array.Empty<ViewportSlot>(), null);
        _create.Enabled = false;

        if (!ReadSettings())
            return;
        if (_sheet is null || _layout.SelectedItem is not string layout)
        {
            _viewportInfo.Text = "";
            Fail("Pick the sheet template (.dwt) that has the profile viewports.");
            return;
        }

        try
        {
            _slots = _sheet.Slots(layout, _settings.ViewportLayerFilter);
            _viewportInfo.Text = DescribeSlots();
            var plan = StackPlanner.Plan(_slots, _source.Frame, _settings, _source.Samples, (_source.ElevationMin, _source.ElevationMax));
            ShowPlan(plan);
        }
        catch (PlanException ex)
        {
            Fail(ex.Message);
        }
    }

    private string DescribeSlots()
    {
        if (_slots.Count == 0)
            return "No viewports found on this layout.";
        var lines = _slots.Select(s =>
        {
            var used = _settings.ScaleOverride > 0 ? s with { Scale = _settings.ScaleOverride } : s;
            var cap = StackPlanner.Capacity(used, _source.Frame, _settings.PaperClearance);
            return $"Viewport {s.Index + 1}: {Ui.Format(s.PaperWidth)} × {Ui.Format(s.PaperHeight)} at {Ui.Format(used.Scale)}xp, layer {s.Layer}"
                   + $" → {Ui.Format(Math.Round(cap.StationLength, 1))} of station × {Ui.Format(Math.Round(cap.ElevationRange, 2))} of elevation";
        });
        return string.Join(Environment.NewLine, lines);
    }

    private void ShowPlan(StackPlan plan)
    {
        Plan = plan;
        foreach (var v in plan.Views)
        {
            string note = !v.FitsHeight ? "taller than viewport" : !v.HasProfileData ? "no profile here" : "";
            int row = _grid.Rows.Add(
                v.Number, v.Sheet + 1, v.Slot.Index + 1, Sta(v.StationStart), Sta(v.StationEnd),
                Ui.Format(Math.Round(v.Length, 3)), Ui.Format(Math.Round(v.ElevationMin, 3)), Ui.Format(Math.Round(v.ElevationMax, 3)), note);
            _grid.Rows[row].Tag = v;
            if (note.Length > 0)
                _grid.Rows[row].DefaultCellStyle.ForeColor = Ui.Failure;
        }
        _preview.Set(_slots.Select(s => _settings.ScaleOverride > 0 ? s with { Scale = _settings.ScaleOverride } : s).ToList(), plan);
        _preview.ShowSheet(0);
        _warnings.Text = string.Join(Environment.NewLine, plan.Warnings);

        string layouts = _settings.CreateLayouts ? $" on {plan.SheetCount} sheet{(plan.SheetCount == 1 ? "" : "s")}" : "";
        _create.Text = $"Create {plan.Views.Count} view{(plan.Views.Count == 1 ? "" : "s")}{layouts}";
        _create.Enabled = true;
        _status.ForeColor = plan.Warnings.Count == 0 ? Ui.Success : Ui.Muted;
        _status.Text = plan.Warnings.Count == 0
            ? $"✔ {plan.Views.Count} views, {plan.SlotsPerSheet} per sheet, each filling its viewport."
            : $"{plan.Views.Count} views, {plan.SlotsPerSheet} per sheet. Check the notes above the list.";
    }

    private void Fail(string message)
    {
        _status.ForeColor = Ui.Failure;
        _status.Text = message;
        _create.Text = "Create";
        _create.Enabled = false;
    }

    private void Create()
    {
        _debounce.Stop();
        Replan();
        if (Plan is null)
            return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private string Sta(double station) => Naming.Station(station, _settings.StationPlusDigits, _settings.StationDecimals);

    /// <summary>Sketch of one sheet: the template's viewports, each with the station range it will show.</summary>
    private sealed class SheetPreview : Panel
    {
        private IReadOnlyList<ViewportSlot> _slots = Array.Empty<ViewportSlot>();
        private StackPlan? _plan;
        private int _sheet;

        public Func<double, string> Station { get; set; } = s => Naming.Station(s);

        public SheetPreview()
        {
            DoubleBuffered = true;
            BackColor = Ui.Surface;
            BorderStyle = BorderStyle.FixedSingle;
        }

        public void Set(IReadOnlyList<ViewportSlot> slots, StackPlan? plan)
        {
            _slots = slots;
            _plan = plan;
            Invalidate();
        }

        public void ShowSheet(int sheet)
        {
            _sheet = sheet;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_slots.Count == 0 || _plan is null)
                return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            double minX = _slots.Min(s => s.Left), maxX = _slots.Max(s => s.Left + s.PaperWidth);
            double maxY = _slots.Max(s => s.Top), minY = _slots.Min(s => s.Top - s.PaperHeight);
            double pad = 12;
            double k = Math.Min((Width - 2 * pad) / (maxX - minX), (Height - 2 * pad - 18) / (maxY - minY));
            double ox = (Width - (maxX - minX) * k) / 2;
            double oy = pad + 18;

            using var title = Ui.Body(FontStyle.Bold);
            using var body = Ui.Body();
            using var border = new Pen(Ui.Muted);
            using var graph = new SolidBrush(Color.FromArgb(60, Ui.HeaderBg));
            using var text = new SolidBrush(Ui.Text);
            using var heading = new SolidBrush(Ui.Heading);
            using var muted = new SolidBrush(Ui.Muted);
            using var failure = new SolidBrush(Ui.Failure);
            g.DrawString($"Sheet {_sheet + 1} of {_plan.SheetCount}", title, heading, (float)pad, 4f);

            var views = _plan.Views.Where(v => v.Sheet == _sheet).ToList();
            foreach (var slot in _slots)
            {
                var rect = new RectangleF(
                    (float)(ox + (slot.Left - minX) * k), (float)(oy + (maxY - slot.Top) * k),
                    (float)(slot.PaperWidth * k), (float)(slot.PaperHeight * k));
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                var view = views.FirstOrDefault(v => v.Slot.Index == slot.Index);
                if (view is null)
                {
                    g.DrawString($"Viewport {slot.Index + 1}: not used", body, muted, rect.X + 4, rect.Y + 4);
                    continue;
                }
                // Shade the share of the viewport the graph takes (shorter on a trimmed last view).
                float share = (float)Math.Clamp(view.Length / Math.Max(1e-9, view.Capacity.StationLength), 0, 1);
                g.FillRectangle(graph, rect.X, rect.Y, rect.Width * share, rect.Height);
                g.DrawString(
                    $"View {view.Number}:  {Station(view.StationStart)} to {Station(view.StationEnd)}   datum {Ui.Format(Math.Round(view.ElevationMin, 2))}",
                    body, view.FitsHeight ? text : failure, rect.X + 4, rect.Y + 4);
            }
        }
    }
}
