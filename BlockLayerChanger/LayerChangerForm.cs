using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using DrawColor = System.Drawing.Color;

namespace BlockLayerChanger
{
    internal sealed class LayerChangerForm : Form
    {
        private readonly Document _doc;
        private Dictionary<ObjectId, BlockInfo> _blocks;

        // Check state is kept here so it survives filtering and list refreshes.
        private readonly HashSet<ObjectId> _checkedBlocks = new HashSet<ObjectId>();
        private readonly HashSet<string> _checkedFind = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressEvents;

        private readonly TextBox _blockFilter = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckedListBox _blockList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };

        private readonly RadioButton _byColour = new RadioButton { Text = "Colour", AutoSize = true, Checked = true };
        private readonly RadioButton _byLayer = new RadioButton { Text = "Layer", AutoSize = true };
        private readonly ListView _findList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        private readonly ImageList _swatches = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

        private readonly ComboBox _targetLayer = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        private readonly CheckBox _resetByLayer = new CheckBox { Text = "Set colour, linetype && lineweight to ByLayer", AutoSize = true, Checked = true };
        private readonly CheckBox _includeNested = new CheckBox { Text = "Include nested blocks", AutoSize = true, Checked = true };
        private readonly CheckBox _includeAttributes = new CheckBox { Text = "Include attributes on inserted blocks", AutoSize = true, Checked = true };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

        private bool FindByColour => _byColour.Checked;

