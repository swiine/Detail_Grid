using System.Drawing;
using System.Windows.Forms;

namespace PavementBuildup;

/// <summary>
/// TTW brand for the plugin's windows (colours, fonts, buttons, header and status line), matching
/// TTW Advanced Tools: Water Blue header with the logo top right, flat square buttons, status line.
/// </summary>
internal static class Brand
{
    public static readonly Color HeaderBg = Hex("024360");     // Water Blue
    public static readonly Color HeaderFg = Hex("F7F4E7");     // Alabaster
    public static readonly Color HeaderSubtle = Hex("8ECAEF");
    public static readonly Color Accent = Hex("024360");
    public static readonly Color AccentHover = Hex("03587E");
    public static readonly Color AccentFg = Hex("F7F4E7");
    public static readonly Color Bg = Hex("F7F4E7");
    public static readonly Color Surface = Hex("FFFDF8");
    public static readonly Color Hover = Hex("EFEADB");
    public static readonly Color Selection = Hex("DDE8EC");
    public static readonly Color Border = Hex("DED7C4");
    public static readonly Color Text = Hex("1E1711");
    public static readonly Color Heading = Hex("024360");
    public static readonly Color Muted = Hex("6F6A5D");
    public static readonly Color Success = Hex("325442");
    public static readonly Color Failure = Hex("A84933");

    /// <summary>Marks a button as the main (Water Blue) one when it isn't the form's AcceptButton.</summary>
    public const string PrimaryTag = "brand-primary";

    /// <summary>Marks a control that draws its own brand look, so <see cref="Apply"/> leaves it alone.</summary>
    public const string SelfPaintTag = "brand-self";

    private static readonly string Family = Installed("Arial Nova") ? "Arial Nova" : "Arial";

    /// <summary>Body text (12.5 px).</summary>
    public static Font Body(FontStyle style = FontStyle.Regular) => new(Family, 9.5f, style);

    public static Font Sized(float points, FontStyle style = FontStyle.Regular) => new(Family, points, style);

    /// <summary>
    /// Brands a form: font and colours now (so child fonts built from it match), header and status line,
    /// and the controls' styles once everything has been added.
    /// </summary>
    public static void Decorate(Form form, string title, string subtitle, Control? status = null)
    {
        form.Font = Body();
        form.BackColor = Bg;
        form.ForeColor = Text;
        if (Image("brandmark-waterblue.png") is Bitmap mark)
        {
            try { form.Icon = Icon.FromHandle(mark.GetHicon()); }
            catch (System.Runtime.InteropServices.ExternalException) { /* keep the default icon */ }
        }
        // Docked controls added last are laid out first: header on top, status along the bottom.
        if (status is not null)
            form.Controls.Add(StatusBar(status));
        form.Controls.Add(Header(title, subtitle, out var titleLabel, out var subtitleLabel));
        form.Tag = new HeaderLabels(titleLabel, subtitleLabel);
        form.Load += (_, _) => Apply(form, form);
    }

    /// <summary>Screen scaling (1.0 at 100%, 1.5 at 150%): text sizes grow with it, so fixed pixel sizes must too.</summary>
    public static readonly float Scale = ScreenScale();

    /// <summary>A size in pixels at 100%, scaled for the screen.</summary>
    public static int Px(int pixels) => (int)Math.Round(pixels * Scale);

    public static int HeaderHeight => Px(70);
    public static int StatusHeight => Px(30);

    /// <summary>
    /// Window size for a form whose content is one top-docked panel under the header (docked panels
    /// don't make an AutoSize window wide enough, so the size is set from the panel's preferred size).
    /// </summary>
    public static Size FitClient(Control content, int minWidth, bool hasStatus)
    {
        var preferred = content.GetPreferredSize(new Size(minWidth, 0));
        int width = Math.Max(minWidth, preferred.Width);
        return new Size(width, HeaderHeight + preferred.Height + (hasStatus ? StatusHeight : 0));
    }

    /// <summary>Changes the header text after <see cref="Decorate"/>.</summary>
    public static void SetHeader(Form form, string title, string subtitle)
    {
        if (form.Tag is HeaderLabels h)
        {
            h.Title.Text = title;
            h.Subtitle.Text = subtitle;
        }
    }

    private sealed record HeaderLabels(Label Title, Label Subtitle);

    /// <summary>A step heading in the window ("① Paste or type the build-up").</summary>
    public static Label Step(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Heading, Font = Sized(11f, FontStyle.Bold),
        Margin = new Padding(3, 8, 3, 6),
    };

