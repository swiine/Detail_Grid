using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    // Walks the GuidedQuestions tree one question at a time. Knows nothing about the content, so
    // the real question wording can replace the placeholder tree without touching this file.
    internal sealed class GuidedQuestionsForm : Form
    {
        private readonly Stack<Question> _history = new Stack<Question>();
        private readonly Label _question;
        private readonly FlowLayoutPanel _answers;

        public string SelectedCode { get; private set; }

        public GuidedQuestionsForm()
        {
            Text = "TTW Linemarking - Guided Questions";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Ui.Regular(10f);
            ClientSize = Ui.S(560, 460);

            _question = new Label
            {
                Dock = DockStyle.Top,
                Height = Ui.S(52),
                Padding = new Padding(Ui.S(16), Ui.S(14), Ui.S(16), 0),
                Font = Ui.Bold(12f),
            };
            _answers = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(Ui.S(16), Ui.S(4), Ui.S(16), Ui.S(16)),
            };
            _answers.SizeChanged += (s, e) => FitButtons();

            Controls.Add(_answers);
            Controls.Add(_question);
            ShowQuestion(GuidedQuestions.Root);
        }

        private void ShowQuestion(Question q)
        {
            _question.Text = q.Text;
            _answers.SuspendLayout();
            while (_answers.Controls.Count > 0) _answers.Controls[0].Dispose();

            if (_history.Count > 0)
            {
                var back = Choice("< Back");
                back.BackColor = SystemColors.ControlLight;
                back.Click += (s, e) => ShowQuestion(_history.Pop());
                _answers.Controls.Add(back);
            }

            foreach (var answer in q.Answers)
            {
                var btn = Choice(answer.Text);
                btn.Click += (s, e) =>
                {
                    if (answer.Next != null)
                    {
                        _history.Push(q);
                        ShowQuestion(answer.Next);
                    }
                    else
                    {
                        SelectedCode = answer.Code;
                        DialogResult = DialogResult.OK;
                    }
                };
                _answers.Controls.Add(btn);
            }

            FitButtons();
            _answers.ResumeLayout();
        }

        private Button Choice(string text) => new Button
        {
            Text = text,
            Height = Ui.S(38),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, Ui.S(6)),
        };

        private void FitButtons()
        {
            int w = _answers.ClientSize.Width - _answers.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            foreach (Control c in _answers.Controls) c.Width = w;
        }
    }
}
