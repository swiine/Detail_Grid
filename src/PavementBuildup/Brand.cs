using System.Drawing;
using System.Windows.Forms;

namespace PavementBuildup;

internal static class Brand
{
    internal sealed record Palette(
        Color HeaderBg, Color HeaderFg, Color HeaderSubtle, Color Accent, Color AccentHover, Color AccentFg,
        Color Bg, Color Surface, Color Hover, Color Selection, Color Border, Color Text, Color Heading,
        Color Muted, Color Success, Color Failure, Color Notice, Color ErrorBack)
    {
        public IEnumerable<Color> All => new[]
        {
            HeaderBg, HeaderFg, HeaderSubtle, Accent, AccentHover, AccentFg, Bg, Surface, Hover, Selection, Border,
            Text, Heading, Muted, Success, Failure, Notice, ErrorBack,
        };
    }

    internal static readonly Palette Light = new(
        Hex("024360"), Hex("F7F4E7"), Hex("8ECAEF"), Hex("024360"), Hex("03587E"), Hex("F7F4E7"),
        Hex("F7F4E7"), Hex("FFFDF8"), Hex("EFEADB"), Hex("DDE8EC"), Hex("DED7C4"), Hex("1E1711"), Hex("024360"),
        Hex("6F6A5D"), Hex("325442"), Hex("A84933"), Hex("FDF3D6"), Hex("FBECE8"));

    internal static readonly Palette DarkPalette = new(
        Hex("024360"), Hex("F7F4E7"), Hex("8ECAEF"), Hex("2A7DA6"), Hex("3590BD"), Hex("FFFFFF"),
        Hex("16191D"), Hex("1F242A"), Hex("29313A"), Hex("1E4256"), Hex("36414C"), Hex("E8E6DF"), Hex("8ECAEF"),
        Hex("9AA3AB"), Hex("6CC08B"), Hex("E8806A"), Hex("3A3322"), Hex("4A2A24"));

    internal static Palette Current => PavementBuildup.Core.UserSettings.Current.Dark ? DarkPalette : Light;

    public static Color HeaderBg => Current.HeaderBg;
    public static Color HeaderFg => Current.HeaderFg;
    public static Color HeaderSubtle => Current.HeaderSubtle;
    public static Color Accent => Current.Accent;
    public static Color AccentHover => Current.AccentHover;
    public static Color AccentFg => Current.AccentFg;
    public static Color Bg => Current.Bg;
    public static Color Surface => Current.Surface;
    public static Color Hover => Current.Hover;
    public static Color Selection => Current.Selection;
    public static Color Border => Current.Border;
    public static Color Text => Current.Text;
    public static Color Heading => Current.Heading;
    public static Color Muted => Current.Muted;
    public static Color Success => Current.Success;
    public static Color Failure => Current.Failure;
    public static Color Notice => Current.Notice;
    public static Color ErrorBack => Current.ErrorBack;

    public static event Action<PavementBuildup.Core.UserSettings>? SettingsSaved;

    public const string PrimaryTag = "brand-primary";

    public const string SelfPaintTag = "brand-self";

    private static readonly string Family = Installed("Arial Nova") ? "Arial Nova" : "Arial";

    private static float TextScale => PavementBuildup.Core.UserSettings.Current.TextSize / 100f;

    public static Font Body(FontStyle style = FontStyle.Regular) => new(Family, 9.5f * TextScale, style);

    public static Font Sized(float points, FontStyle style = FontStyle.Regular) => new(Family, points * TextScale, style);

