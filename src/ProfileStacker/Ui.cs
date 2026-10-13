using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ProfileStacker;

/// <summary>TTW look (Water Blue header, alabaster background, flat buttons) and small form helpers.</summary>
internal static class Ui
{
    public static readonly Color HeaderBg = Hex("024360");
    public static readonly Color HeaderFg = Hex("F7F4E7");
    public static readonly Color HeaderSubtle = Hex("8ECAEF");
    public static readonly Color Bg = Hex("F7F4E7");
    public static readonly Color Surface = Hex("FFFDF8");
    public static readonly Color Border = Hex("DED7C4");
    public static readonly Color Text = Hex("1E1711");
    public static readonly Color Heading = Hex("024360");
    public static readonly Color Muted = Hex("6F6A5D");
    public static readonly Color Success = Hex("325442");
    public static readonly Color Failure = Hex("A84933");
    public static readonly Color Selection = Hex("DDE8EC");

    public static Font Body(FontStyle style = FontStyle.Regular) => new("Arial", 9f, style);

    public static Panel Header(string title, out Label subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = HeaderBg, Padding = new Padding(14, 8, 14, 8) };
        subtitle = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 18, ForeColor = HeaderSubtle, Font = Body(), AutoEllipsis = true };
        var heading = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 24, Text = title, ForeColor = HeaderFg, Font = new Font("Arial", 13f, FontStyle.Bold) };
        panel.Controls.Add(subtitle);
        panel.Controls.Add(heading);
        return panel;
    }

    public static GroupBox Group(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ForeColor = Heading,
        Font = Body(FontStyle.Bold),
        Padding = new Padding(8, 6, 8, 6),
        Margin = new Padding(0, 0, 0, 8),
    };

    /// <summary>Label/control rows inside a group.</summary>
    public static TableLayoutPanel Grid(int columns = 4)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            ForeColor = Text,
            Font = Body(),
        };
        for (int i = 0; i < columns; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3),
    };

    public static TextBox Number(double value, int width = 70) => new()
    {
        Text = Format(value),
        Width = width,
        TextAlign = HorizontalAlignment.Right,
        BackColor = Surface,
    };

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? HeaderBg : Surface,
            ForeColor = primary ? HeaderFg : Text,
            Padding = new Padding(10, 3, 10, 3),
            Font = Body(primary ? FontStyle.Bold : FontStyle.Regular),
        };
        b.FlatAppearance.BorderColor = primary ? HeaderBg : Border;
        return b;
    }

    public static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Reads a number typed with either the local or the invariant decimal separator.</summary>
    public static bool TryRead(TextBox box, out double value)
    {
        string text = box.Text.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Color Hex(string rgb) => Color.FromArgb(
        int.Parse(rgb[..2], NumberStyles.HexNumber),
        int.Parse(rgb[2..4], NumberStyles.HexNumber),
        int.Parse(rgb[4..], NumberStyles.HexNumber));
}
