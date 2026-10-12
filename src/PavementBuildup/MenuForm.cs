using System.Drawing;
using System.Windows.Forms;

namespace PavementBuildup;

/// <summary>What the TTWPVMT menu can start.</summary>
internal enum MenuChoice { None, NewDetail, EditDetail, FromNote, Quick, Standard, Ai }

/// <summary>
/// TTWPVMT: one window with every pavement tool as a big button, so there are no commands to remember.
/// Click one (or press its number) and the window closes and that tool starts.
/// </summary>
internal sealed class MenuForm : Form
{
    public MenuChoice Choice { get; private set; }

    private static readonly int TileWidth = Brand.Px(520);

    private static readonly (MenuChoice Choice, string Title, string Detail)[] Items =
    {
        (MenuChoice.NewDetail, "New pavement detail", "Paste or type a build-up in any wording, check the picture, place it."),
        (MenuChoice.EditDetail, "Edit a detail in the drawing", "Click a TTW_pavement-profile and change its layers, title or settings."),
        (MenuChoice.FromNote, "Detail from a note in the drawing", "Click a spec note (text or MText) and turn it into a detail."),
        (MenuChoice.Quick, "Quick place (command line)", "Type a one-line build-up and place it straight away. Also: PAVEQUICK."),
        (MenuChoice.Standard, "CAD standard", "Layers, hatches, text heights, break lines and bars. Copy from the drawing."),
        (MenuChoice.Ai, "AI settings (optional)", "Paid Claude API key or account. Not needed for Ask AI (free)."),
    };

    public MenuForm()
    {
        Text = "TTW Pavement";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var status = new Label { Text = "Pick what you want to do (or press 1-6). Esc closes." };
        Brand.Decorate(this, "TTW Pavement", "Civil 3D  ·  pavement build-up details", status);

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 16, 22, 14),
        };
        int n = 1;
        foreach (var (choice, title, detail) in Items)
        {
            var tile = new Tile($"{n}   {title}", detail) { Margin = new Padding(0, 0, 0, 8) };
            tile.Click += (_, _) => Pick(choice);
            list.Controls.Add(tile);
            n++;
        }
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 6, 0, 0) };
        CancelButton = close;
        list.Controls.Add(close);
        Controls.Add(list);
        list.BringToFront(); // under the header
        // Docked panels don't size the window by themselves: header + buttons + status line.
        ClientSize = Brand.FitClient(list, TileWidth + list.Padding.Horizontal, hasStatus: true);

        KeyDown += (_, e) =>
        {
            int i = e.KeyCode is >= Keys.D1 and <= Keys.D9 ? e.KeyCode - Keys.D1
                : e.KeyCode is >= Keys.NumPad1 and <= Keys.NumPad9 ? e.KeyCode - Keys.NumPad1 : -1;
            if (i >= 0 && i < Items.Length)
            {
                e.Handled = true;
                Pick(Items[i].Choice);
            }
        };
    }

    private void Pick(MenuChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>A big flat button: bold title, one muted line underneath.</summary>
    private sealed class Tile : Button
    {
        private readonly string _title;
        private readonly string _detail;
        private bool _hover;

        public Tile(string title, string detail)
        {
            _title = title;
            _detail = detail;
            Size = new Size(TileWidth, Brand.Px(58));
            Cursor = Cursors.Hand;
            Tag = Brand.SelfPaintTag;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += (_, _) => { _hover = true; Invalidate(); };
            MouseLeave += (_, _) => { _hover = false; Invalidate(); };
            GotFocus += (_, _) => Invalidate();
            LostFocus += (_, _) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool lit = _hover || Focused;
            g.Clear(lit ? Brand.Hover : Brand.Surface);
            // Water Blue bar down the left; the border goes blue on hover/focus.
            using (var bar = new SolidBrush(Brand.Accent))
                g.FillRectangle(bar, 0, 0, Brand.Px(lit ? 6 : 4), Height);
            using (var pen = new Pen(lit ? Brand.Accent : Brand.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            using var titleFont = Brand.Sized(11f, FontStyle.Bold);
            using var detailFont = Brand.Sized(8.75f);
            var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int x = Brand.Px(18), w = Width - Brand.Px(28);
            TextRenderer.DrawText(g, _title, titleFont, new Rectangle(x, Brand.Px(8), w, Brand.Px(24)), Brand.Heading, flags);
            TextRenderer.DrawText(g, _detail, detailFont, new Rectangle(x, Brand.Px(32), w, Brand.Px(20)), Brand.Muted, flags);
        }
    }
}