        public LayerChangerForm(Document doc)
        {
            _doc = doc;

            Text = "Block Layer Changer";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(680, 500);
            Size = new Size(800, 600);
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;

            _findList.SmallImageList = _swatches;
            _findList.Columns.Add("Colour", 200);
            _findList.Columns.Add("Used in", 90);

            BuildLayout();

            _blockFilter.TextChanged += (s, e) => RefreshBlockList();
            _blockList.ItemCheck += BlockList_ItemCheck;
            _findList.ItemChecked += FindList_ItemChecked;
            _findList.Resize += (s, e) => _findList.Columns[0].Width = Math.Max(80, _findList.ClientSize.Width - _findList.Columns[1].Width - 4);
            _byColour.CheckedChanged += (s, e) => { _checkedFind.Clear(); RefreshFindList(); };
            _includeNested.CheckedChanged += (s, e) => RefreshFindList();

            Rescan();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 4 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            // --- 1. Blocks ---
            var blockGroup = new GroupBox { Text = "1. Blocks to search", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var blockPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            blockPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            blockPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            blockPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            blockPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            blockPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            blockPanel.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            blockPanel.Controls.Add(_blockFilter, 1, 0);
            blockPanel.Controls.Add(_blockList, 0, 1);
            blockPanel.SetColumnSpan(_blockList, 2);
            var blockButtons = ButtonRow(
                MakeButton("All", (s, e) => SetAllBlocks(true)),
                MakeButton("None", (s, e) => SetAllBlocks(false)),
                MakeButton("Pick in drawing <", (s, e) => PickBlocks()));
            blockPanel.Controls.Add(blockButtons, 0, 2);
            blockPanel.SetColumnSpan(blockButtons, 2);
            blockGroup.Controls.Add(blockPanel);
            root.Controls.Add(blockGroup, 0, 0);

            // --- 2. What to find ---
            var findGroup = new GroupBox { Text = "2. Find objects inside the checked blocks by", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var findPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            findPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            findPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            findPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            findPanel.Controls.Add(ButtonRow(_byColour, _byLayer), 0, 0);
            findPanel.Controls.Add(_findList, 0, 1);
            findPanel.Controls.Add(ButtonRow(
                MakeButton("All", (s, e) => SetAllFind(true)),
                MakeButton("None", (s, e) => SetAllFind(false))), 0, 2);
            findGroup.Controls.Add(findPanel);
            root.Controls.Add(findGroup, 1, 0);

            // --- 3. Target layer ---
            var targetGroup = new GroupBox
            {
                Text = "3. Put them on layer (type a new name to create it, leave blank to keep their layer)",
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8)
            };
            targetGroup.Controls.Add(_targetLayer);
            root.Controls.Add(targetGroup, 0, 1);
            root.SetColumnSpan(targetGroup, 2);

            // --- Options ---
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            options.Controls.Add(_resetByLayer);
            options.Controls.Add(_includeNested);
            options.Controls.Add(_includeAttributes);
            root.Controls.Add(options, 0, 2);
            root.SetColumnSpan(options, 2);

            // --- Bottom bar ---
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var apply = MakeButton("Apply", (s, e) => Apply());
            var close = MakeButton("Close", (s, e) => Close());
            bottom.Controls.Add(_status, 0, 0);
            bottom.Controls.Add(apply, 1, 0);
            bottom.Controls.Add(close, 2, 0);
            root.Controls.Add(bottom, 0, 3);
            root.SetColumnSpan(bottom, 2);

            AcceptButton = apply;
            CancelButton = close;
            Controls.Add(root);
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 26) };
            b.Click += onClick;
            return b;
        }

        private static FlowLayoutPanel ButtonRow(params Control[] controls)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            row.Controls.AddRange(controls);
            return row;
        }

        // ----------------------------------------------------------------- data

        private void Rescan()
        {
            _blocks = BlockScanner.Scan(_doc.Database);
            _checkedBlocks.RemoveWhere(id => !_blocks.ContainsKey(id));

            string typed = _targetLayer.Text;
            _targetLayer.BeginUpdate();
            _targetLayer.Items.Clear();
            _targetLayer.Items.AddRange(BlockScanner.GetLayerNames(_doc.Database).Cast<object>().ToArray());
            _targetLayer.EndUpdate();
            _targetLayer.Text = typed;

            RefreshBlockList();
        }

        private List<BlockInfo> SelectedBlocks() =>
            BlockScanner.Expand(_checkedBlocks, _blocks, _includeNested.Checked);

        private void RefreshBlockList()
        {
            string filter = _blockFilter.Text.Trim();
            var visible = _blocks.Values
                .Where(b => filter.Length == 0 || b.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _suppressEvents = true;
            _blockList.BeginUpdate();
            _blockList.Items.Clear();
            foreach (var b in visible)
                _blockList.Items.Add(b, _checkedBlocks.Contains(b.Id));
            _blockList.EndUpdate();
            _suppressEvents = false;

            RefreshFindList();
        }

        private void RefreshFindList()
        {
            bool byColour = FindByColour;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var colours = new Dictionary<string, AcColor>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in SelectedBlocks())
            {
                IEnumerable<string> keys = byColour ? block.Colors.Keys : block.Layers;
                foreach (var key in keys)
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                if (byColour)
                    foreach (var kv in block.Colors) colours[kv.Key] = kv.Value;
            }

            var ordered = byColour
                ? counts.Keys.OrderBy(k => ColourSortRank(colours[k])).ThenBy(k => colours[k].ColorIndex).ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                : counts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            _suppressEvents = true;
            _findList.BeginUpdate();
            _findList.Items.Clear();
            _findList.Columns[0].Text = byColour ? "Colour" : "Layer";
            foreach (var key in ordered)
            {
                int n = counts[key];
                var item = new ListViewItem(byColour ? ColourLabel(key) : key)
                {
                    Tag = key,
                    Checked = _checkedFind.Contains(key)
                };
                if (byColour) item.ImageKey = Swatch(key, colours[key]);
                item.SubItems.Add($"{n} block{(n == 1 ? "" : "s")}");
                _findList.Items.Add(item);
            }
            _findList.EndUpdate();
            _suppressEvents = false;

            UpdateStatus();
        }

        private static int ColourSortRank(AcColor c)
        {
            if (c.IsByLayer) return 0;
            if (c.IsByBlock) return 1;
            if (c.ColorMethod == ColorMethod.ByAci) return 2;
            return 3;
        }

        private static string ColourLabel(string key)
        {
            switch (key)
            {
                case "1": return "1 (red)";
                case "2": return "2 (yellow)";
                case "3": return "3 (green)";
                case "4": return "4 (cyan)";
                case "5": return "5 (blue)";
                case "6": return "6 (magenta)";
                case "7": return "7 (white/black)";
                default: return key;
            }
        }

        private string Swatch(string key, AcColor colour)
        {
            if (_swatches.Images.ContainsKey(key)) return key;
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(DrawColor.Transparent);
                var box = new Rectangle(1, 1, 13, 13);
                if (!colour.IsByLayer && !colour.IsByBlock)
                {
                    using (var brush = new SolidBrush(colour.ColorValue))
                        g.FillRectangle(brush, box);
                }
                g.DrawRectangle(Pens.Gray, box);
            }
            _swatches.Images.Add(key, bmp);
            return key;
        }

        private List<string> CheckedFindKeys() =>
            _findList.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag).ToList();

        private void UpdateStatus()
        {
            _status.Text = $"{_checkedBlocks.Count} block(s) checked, {CheckedFindKeys().Count} " +
                           (FindByColour ? "colour(s)" : "layer(s)") + " checked.";
        }

        // --------------------------------------------------------------- events

        private void BlockList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressEvents) return;
            var block = (BlockInfo)_blockList.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checkedBlocks.Add(block.Id);
            else _checkedBlocks.Remove(block.Id);
            // ItemCheck fires before the check box changes; refresh afterwards.
            BeginInvoke(new Action(RefreshFindList));
        }

        private void FindList_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_suppressEvents) return;
            var key = (string)e.Item.Tag;
            if (e.Item.Checked) _checkedFind.Add(key);
            else _checkedFind.Remove(key);
            UpdateStatus();
        }

        private void SetAllBlocks(bool check)
        {
            foreach (BlockInfo b in _blockList.Items)
            {
                if (check) _checkedBlocks.Add(b.Id);
                else _checkedBlocks.Remove(b.Id);
            }
            RefreshBlockList();
        }

        private void SetAllFind(bool check)
        {
            foreach (ListViewItem item in _findList.Items)
            {
                if (check) _checkedFind.Add((string)item.Tag);
                else _checkedFind.Remove((string)item.Tag);
            }
            RefreshFindList();
        }

        private void PickBlocks()
        {
            Editor ed = _doc.Editor;
            using (EditorUserInteraction ui = ed.StartUserInteraction(this))
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
                var opts = new PromptSelectionOptions { MessageForAdding = "\nSelect blocks to search: " };
                PromptSelectionResult res = ed.GetSelection(opts, filter);
                if (res.Status == PromptStatus.OK)
                {
                    using (var tr = _doc.Database.TransactionManager.StartOpenCloseTransaction())
                    {
                        foreach (ObjectId id in res.Value.GetObjectIds())
                        {
                            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                            ObjectId defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                            if (_blocks.ContainsKey(defId)) _checkedBlocks.Add(defId);
                        }
                        tr.Commit();
                    }
                }
                ui.End();
            }
            _blockFilter.Text = "";
            RefreshBlockList();
        }

        private void Apply()
        {
            string target = _targetLayer.Text.Trim();
            var keys = new HashSet<string>(CheckedFindKeys(), StringComparer.OrdinalIgnoreCase);
            bool byColour = FindByColour;

            if (_checkedBlocks.Count == 0) { Warn("Check at least one block."); return; }
            if (keys.Count == 0) { Warn(byColour ? "Check at least one colour to find." : "Check at least one layer to find."); return; }
            if (target.Length == 0 && !_resetByLayer.Checked) { Warn("Choose a layer to put them on, or tick \"Set ... to ByLayer\"."); return; }

            Func<Entity, bool> match = byColour
                ? (Func<Entity, bool>)(ent => keys.Contains(BlockScanner.ColorKey(ent.Color)))
                : ent => keys.Contains(ent.Layer);

            ChangeResult result;
            try
            {
                using (_doc.LockDocument())
                {
                    result = BlockScanner.ChangeEntities(_doc.Database, SelectedBlocks(), match,
                        target, _resetByLayer.Checked, _includeAttributes.Checked);
                }
                _doc.Editor.Regen();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change objects:\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string what = byColour ? "colour " + string.Join(", ", keys) : "layer " + string.Join(", ", keys);
            string msg = $"Changed {result.Entities} object(s)" +
                         (_includeAttributes.Checked ? $" and {result.Attributes} attribute(s)" : "") +
                         $" on {what} in {result.Blocks} block(s)" +
                         (target.Length > 0 ? $" -> layer \"{target}\"" : "") +
                         (_resetByLayer.Checked ? ", set to ByLayer." : ".");
            _doc.Editor.WriteMessage("\n" + msg);

            _checkedFind.Clear();
            Rescan();
            _status.Text = msg;
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