    private static Control Header(string title, string subtitle, out Label titleLabel, out Label subtitleLabel)
    {
        var band = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = HeaderHeight, BackColor = HeaderBg, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(22, 10, 22, 10),
        };
        band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        band.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        band.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        band.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        titleLabel = new Label
        {
            Text = title, AutoSize = true, ForeColor = HeaderFg, BackColor = HeaderBg,
            Font = Sized(18f), Margin = new Padding(0),
        };
        subtitleLabel = new Label
        {
            Text = subtitle, AutoSize = true, ForeColor = HeaderSubtle, BackColor = HeaderBg,
            Font = Sized(8.5f), Margin = new Padding(1, 1, 0, 0),
        };
        band.Controls.Add(titleLabel, 0, 0);
        band.Controls.Add(subtitleLabel, 0, 1);

        if (Image("logo-alabaster.png") is { } logo)
        {
            var box = new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = HeaderBg,
                Height = Px(30), Width = Px(30) * logo.Width / Math.Max(1, logo.Height),
                Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0),
            };
            band.Controls.Add(box, 1, 0);
            band.SetRowSpan(box, 2);
        }
        return band;
    }

    private static Control StatusBar(Control content)
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = StatusHeight, BackColor = Bg, Padding = new Padding(14, 7, 14, 6) };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        content.Dock = DockStyle.Fill;
        content.Font = Sized(8.75f);
        if (content is Label l) l.AutoEllipsis = true;
        bar.Controls.Add(content);
        return bar;
    }

    /// <summary>Flat brand styles for every control (buttons, boxes, tables, links).</summary>
    public static void Apply(Form form, Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button self when Equals(self.Tag, SelfPaintTag):
                    break; // paints itself (menu tiles)
                case Button b:
                    StyleButton(b, primary: ReferenceEquals(form.AcceptButton, b) || Equals(b.Tag, PrimaryTag));
                    break;
                case TextBoxBase t:
                    t.BackColor = t.ReadOnly && t is TextBox ? Bg : Surface;
                    t.ForeColor = Text;
                    t.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.BackColor = Surface;
                    break;
                case NumericUpDown n:
                    n.BackColor = Surface;
                    n.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case DataGridView g:
                    StyleGrid(g);
                    break;
                case LinkLabel link:
                    link.LinkColor = Accent;
                    link.VisitedLinkColor = Accent;
                    link.ActiveLinkColor = AccentHover;
                    break;
                case TabPage page:
                    page.UseVisualStyleBackColor = false;
                    page.BackColor = Bg;
                    break;
                case CheckBox check:
                    check.Cursor = Cursors.Hand;
                    break;
            }
            if (c.HasChildren && c is not DataGridView and not NumericUpDown)
                Apply(form, c);
        }
    }

    private static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.MinimumSize = new Size(b.MinimumSize.Width, Math.Max(b.MinimumSize.Height, primary ? 36 : 30));
        if (!b.AutoSize)
            b.Height = Math.Max(b.Height, b.MinimumSize.Height); // fixed width: no side padding, so the text stays on one line
        else if (b.Padding == Padding.Empty)
            b.Padding = new Padding(primary ? 14 : 8, 0, primary ? 14 : 8, 0);
        if (primary)
        {
            b.BackColor = Accent;
            b.ForeColor = AccentFg;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentHover;
            b.FlatAppearance.MouseDownBackColor = AccentHover;
            b.Font = new Font(b.Font, FontStyle.Bold);
        }
        else
        {
            b.BackColor = Surface;
            b.ForeColor = Accent;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Selection;
        }
    }

    private static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Surface;
        g.BorderStyle = BorderStyle.FixedSingle;
        g.GridColor = Border;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        g.ColumnHeadersDefaultCellStyle.BackColor = HeaderBg;
        g.ColumnHeadersDefaultCellStyle.ForeColor = HeaderFg;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBg;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = HeaderFg;
        g.ColumnHeadersDefaultCellStyle.Font = new Font(g.Font, FontStyle.Bold);
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        g.RowHeadersDefaultCellStyle.BackColor = Bg;
        g.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
        g.DefaultCellStyle.BackColor = Surface;
        g.DefaultCellStyle.ForeColor = Text;
        g.DefaultCellStyle.SelectionBackColor = Selection;
        g.DefaultCellStyle.SelectionForeColor = Text;
    }

    /// <summary>A logo compiled into the plugin (Assets\*.png), or null if it's missing.</summary>
    public static Bitmap? Image(string name)
    {
        try
        {
            using var stream = typeof(Brand).Assembly.GetManifestResourceStream("PavementBuildup.Assets." + name);
            return stream is null ? null : new Bitmap(stream);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static float ScreenScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or OutOfMemoryException)
        {
            return 1f;
        }
    }

    private static bool Installed(string family)
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));
    }

    private static Color Hex(string rgb) => Color.FromArgb(
        Convert.ToInt32(rgb[..2], 16), Convert.ToInt32(rgb[2..4], 16), Convert.ToInt32(rgb[4..], 16));
}
