using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    // One dialog for all three commands. LINEMARKING/8 open on the purpose list; ROAD opens with
    // Manual Override on (the flat code list). The checkbox flips between them either way.
    internal sealed class MainForm : Form
    {
        // Remembered for the rest of the Civil 3D session, so re-running lands where you left off.
        private static string _lastCode;
        private static bool _lastAllowMultiple = true;

        private readonly CheckBox _override;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Label _code;
        private readonly Label _label;
        private readonly Label _rms;
        private readonly Label _width;
        private readonly Label _description;
        private readonly Label _note;
        private readonly PatternPreview _preview;
        private readonly CheckBox _multiple;

        public LinemarkingItem SelectedItem { get; private set; }
        public bool AllowMultiple => _multiple.Checked;

        public MainForm(bool startInOverride, string cannoscale, string linPath)
        {
            Text = $"TTW Linemarking {PluginApp.Version}";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Ui.Regular();
            ClientSize = Ui.S(780, 520);
            MinimumSize = Ui.S(600, 420);

            // ---- top: logo, override toggle, search
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(12), Ui.S(6)) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var logo = Ui.TryLoadLogo();
            if (logo != null)
            {
                var pic = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = Ui.S(52, 52), Margin = new Padding(0, 0, Ui.S(12), 0) };
                top.Controls.Add(pic, 0, 0);
                top.SetRowSpan(pic, 2);
            }
            _override = new CheckBox { Text = "Manual Override - choose the linetype code directly", AutoSize = true };
            _search = new TextBox { Width = Ui.S(320), PlaceholderText = "Search purpose, code or description..." };
            top.Controls.Add(_override, 1, 0);
            top.Controls.Add(_search, 1, 1);

            // ---- left: the list
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.None,
            };
            _list.Columns.Add("Item");
            _list.Resize += (s, e) => _list.Columns[0].Width = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;

            // ---- right: details
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(Ui.S(14), Ui.S(4), Ui.S(4), Ui.S(4)), AutoScroll = true };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _code = new Label { AutoSize = true, Font = Ui.Mono(16f, FontStyle.Bold) };
            _label = new Label { AutoSize = true, ForeColor = Ui.Muted };
            _rms = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Mono(8.5f) };
            _width = new Label { AutoSize = true, Margin = new Padding(3, Ui.S(8), 3, 3) };
            _description = new Label { AutoSize = true, Margin = new Padding(3, Ui.S(4), 3, Ui.S(10)) };
            var previewCaption = new Label { Text = "LINE PREVIEW", AutoSize = true, Font = Ui.Bold(7.5f), ForeColor = Ui.Muted };
            _preview = new PatternPreview { Height = Ui.S(64), Dock = DockStyle.Top };
            _note = new Label { AutoSize = true, Margin = new Padding(3, Ui.S(10), 3, 3) };
            foreach (Control c in new Control[] { _code, _label, _rms, _width, _description, previewCaption, _preview, _note })
                details.Controls.Add(c);
            details.Resize += (s, e) => WrapDetails(details);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Padding = new Padding(Ui.S(12), 0, 0, 0);
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(details);

            // ---- bottom: options, status, buttons
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), Ui.S(10)) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var info = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            _multiple = new CheckBox { Text = "Allow multiple polylines", AutoSize = true, Checked = _lastAllowMultiple };
            info.Controls.Add(_multiple);
            info.Controls.Add(new Label { Text = $"Current scale: {cannoscale}", AutoSize = true });
            info.Controls.Add(new Label { Text = $"LIN file: {Path.GetFileName(linPath)}", AutoSize = true, Font = Ui.Mono(8.5f) });
            var source = new Label { Text = $"from {Path.GetDirectoryName(linPath)}", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Regular(7.5f) };
            info.Controls.Add(source);
            info.Controls.Add(new Label { Text = "Brought to you by Corey Young", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Regular(7.5f) });

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var guided = new Button { Text = "Guided Questions...", Size = Ui.S(170, 30) };
            var apply = new Button { Text = "Apply to Selection", Size = Ui.S(160, 30) };
            var cancel = new Button { Text = "Cancel", Size = Ui.S(100, 30), DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { guided, apply, cancel });

            bottom.Controls.Add(info, 0, 0);
            bottom.Controls.Add(buttons, 1, 0);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(top);
            AcceptButton = apply;
            CancelButton = cancel;

            // SplitterDistance has to be set once the container has its real size.
            Load += (s, e) => split.SplitterDistance = Ui.S(340);

            _override.Checked = startInOverride;
            _override.CheckedChanged += (s, e) => Populate(CurrentItem()?.Code);
            _search.TextChanged += (s, e) => Populate(CurrentItem()?.Code);
            _list.SelectedIndexChanged += (s, e) => ShowDetails();
            _list.DoubleClick += (s, e) => Apply();
            apply.Click += (s, e) => Apply();
            guided.Click += (s, e) => RunGuided();

            Populate(_lastCode);
        }

        private LinemarkingItem CurrentItem() =>
            _list.SelectedItems.Count > 0 ? (LinemarkingItem)_list.SelectedItems[0].Tag : null;

        private void Populate(string keepCode)
        {
            string filter = _search.Text.Trim();
            IEnumerable<LinemarkingItem> items = Catalogue.Items;
            if (filter.Length > 0)
            {
                items = items.Where(i =>
                    Contains(i.Code, filter) || Contains(i.Label, filter) ||
                    Contains(i.Description, filter) || Contains(i.RmsCode, filter));
            }

            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();
            if (_override.Checked)
            {
                foreach (var item in items.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase))
                    _list.Items.Add(new ListViewItem(item.Code) { Tag = item });
            }
            else
            {
                var list = items.ToList();
                foreach (string cat in Catalogue.Categories)
                {
                    var inCat = list.Where(i => i.Category == cat).ToList();
                    if (inCat.Count == 0) continue;
                    var group = new ListViewGroup(cat);
                    _list.Groups.Add(group);
                    foreach (var item in inCat)
                        _list.Items.Add(new ListViewItem(item.Label) { Tag = item, Group = group });
                }
            }
            _list.EndUpdate();

            var keep = _list.Items.Cast<ListViewItem>().FirstOrDefault(l => ((LinemarkingItem)l.Tag).Code == keepCode)
                       ?? (_list.Items.Count > 0 ? _list.Items[0] : null);
            if (keep != null)
            {
                keep.Selected = true;
                keep.Focused = true;
                keep.EnsureVisible();
            }
            ShowDetails();
        }

        private static bool Contains(string text, string filter) =>
            text != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ShowDetails()
        {
            var item = CurrentItem();
            _preview.Item = item;
            if (item == null)
            {
                _code.Text = "";
                _label.Text = "No matches.";
                _rms.Text = _width.Text = _description.Text = _note.Text = "";
                return;
            }

            _code.Text = item.Code;
            _label.Text = item.Label;
            _rms.Text = item.RmsCode != null ? $"RMS Code = {item.RmsCode}" : "No RMS code";
            string colour = item.Paint == Core.Paint.White ? "" : $"   Colour: {item.Paint.ToString().ToUpperInvariant()}";
            _width.Text = $"Applied width: {item.Width:0.00}   (standard notes: {item.StandardNote}){colour}";
            _description.Text = item.Description + ".";
            _note.Text = NoteFor(item);
            _note.ForeColor = item.NeedsManualOffset ? Ui.Red : SystemColors.ControlText;
        }

        private static string NoteFor(LinemarkingItem item)
        {
            if (item.NeedsManualOffset)
                return "Double line - gap distance not confirmed yet. Only one line is drawn; offset the second by hand.";
            if (item.Pair == null)
                return "";
            string both = item.Pair.PartnerCode == item.Code
                ? $"two {item.Code} lines"
                : $"{item.Code} on the side you pick and {item.Pair.PartnerCode} on the other";
            return $"Paired marking: select the CENTRELINE. Creates {both}, {item.Pair.Gap:0.00} clear gap, "
                 + $"and moves the centreline to {Catalogue.ReferenceLayer} (stays visible, never plots).";
        }

        private void WrapDetails(Control details)
        {
            int w = Math.Max(Ui.S(120), details.ClientSize.Width - details.Padding.Horizontal - Ui.S(10));
            foreach (var l in new[] { _width, _description, _note }) l.MaximumSize = new Size(w, 0);
            _preview.Width = w;
        }

        private void Apply()
        {
            var item = CurrentItem();
            if (item == null)
            {
                MessageBox.Show(this, "Pick an item from the list first.", "TTW Linemarking", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SelectedItem = item;
            _lastCode = item.Code;
            _lastAllowMultiple = _multiple.Checked;
            DialogResult = DialogResult.OK;
        }

        private void RunGuided()
        {
            using (var wizard = new GuidedQuestionsForm())
            {
                if (wizard.ShowDialog(this) != DialogResult.OK || wizard.SelectedCode == null) return;
                _search.Text = "";
                _override.Checked = false;
                Populate(wizard.SelectedCode);
                _list.Focus();
            }
        }
    }
}
