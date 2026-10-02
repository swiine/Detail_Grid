using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    // Draws a marking on a strip of asphalt: real dash/gap proportions, paint colour, relative line
    // width, and both lines for pairs.
    internal sealed class PatternPreview : Control
    {
        // Longest TTW pattern (30/90 and 90/30) is 120 units; fit about 1.5 repeats of it across.
        private const float UnitsAcross = 180f;

        private LinemarkingItem _item;

        public PatternPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Ui.Asphalt;
        }

        public LinemarkingItem Item
        {
            get => _item;
            set { _item = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_item == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float margin = Ui.S(10);
            float left = margin, right = Width - margin;
            float pxPerUnit = (right - left) / UnitsAcross;
            float mid = Height / 2f;

            var partner = _item.Pair != null ? Catalogue.Find(_item.Pair.PartnerCode) : null;
            if (partner != null || _item.Double)
            {
                var second = partner ?? _item;
                float spread = Thickness(_item) / 2 + Thickness(second) / 2 + Ui.S(6);
                DrawLine(g, _item, left, right, mid - spread / 2, pxPerUnit);
                DrawLine(g, second, left, right, mid + spread / 2, pxPerUnit);
            }
            else
            {
                DrawLine(g, _item, left, right, mid, pxPerUnit);
            }
        }

        // Exaggerated so 0.10 / 0.15 / 0.30 read as visibly different.
        private static float Thickness(LinemarkingItem item) => Ui.S(2) + (float)item.Width * Ui.S(20);

        private static void DrawLine(Graphics g, LinemarkingItem item, float left, float right, float y, float pxPerUnit)
        {
            float t = Thickness(item);
            using (var brush = new SolidBrush(Ui.PaintColor(item.Paint)))
            {
                if (item.Solid)
                {
                    g.FillRectangle(brush, left, y - t / 2, right - left, t);
                    return;
                }

                float x = left;
                int k = 0;
                while (x < right)
                {
                    float len = (float)Math.Abs(item.Dash[k % item.Dash.Length]) * pxPerUnit;
                    bool painted = item.Dash[k % item.Dash.Length] > 0;
                    if (painted) g.FillRectangle(brush, x, y - t / 2, Math.Min(len, right - x), t);
                    x += Math.Max(len, 1f);
                    k++;
                }
            }
        }
    }
}
