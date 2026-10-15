using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace BlockLayerChanger
{
    internal sealed class LayerChangerForm : Form
    {
        private readonly Document _doc;
        private Dictionary<ObjectId, BlockInfo> _blocks;

        // Check state is kept here so it survives filtering and list refreshes.
        private readonly HashSet<ObjectId> _checkedBlocks = new HashSet<ObjectId>();
        private readonly HashSet<string> _checkedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressEvents;

        private readonly TextBox _blockFilter = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckedListBox _blockList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly CheckedListBox _layerList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly ComboBox _targetLayer = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        private readonly CheckBox _includeNested = new CheckBox { Text = "Include nested blocks", AutoSize = true, Checked = true };
        private readonly CheckBox _includeAttributes = new CheckBox { Text = "Also change attributes on inserted blocks", AutoSize = true, Checked = true };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };

        private sealed class LayerItem
        {
            public string Name;
            public int BlockCount;
            public override string ToString() => $"{Name}   ({BlockCount} block{(BlockCount == 1 ? "" : "s")})";
        }

        public LayerChangerForm(Document doc)
        {
            _doc = doc;

            Text = "Block Layer Changer";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 480);
            Size = new Size(760, 560);
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;

            BuildLayout();

            _blockFilter.TextChanged += (s, e) => RefreshBlockList();
            _blockList.ItemCheck += BlockList_ItemCheck;
            _layerList.ItemCheck += LayerList_ItemCheck;
            _includeNested.CheckedChanged += (s, e) => RefreshLayerList();

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

            // --- 2. Layers ---
            var layerGroup = new GroupBox { Text = "2. Layers to find (inside the checked blocks)", Dock = DockStyle.Fill, Padding = new Padding(8) };
            var layerPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layerPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layerPanel.Controls.Add(_layerList, 0, 0);
            layerPanel.Controls.Add(ButtonRow(
                MakeButton("All", (s, e) => SetAllLayers(true)),
                MakeButton("None", (s, e) => SetAllLayers(false))), 0, 1);
            layerGroup.Controls.Add(layerPanel);
            root.Controls.Add(layerGroup, 1, 0);

            // --- 3. Target layer ---
            var targetGroup = new GroupBox { Text = "3. Change them to layer (type a new name to create it)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            targetGroup.Controls.Add(_targetLayer);
            root.Controls.Add(targetGroup, 0, 1);
            root.SetColumnSpan(targetGroup, 2);

            // --- Options ---
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
            options.Controls.Add(_includeNested);
            options.Controls.Add(_includeAttributes);
            root.Controls.Add(options, 0, 2);
            root.SetColumnSpan(options, 2);

            // --- Bottom bar ---
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var apply = MakeButton("Change Layers", (s, e) => Apply());
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

        private static FlowLayoutPanel ButtonRow(params Control[] buttons)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            row.Controls.AddRange(buttons);
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

            RefreshLayerList();
        }

        private void RefreshLayerList()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in SelectedBlocks())
                foreach (var layer in block.Layers)
                    counts[layer] = counts.TryGetValue(layer, out int n) ? n + 1 : 1;

            _suppressEvents = true;
            _layerList.BeginUpdate();
            _layerList.Items.Clear();
            foreach (var kv in counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                _layerList.Items.Add(new LayerItem { Name = kv.Key, BlockCount = kv.Value }, _checkedLayers.Contains(kv.Key));
            _layerList.EndUpdate();
            _suppressEvents = false;

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int layers = _layerList.CheckedItems.Count;
            _status.Text = $"{_checkedBlocks.Count} block(s) checked, {layers} layer(s) checked.";
        }

        // --------------------------------------------------------------- events

        private void BlockList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressEvents) return;
            var block = (BlockInfo)_blockList.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checkedBlocks.Add(block.Id);
            else _checkedBlocks.Remove(block.Id);
            // ItemCheck fires before the check box changes; refresh afterwards.
            BeginInvoke(new Action(RefreshLayerList));
        }

        private void LayerList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressEvents) return;
            var layer = (LayerItem)_layerList.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checkedLayers.Add(layer.Name);
            else _checkedLayers.Remove(layer.Name);
            BeginInvoke(new Action(UpdateStatus));
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

        private void SetAllLayers(bool check)
        {
            foreach (LayerItem l in _layerList.Items)
            {
                if (check) _checkedLayers.Add(l.Name);
                else _checkedLayers.Remove(l.Name);
            }
            RefreshLayerList();
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
            var fromLayers = new HashSet<string>(_checkedLayers.Where(l =>
                _layerList.Items.Cast<LayerItem>().Any(i => i.Name.Equals(l, StringComparison.OrdinalIgnoreCase))),
                StringComparer.OrdinalIgnoreCase);

            if (_checkedBlocks.Count == 0) { Warn("Check at least one block."); return; }
            if (fromLayers.Count == 0) { Warn("Check at least one layer to find."); return; }
            if (target.Length == 0) { Warn("Choose or type the layer to change to."); return; }

            ChangeResult result;
            try
            {
                using (_doc.LockDocument())
                {
                    result = BlockScanner.ChangeLayers(_doc.Database, SelectedBlocks(), fromLayers,
                        target, _includeAttributes.Checked);
                }
                _doc.Editor.Regen();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change layers:\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string msg = $"Moved {result.Entities} object(s)" +
                         (_includeAttributes.Checked ? $" and {result.Attributes} attribute(s)" : "") +
                         $" in {result.Blocks} block(s) to layer \"{target}\".";
            _doc.Editor.WriteMessage("\n" + msg);

            _checkedLayers.Clear();
            Rescan();
            _status.Text = msg;
        }

        private void Warn(string message) =>
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
