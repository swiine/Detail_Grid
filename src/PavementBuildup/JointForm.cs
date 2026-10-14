using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

internal sealed class JointForm : Form
{
    private sealed class Remembered
    {
        public string Rules { get; set; } = "";
        public string Kind { get; set; } = nameof(SlabKind.Unreinforced);
        public string Thickness { get; set; } = "200";
        public string MaxSpacing { get; set; } = "";
        public string PlanScale { get; set; } = "200";
        public bool AlongDrawingX { get; set; }
        public int SetOut { get; set; } = 2;
        public bool Exact { get; set; }
        public bool Note { get; set; } = true;
    }

    private sealed class AddCouncil
    {
        public override string ToString() => "+ Add a council standard…";
    }

    private static string RememberPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", "joints.json");

    private readonly IReadOnlyList<Pt> _boundary;
    private readonly IReadOnlyList<IReadOnlyList<Pt>> _holes;
    private readonly double _unitsPerMetre;
    private JointRulesFile _rules = JointRulesFile.LoadCompany();
    private readonly ComboBox _council = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _kind = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _thickness = Box("200");
    private readonly TextBox _maxSpacing = Box("");
    private readonly TextBox _planScale = Box("200");
    private readonly ComboBox _direction = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _exact = new() { Text = "Exact circle steps from the corner (leftover panel at the far end)", AutoSize = true };
    private readonly Label _cornerHint = new() { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(360), 0) };
    private Pt? _corner;
    private readonly CheckBox _note = new() { Text = "Place a joint note (pick a point after)", AutoSize = true, Checked = true };
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(Brand.Px(360), 0) };
    private readonly Label _status = new() { ForeColor = Brand.Muted };
    private readonly Preview _preview = new() { Dock = DockStyle.Fill };
    private readonly Button _draw = new() { Text = "Draw joints", AutoSize = true, Tag = Brand.PrimaryTag };
    private readonly Button _back = Brand.BackButton();
    private bool _filling;
    private string? _lastCouncil;

    public SlabJointResult? Result { get; private set; }
    public JointRuleSet Rules => _council.SelectedItem as JointRuleSet ?? new JointRuleSet();
    public SlabKind Kind => Enum.GetValues<SlabKind>()[Math.Max(0, _kind.SelectedIndex)];
    public double ThicknessMm => Num(_thickness.Text) ?? 0;
    public double PlanScale => Num(_planScale.Text) is { } s && s >= 1 ? s : 200;
    public bool PlaceNote => _note.Checked;

    public JointForm(IReadOnlyList<Pt> boundary, IReadOnlyList<IReadOnlyList<Pt>> holes, double unitsPerMetre)
    {
        _boundary = boundary;
        _holes = holes;
        _unitsPerMetre = unitsPerMetre;
        Text = "Joint layout";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Size = new Size(Brand.Px(1060), Brand.Px(640));
        MinimumSize = new Size(Brand.Px(860), Brand.Px(520));

        foreach (var kind in Enum.GetValues<SlabKind>()) _kind.Items.Add(JointLayout.Describe(kind));
        _direction.Items.AddRange(new object[] { "Grid square to the longest edge", "Grid along the drawing X / Y axes", "From a corner, parallel to its edges (circles)" });
        var remembered = LoadRemembered();
        _thickness.Text = remembered.Thickness;
        _maxSpacing.Text = remembered.MaxSpacing;
        _planScale.Text = remembered.PlanScale;
        _note.Checked = remembered.Note;
        _kind.SelectedIndex = Math.Max(0, Array.IndexOf(Enum.GetNames<SlabKind>(), remembered.Kind));
        _direction.SelectedIndex = Math.Clamp(remembered.SetOut, 0, 2);
        _exact.Checked = remembered.Exact;
        _exact.CheckedChanged += (_, _) => Recalculate();
        _preview.CornerPicked += p => { _corner = p; if (_direction.SelectedIndex != 2) _direction.SelectedIndex = 2; else Recalculate(); };
        FillCouncils(remembered.Rules);

        var edit = new LinkLabel { Text = "Edit", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        edit.LinkClicked += (_, _) => { if (_council.SelectedItem is JointRuleSet set) EditCouncil(set); };
        _council.SelectedIndexChanged += (_, _) =>
        {
            if (_filling) return;
            if (_council.SelectedItem is AddCouncil) { EditCouncil(null); return; }
            _lastCouncil = (_council.SelectedItem as JointRuleSet)?.Name;
            Recalculate();
        };
        foreach (var c in new Control[] { _kind, _direction }) ((ComboBox)c).SelectedIndexChanged += (_, _) => Recalculate();
        foreach (var t in new[] { _thickness, _maxSpacing }) t.TextChanged += (_, _) => Recalculate();

        var left = new TableLayoutPanel { Dock = DockStyle.Left, AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 10, 10, 10) };
        left.Controls.Add(Brand.Step("Council / standard"));
        left.Controls.Add(Row(_council, edit));
        left.Controls.Add(Brand.Step("Slab"));
        left.Controls.Add(Row(Lbl("Type"), _kind));
        left.Controls.Add(Row(Lbl("Thickness"), _thickness, Lbl("mm")));
        left.Controls.Add(Row(Lbl("Max spacing"), _maxSpacing, Lbl("m  (blank = council rule)")));
        left.Controls.Add(Row(Lbl("Set out"), _direction));
        left.Controls.Add(_exact);
        left.Controls.Add(_cornerHint);
        left.Controls.Add(Brand.Step("Drawing"));
        left.Controls.Add(Row(Lbl("Plan scale 1:"), _planScale));
        left.Controls.Add(_note);
        left.Controls.Add(Brand.Step("Result"));
        left.Controls.Add(_summary);
        _draw.MinimumSize = new Size(Brand.Px(130), Brand.Px(38));
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        left.Controls.Add(Row(_draw, _back, cancel));
        _draw.Click += (_, _) => Finish();
        CancelButton = cancel;
        AcceptButton = _draw;

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 10, 16, 10) };
        right.Controls.Add(_preview);

        Controls.Add(right);
        Controls.Add(left);
        Brand.Decorate(this, "Joint layout", "Sawcut joints for the selected slab  ·  joints off inside corners and pits, so fewer trimmer bars", _status);
        right.BringToFront();
        Recalculate();
    }

    public void HideBack() => _back.Visible = false;

    private void Recalculate()
    {
        if (_filling || _council.SelectedItem is not JointRuleSet set) return;
        try
        {
            var spacing = _maxSpacing.Text.Trim().Length == 0 ? 0 : Num(_maxSpacing.Text) ?? throw new ArgumentException("Max spacing: enter metres, e.g. 4.5, or leave it blank.");
            var thickness = Num(_thickness.Text) ?? throw new ArgumentException("Thickness: enter millimetres, e.g. 200.");
            Result = SlabJoints.Layout(new SlabJointInputs
            {
                Boundary = _boundary, Holes = _holes, UnitsPerMetre = _unitsPerMetre, ThicknessMm = thickness, Kind = Kind,
                Rules = set, MaxSpacingM = spacing,
                SetOut = _direction.SelectedIndex switch { 1 => JointSetOut.DrawingAxes, 2 => JointSetOut.FromCorner, _ => JointSetOut.LongestEdge },
                Corner = _corner, ExactSteps = _exact.Checked,
            });
            _exact.Enabled = _direction.SelectedIndex == 2;
            _cornerHint.Text = _direction.SelectedIndex == 2
                ? (_corner is null ? "Corner: picked for the fewest trimmer bars (green). Click a corner in the picture to set out from it." : "Corner: the one you clicked (green). Click another corner to change it.")
                : "Tip: click a corner in the picture to set out from it with circles.";
            var r = Result;
            _summary.ForeColor = Brand.Text;
            _summary.Text = $"Max spacing {r.SpacingM:0.00} m  ·  {r.Panels} panels (largest side {r.LargestPanelM:0.00} m, worst {r.WorstAspect:0.00}:1)\n"
                            + $"Sawcuts {r.SawcutLengthM:0.0} m, {r.SawcutDepthMm} mm deep  ·  isolation joint {r.PerimeterM:0.0} m  ·  slab {r.AreaM2:0} m²\n"
                            + (r.CornersJointed > 0 ? $"{r.CornersJointed} inside / pit corner(s) on a joint.\n" : "");
            _status.ForeColor = r.Warnings.Count == 0 ? Brand.Success : Brand.Failure;
            _status.Text = r.Warnings.Count == 0 ? "No corners need trimmer bars." : string.Join("  ", r.Warnings);
            _draw.Enabled = true;
        }
        catch (ArgumentException ex)
        {
            Result = null;
            _summary.ForeColor = Brand.Failure;
            _summary.Text = "✖ " + ex.Message;
            _status.Text = "";
            _draw.Enabled = false;
        }
        _preview.Show(_boundary, _holes, Result);
    }

    private void Finish()
    {
        if (Result is null) return;
        SaveRemembered(new Remembered
        {
            Rules = Rules.Name, Kind = Kind.ToString(), Thickness = _thickness.Text.Trim(), MaxSpacing = _maxSpacing.Text.Trim(),
            PlanScale = _planScale.Text.Trim(), AlongDrawingX = _direction.SelectedIndex == 1, SetOut = _direction.SelectedIndex, Exact = _exact.Checked, Note = _note.Checked,
        });
        DialogResult = DialogResult.OK;
    }

    private void FillCouncils(string? keep)
    {
        _filling = true;
        _council.Items.Clear();
        foreach (var set in _rules.Sets) _council.Items.Add(set);
        if (_rules.Sets.Count == 0) _council.Items.Add(new JointRuleSet());
        _council.Items.Add(new AddCouncil());
        _council.SelectedItem = _council.Items.OfType<JointRuleSet>().FirstOrDefault(r => string.Equals(r.Name, keep, StringComparison.OrdinalIgnoreCase))
                                ?? _council.Items[0];
        _lastCouncil = (_council.SelectedItem as JointRuleSet)?.Name;
        _filling = false;
    }

    private void EditCouncil(JointRuleSet? existing)
    {
        var others = _rules.Sets.Where(s => !ReferenceEquals(s, existing)).Select(s => s.Name).ToList();
        using var form = new CouncilRuleForm(existing, others);
        var answer = form.ShowDialog(this);
        string? select = _lastCouncil;
        if (answer == DialogResult.OK)
        {
            int at = existing is null ? -1 : _rules.Sets.IndexOf(existing);
            if (at >= 0) _rules.Sets[at] = form.Result; else _rules.Sets.Add(form.Result);
            select = form.Result.Name;
        }
        else if (answer == DialogResult.Abort && form.Deleted && existing is not null)
        {
            _rules.Sets.Remove(existing);
            select = null;
        }
        if (answer is DialogResult.OK or DialogResult.Abort)
        {
            var path = RulesPath();
            try
            {
                _rules.Save(path);
                _status.ForeColor = Brand.Muted;
                _status.Text = "Saved to " + path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Could not save the joint rules to {path}: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        FillCouncils(select);
        Recalculate();
    }

    private string RulesPath()
    {
        if (_rules.SourcePath is { } existing) return existing;
        if (StandardStore.FindSupportFile(JointRulesFile.FileName) is { } found) return found;
        if (StandardStore.CompanyDefault is { } company && Path.GetDirectoryName(company) is { } dir) return Path.Combine(dir, JointRulesFile.FileName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PavementBuildup", JointRulesFile.FileName);
    }

    private static Remembered LoadRemembered()
    {
        try { return File.Exists(RememberPath) ? JsonSerializer.Deserialize<Remembered>(File.ReadAllText(RememberPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private static void SaveRemembered(Remembered r)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RememberPath)!);
            File.WriteAllText(RememberPath, JsonSerializer.Serialize(r));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static double? Num(string text) =>
        double.TryParse(text.Trim().TrimEnd('m', 'M').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    private static TextBox Box(string text) => new() { Text = text, Width = Brand.Px(70), TextAlign = HorizontalAlignment.Right };

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        f.Controls.AddRange(controls);
        return f;
    }

    private sealed class Preview : Panel
    {
        private IReadOnlyList<Pt> _boundary = Array.Empty<Pt>();
        private IReadOnlyList<IReadOnlyList<Pt>> _holes = Array.Empty<IReadOnlyList<Pt>>();
        private SlabJointResult? _result;
        private Func<Pt, PointF>? _map;

        public event Action<Pt>? CornerPicked;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_map is null || _boundary.Count < 3) return;
            var nearest = _boundary.OrderBy(p => Dist(_map(p), e.Location)).First();
            if (Dist(_map(nearest), e.Location) <= Brand.Px(18)) CornerPicked?.Invoke(nearest);
        }

        private static double Dist(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        public Preview()
        {
            DoubleBuffered = true;
            BackColor = Brand.Surface;
            ResizeRedraw = true;
            BorderStyle = BorderStyle.FixedSingle;
            Cursor = Cursors.Hand;
        }

        public void Show(IReadOnlyList<Pt> boundary, IReadOnlyList<IReadOnlyList<Pt>> holes, SlabJointResult? result)
        {
            _boundary = boundary;
            _holes = holes;
            _result = result;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_boundary.Count < 3) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double minX = _boundary.Min(p => p.X), maxX = _boundary.Max(p => p.X), minY = _boundary.Min(p => p.Y), maxY = _boundary.Max(p => p.Y);
            float pad = Brand.Px(24);
            double k = Math.Min((Width - 2 * pad) / Math.Max(1e-9, maxX - minX), (Height - 2 * pad) / Math.Max(1e-9, maxY - minY));
            double ox = (Width - (maxX - minX) * k) / 2, oy = (Height - (maxY - minY) * k) / 2;
            PointF P(Pt p) => new((float)(ox + (p.X - minX) * k), (float)(Height - oy - (p.Y - minY) * k));
            _map = P;

            using (var fill = new SolidBrush(Color.FromArgb(240, 236, 224)))
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddPolygon(_boundary.Select(P).ToArray());
                foreach (var h in _holes.Where(h => h.Count >= 3)) path.AddPolygon(h.Select(P).ToArray());
                g.FillPath(fill, path);
                using var edge = new Pen(Brand.Text, 2f);
                g.DrawPath(edge, path);
            }
            using (var dot = new SolidBrush(Brand.Muted))
            {
                float d = Brand.Px(3);
                foreach (var p in _boundary.Select(P)) g.FillEllipse(dot, p.X - d, p.Y - d, 2 * d, 2 * d);
            }
            if (_result is null) return;
            if (_result.SetOutCorner is { } sc)
            {
                var p = P(sc);
                float d = Brand.Px(8);
                using var green = new SolidBrush(Color.FromArgb(50, 140, 70));
                g.FillEllipse(green, p.X - d, p.Y - d, 2 * d, 2 * d);
            }
            using (var joint = new Pen(Brand.Failure, 1.4f) { DashStyle = DashStyle.Dash })
                foreach (var j in _result.Joints) g.DrawLine(joint, P(j.A), P(j.B));
            using var ring = new Pen(Color.FromArgb(0, 102, 204), 2f);
            float r = Brand.Px(9);
            foreach (var c in _result.CheckPoints)
            {
                var p = P(c);
                g.DrawEllipse(ring, p.X - r, p.Y - r, 2 * r, 2 * r);
            }
        }
    }
}
