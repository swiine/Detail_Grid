using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

internal sealed class CouncilRuleForm : Form
{
    private readonly TextBox _name = new() { Width = 360, PlaceholderText = "e.g. Bankstown Council standard spacing" };
    private readonly TextBox _source = new() { Width = 360, PlaceholderText = "e.g. Council spec clause 5.3 / standard drawing SD-102" };
    private readonly TextBox _aspect = Box("1.5");
    private readonly Dictionary<SlabKind, (TextBox Times, TextBox Cap)> _rules = new();
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Max spacing = the smaller of (times x slab thickness) and the cap. Leave the cap blank for none." };

    public JointRuleSet Result { get; private set; } = new();
    public bool Deleted { get; private set; }

    public CouncilRuleForm(JointRuleSet? existing, IReadOnlyCollection<string> otherNames)
    {
        bool editing = existing is not null;
        var set = existing ?? new JointRuleSet { Name = "", Source = "" };
        Text = editing ? "Edit joint rules" : "Add a council standard";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;

        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(16, 10, 16, 6) };
        t.Controls.Add(Brand.Step("Council / standard"));
        t.Controls.Add(Row(Lbl("Name", 120), _name));
        t.Controls.Add(Row(Lbl("Comes from", 120), _source));
        t.Controls.Add(Brand.Step("Maximum joint spacing"));
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 5, Margin = new Padding(3, 0, 3, 6) };
        grid.Controls.Add(Lbl("", 210), 0, 0);
        grid.Controls.Add(Lbl("x thickness", 0), 1, 0);
        grid.Controls.Add(Lbl("", 0), 2, 0);
        grid.Controls.Add(Lbl("cap", 0), 3, 0);
        int row = 1;
        foreach (var kind in Enum.GetValues<SlabKind>())
        {
            var rule = set.For(kind);
            var times = Box(Num(rule.TimesThickness));
            var cap = Box(rule.CapM > 0 ? Num(rule.CapM) : "");
            _rules[kind] = (times, cap);
            grid.Controls.Add(Lbl(JointLayout.Describe(kind), 210), 0, row);
            grid.Controls.Add(times, 1, row);
            grid.Controls.Add(Lbl("x t, max", 0), 2, row);
            grid.Controls.Add(cap, 3, row);
            grid.Controls.Add(Lbl("m", 0), 4, row);
            row++;
        }
        t.Controls.Add(grid);
        t.Controls.Add(Row(Lbl("Max panel ratio (long : short)", 210), _aspect, Lbl("e.g. 1.25 or 1.5", 0)));
        _aspect.Text = Num(set.AspectLimit);
        _name.Text = set.Name;
        _source.Text = set.Source;

        var save = new Button { Text = editing ? "Save" : "Add", AutoSize = true, Tag = Brand.PrimaryTag, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        if (editing)
        {
            var delete = new Button { Text = "Delete", AutoSize = true, MinimumSize = new Size(Brand.Px(100), Brand.Px(34)) };
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete \"{set.Name}\" for everyone using this release?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Deleted = true;
                DialogResult = DialogResult.Abort;
            };
            buttons.Controls.Add(delete);
        }
        t.Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        save.Click += (_, _) =>
        {
            var problem = Read(out var result);
            if (problem is null && otherNames.Contains(result.Name, StringComparer.OrdinalIgnoreCase))
                problem = $"There is already a rule set called \"{result.Name}\".";
            if (problem is not null)
            {
                _status.ForeColor = Brand.Failure;
                _status.Text = "✖ " + problem;
                return;
            }
            Result = result;
            DialogResult = DialogResult.OK;
        };

        Controls.Add(t);
        Brand.Decorate(this, Text, "Joint spacing  ·  saved for the whole team in TTW_JointRules.json", _status);
        t.BringToFront();
        ClientSize = Brand.FitClient(t, Brand.Px(620), hasStatus: true);
    }

    private string? Read(out JointRuleSet set)
    {
        set = new JointRuleSet { Name = _name.Text.Trim(), Source = _source.Text.Trim() };
        if (set.Name.Length == 0) return "Give it a name, e.g. \"Bankstown Council standard spacing\".";
        foreach (var (kind, (timesBox, capBox)) in _rules)
        {
            if (!TryNum(timesBox.Text, out var times) || times <= 0) return $"{JointLayout.Describe(kind)}: enter how many times the thickness (e.g. 24).";
            double cap = 0;
            if (capBox.Text.Trim().Length > 0 && (!TryNum(capBox.Text, out cap) || cap < 0.5)) return $"{JointLayout.Describe(kind)}: the cap is in metres (at least 0.5), or leave it blank.";
            var rule = new SpacingRule { TimesThickness = times, CapM = cap };
            switch (kind)
            {
                case SlabKind.Reinforced: set.Reinforced = rule; break;
                case SlabKind.SteelFibre: set.SteelFibre = rule; break;
                default: set.Unreinforced = rule; break;
            }
        }
        if (!TryNum(_aspect.Text, out var aspect) || aspect < 1) return "The panel ratio is 1 or more (e.g. 1.25).";
        set.AspectLimit = aspect;
        return null;
    }

    private static bool TryNum(string text, out double v) =>
        double.TryParse(text.Trim().TrimEnd('m', 'M').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static TextBox Box(string text) => new() { Text = text, Width = Brand.Px(70), TextAlign = HorizontalAlignment.Right };

    private static Label Lbl(string text, int width) => width > 0
        ? new Label { Text = text, AutoSize = false, Width = Brand.Px(width), Height = Brand.Px(24), TextAlign = ContentAlignment.MiddleLeft }
        : new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        f.Controls.AddRange(controls);
        return f;
    }
}
