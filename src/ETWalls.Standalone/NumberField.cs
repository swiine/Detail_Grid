using ETWalls.Core.Design;

namespace ETWalls.Standalone;

/// <summary>
/// One input row: label, box, unit and a hint underneath. Reads "2.4", "2400mm", "30°" and so on; the box turns
/// red and the hint says why when the value can't be used.
/// </summary>
internal sealed class NumberField : TableLayoutPanel
{
    private readonly TextBox _box = new() { Width = Brand.Px(160), Margin = new Padding(0, 2, 6, 0) };
    private readonly System.Windows.Forms.Label _unit = new() { AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(0, 6, 0, 0) };
    private readonly System.Windows.Forms.Label _hint = new() { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(420), 0), Margin = new Padding(0, 1, 0, 2) };
    private readonly ValueKind _kind;
    private readonly string _help;
    private readonly Func<double, string?>? _limit;
    private bool _loading;

    public NumberField(string label, string unit, string help, ValueKind kind, Func<double, string?>? limit = null)
    {
        _kind = kind; _help = help; _limit = limit;
        ColumnCount = 3; RowCount = 2; AutoSize = true; Margin = new Padding(0, 0, 0, 4);
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Brand.Px(190)));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(new System.Windows.Forms.Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, 0, 0);
        Controls.Add(_box, 1, 0);
        _unit.Text = unit;
        Controls.Add(_unit, 2, 0);
        Controls.Add(_hint, 1, 1);
        SetColumnSpan(_hint, 2);
        Caption = label;
        _hint.Text = help;
        _hint.Visible = help.Length > 0;
        _box.TextChanged += (_, _) => { if (!_loading) { Refresh(); Changed?.Invoke(); } };
    }

    public string Caption { get; }
    public event Action? Changed;

    /// <summary>The value, or null if it can't be read or is out of range.</summary>
    public double? Value { get; private set; }
    /// <summary>Why the value can't be used, or null.</summary>
    public string? Problem { get; private set; }

    public void Set(double v)
    {
        _loading = true;
        _box.Text = v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        _loading = false;
        Refresh();
    }

    public new void Refresh()
    {
        var r = Units.Read(_box.Text, _kind);
        string? problem = r.Ok ? _limit?.Invoke(r.Value) : r.Message;
        Value = problem is null && r.Ok ? r.Value : null;
        Problem = problem;
        _box.BackColor = problem is null ? Brand.Surface : Brand.FailureBg;
        _hint.ForeColor = problem is null ? Brand.Muted : Brand.Failure;
        _hint.Text = problem ?? r.Message ?? _help;
        _hint.Visible = _hint.Text.Length > 0;
    }
}
