using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    // Walks the GuidedQuestions tree one question at a time. Knows nothing about the content, so
    // the wording can change without touching this file.
    internal sealed class GuidedQuestionsForm : Form
    {
        private readonly Stack<(Question Question, string Choice)> _history = new Stack<(Question, string)>();
        private readonly Label _trail;
        private readonly Label _question;
        private readonly Label _help;
        private readonly FlowLayoutPanel _answers;
        private readonly Button _back;
        private Question _current;

        public string SelectedCode { get; private set; }

        public GuidedQuestionsForm()
        {
            Text = "TTW Linemarking - Guided Questions";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Ui.Regular(10f);
            ClientSize = Ui.S(620, 560);
            MinimumSize = Ui.S(460, 380);

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), Ui.S(6)),
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _trail = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Regular(8.5f) };
            _question = new Label { AutoSize = true, Font = Ui.Bold(13f), Margin = new Padding(3, Ui.S(4), 3, 0) };
            _help = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(3, Ui.S(4), 3, 0) };
            header.Controls.Add(_trail);
            header.Controls.Add(_question);
            header.Controls.Add(_help);

            _answers = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(Ui.S(16), Ui.S(6), Ui.S(16), Ui.S(10)),
            };

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(Ui.S(14), Ui.S(4), Ui.S(14), Ui.S(10)),
            };
            _back = new Button { Text = "< Back", Size = Ui.S(90, 30) };
            _back.Click += (s, e) => GoBack();
            var notSure = new Button { Text = "Not sure - show me the full list", Size = Ui.S(240, 30), DialogResult = DialogResult.Cancel };
            footer.Controls.Add(_back);
            footer.Controls.Add(notSure);
            CancelButton = notSure;

            Controls.Add(_answers);
            Controls.Add(footer);
            Controls.Add(header);

            Resize += (s, e) => FitWidths();
            ShowQuestion(GuidedQuestions.Root);
        }

        private void ShowQuestion(Question q)
        {
            _current = q;
            _trail.Text = _history.Count == 0
                ? "Question 1"
                : $"Question {_history.Count + 1}   ·   " + string.Join("  >  ", _history.Reverse().Select(h => h.Choice));
            _question.Text = q.Text;
            _help.Text = q.Help ?? "";
            _help.Visible = q.Help != null;
            _back.Enabled = _history.Count > 0;

            _answers.SuspendLayout();
            while (_answers.Controls.Count > 0) _answers.Controls[0].Dispose();
            foreach (var answer in q.Answers)
            {
                var btn = new AnswerButton(answer.Text, answer.Hint, answer.Code);
                btn.Click += (s, e) => Choose(answer);
                _answers.Controls.Add(btn);
            }
            FitWidths();
            _answers.ResumeLayout();
            _answers.AutoScrollPosition = Point.Empty;
        }

        private void Choose(Answer answer)
        {
            if (answer.Next != null)
            {
                _history.Push((_current, answer.Text));
                ShowQuestion(answer.Next);
                return;
            }
            SelectedCode = answer.Code;
            DialogResult = DialogResult.OK;
        }

        private void GoBack()
        {
            if (_history.Count > 0) ShowQuestion(_history.Pop().Question);
        }

        private void FitWidths()
        {
            int w = Math.Max(Ui.S(200), _answers.ClientSize.Width - _answers.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            foreach (Control c in _answers.Controls)
            {
                c.Width = w;
                ((AnswerButton)c).FitHeight();
            }
            int textW = Math.Max(Ui.S(200), ClientSize.Width - Ui.S(40));
            foreach (var l in new[] { _trail, _question, _help }) l.MaximumSize = new Size(textW, 0);
        }

        // A big button with a bold title, a smaller grey hint under it, and the resulting code on
        // final answers (so people learn the codes as they go).
        private sealed class AnswerButton : Button
        {
            private readonly string _title, _hint, _code;
            private readonly Font _titleFont = Ui.Bold(10.5f);
            private readonly Font _hintFont = Ui.Regular(9f);
            private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix;

            public AnswerButton(string title, string hint, string code)
            {
                _title = title;
                _hint = hint;
                _code = code;
                Text = "";
                AccessibleName = hint == null ? title : $"{title}. {hint}";
                Margin = new Padding(0, 0, 0, Ui.S(8));
                Cursor = Cursors.Hand;
            }

            private int Pad => Ui.S(12);
            private int CodeWidth => _code == null ? Ui.S(16) : Ui.S(110);
            private int TextWidth => Math.Max(Ui.S(80), Width - Pad * 2 - CodeWidth);

            public void FitHeight()
            {
                int h = Pad + Measure(_title, _titleFont);
                if (_hint != null) h += Ui.S(3) + Measure(_hint, _hintFont);
                Height = h + Pad;
            }

            private int Measure(string text, Font font) =>
                TextRenderer.MeasureText(text, font, new Size(TextWidth, 0), Wrap).Height;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                int y = Pad;
                int titleH = Measure(_title, _titleFont);
                TextRenderer.DrawText(e.Graphics, _title, _titleFont, new Rectangle(Pad, y, TextWidth, titleH), ForeColor, Wrap);
                y += titleH + Ui.S(3);
                if (_hint != null)
                    TextRenderer.DrawText(e.Graphics, _hint, _hintFont, new Rectangle(Pad, y, TextWidth, Height - y), Ui.Muted, Wrap);

                var right = new Rectangle(Width - Pad - CodeWidth, 0, CodeWidth, Height);
                var flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
                if (_code != null)
                    TextRenderer.DrawText(e.Graphics, "Uses " + _code, _hintFont, right, Ui.Muted, flags);
                else
                    TextRenderer.DrawText(e.Graphics, ">", _titleFont, right, Ui.Muted, flags);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _titleFont.Dispose();
                    _hintFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
