using System.Drawing;
using System.Windows.Forms;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    // Shown instead of the main dialog when CANNOSCALE isn't one TTW ships a .lin file for.
    internal sealed class UnsupportedScaleForm : Form
    {
        public UnsupportedScaleForm(string cannoscale)
        {
            Text = "TTW Linemarking";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Ui.Regular();
            ClientSize = Ui.S(440, 200);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Ui.S(12)),
                ColumnCount = 2,
                RowCount = 2,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var logo = Ui.TryLoadLogo();
            if (logo != null)
            {
                layout.Controls.Add(new PictureBox
                {
                    Image = logo,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = Ui.S(56, 56),
                    Margin = new Padding(0, 0, Ui.S(12), 0),
                }, 0, 0);
            }

            string scaleText = string.IsNullOrEmpty(cannoscale) ? "(unknown)" : cannoscale;
            var message = new Label
            {
                Text = $"WARNING: Current scale ({scaleText}) is not a part of current package. "
                     + $"Please set scale to either {Scales.SupportedList()} to continue, "
                     + "or contact @corey young for any changes.",
                Font = Ui.Bold(9.5f),
                ForeColor = Ui.Red,
                Dock = DockStyle.Fill,
            };
            layout.Controls.Add(message, 1, 0);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = Ui.S(84, 28), Anchor = AnchorStyles.Right };
            layout.Controls.Add(ok, 1, 1);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }
}