    public static void Decorate(Form form, string title, string subtitle, Control? status = null)
    {
        form.Font = Body();
        form.BackColor = Bg;
        form.ForeColor = Text;
        if (Image("brandmark-waterblue.png") is Bitmap mark)
        {
            try { form.Icon = Icon.FromHandle(mark.GetHicon()); }
            catch (System.Runtime.InteropServices.ExternalException) {  }
        }
        if (status is not null)
            form.Controls.Add(StatusBar(status));
        form.Controls.Add(Header(form, title, subtitle, out var titleLabel, out var subtitleLabel));
        form.Tag = new HeaderLabels(titleLabel, subtitleLabel);
        form.Load += (_, _) => Apply(form, form);
        form.HandleCreated += (_, _) => DarkTitleBar(form);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void DarkTitleBar(Form form)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        int on = PavementBuildup.Core.UserSettings.Current.Dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    public static readonly float Scale = ScreenScale();

    public static int Px(int pixels) => (int)Math.Round(pixels * Scale);

    public static int HeaderHeight => Px(70);
    public static int StatusHeight => Px(30);

    public static Size FitClient(Control content, int minWidth, bool hasStatus)
    {
        var preferred = content.GetPreferredSize(new Size(minWidth, 0));
        int width = Math.Max(minWidth, preferred.Width);
        return new Size(width, HeaderHeight + preferred.Height + (hasStatus ? StatusHeight : 0));
    }

    public static void SetHeader(Form form, string title, string subtitle)
    {
        if (form.Tag is HeaderLabels h)
        {
            h.Title.Text = title;
            h.Subtitle.Text = subtitle;
        }
    }

    private sealed record HeaderLabels(Label Title, Label Subtitle);

    public static Button BackButton() => new() { Text = "Back to menu", AutoSize = true, DialogResult = DialogResult.Retry };

    public static Label Step(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Heading, Font = Sized(11f, FontStyle.Bold),
        Margin = new Padding(3, 8, 3, 6),
    };

    private static Control Header(Form form, string title, string subtitle, out Label titleLabel, out Label subtitleLabel)
    {
        var band = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = HeaderHeight, BackColor = HeaderBg, ColumnCount = 3, RowCount = 2,
            Padding = new Padding(22, 10, 12, 10),
        };
        band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        band.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        if (form is not SettingsForm)
        {
            var cog = new Button
            {
                Text = "⚙", Tag = SelfPaintTag, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = HeaderBg, ForeColor = HeaderFg, Font = new Font("Segoe UI Symbol", 15f * TextScale),
                Size = new Size(Px(40), Px(40)), Margin = new Padding(10, 0, 0, 0), Anchor = AnchorStyles.Right, Cursor = Cursors.Hand,
                TabStop = false,
            };
            cog.FlatAppearance.BorderSize = 0;
            cog.FlatAppearance.MouseOverBackColor = Current.AccentHover;
            cog.FlatAppearance.MouseDownBackColor = Current.AccentHover;
            new ToolTip().SetToolTip(cog, "My settings: theme, name, default scale, text size");
            cog.Click += (_, _) => OpenSettings(form);
            band.Controls.Add(cog, 2, 0);
            band.SetRowSpan(cog, 2);
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

    public static void OpenSettings(Form owner)
    {
        var before = Current;
        using var settings = new SettingsForm();
        if (settings.ShowDialog(owner) != DialogResult.OK) return;
        var after = Current;
        if (!ReferenceEquals(before, after))
            foreach (Form open in Application.OpenForms.Cast<Form>().Where(f => f.Tag is HeaderLabels).ToList())
                Retheme(open, before, after);
        SettingsSaved?.Invoke(PavementBuildup.Core.UserSettings.Current);
    }

    public static void Retheme(Form form, Palette from, Palette to)
    {
        var map = from.All.Zip(to.All).GroupBy(p => p.First.ToArgb()).ToDictionary(g => g.Key, g => g.First().Second);
        Color Swap(Color c) => map.TryGetValue(c.ToArgb(), out var n) ? n : c;
        void Walk(Control c)
        {
            c.BackColor = Swap(c.BackColor);
            c.ForeColor = Swap(c.ForeColor);
            if (c is Button b && b.FlatStyle == FlatStyle.Flat)
            {
                b.FlatAppearance.BorderColor = Swap(b.FlatAppearance.BorderColor);
                b.FlatAppearance.MouseOverBackColor = Swap(b.FlatAppearance.MouseOverBackColor);
                b.FlatAppearance.MouseDownBackColor = Swap(b.FlatAppearance.MouseDownBackColor);
            }
            foreach (Control child in c.Controls) Walk(child);
        }
        Walk(form);
        Apply(form, form);
        DarkTitleBar(form);
        form.Invalidate(true);
    }

    public static void Apply(Form form, Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button self when Equals(self.Tag, SelfPaintTag):
                    break;
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
                    cb.ForeColor = Text;
                    break;
                case NumericUpDown n:
                    n.BackColor = Surface;
                    n.ForeColor = Text;
                    n.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListBox list:
                    list.BackColor = Surface;
                    list.ForeColor = Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case TabControl tabs:
                    StyleTabs(tabs);
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
            b.Height = Math.Max(b.Height, b.MinimumSize.Height);
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

    private static void StyleTabs(TabControl tabs)
    {
        if (Equals(tabs.Tag, "brand-tabs")) { tabs.Invalidate(); return; }
        tabs.Tag = "brand-tabs";
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(Px(120), Px(30));
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            var r = tabs.GetTabRect(e.Index);
            using (var back = new SolidBrush(selected ? Surface : Bg)) e.Graphics.FillRectangle(back, r);
            if (selected)
                using (var bar = new SolidBrush(Accent)) e.Graphics.FillRectangle(bar, r.X, r.Bottom - Px(3), r.Width, Px(3));
            using var font = Sized(9.5f, selected ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, font, r, selected ? Heading : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        tabs.ParentChanged += (_, _) => tabs.Invalidate();
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
