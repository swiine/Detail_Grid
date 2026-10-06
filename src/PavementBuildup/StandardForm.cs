using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PavementBuildup.Core;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using FlowDirection = System.Windows.Forms.FlowDirection;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace PavementBuildup;

/// <summary>Editor for a <see cref="CadStandard"/>: layers, hatch rules, text/dimension styles and label wording.</summary>
internal sealed class StandardForm : Form
{
    private CadStandard _standard;
    private string _path;
    private readonly Document? _doc;
    private readonly DetailSettings _settings;

    private readonly BindingList<LayerStyle> _layerRows = new();
    private readonly BindingList<HatchRule> _ruleRows = new();
    private readonly BindingList<HatchRule> _specialRows = new();

    private readonly TextBox _name = new() { Width = 260 };
    private readonly Label _pathLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly DataGridView _layersGrid = NewGrid();
    private readonly DataGridView _rulesGrid = NewGrid();
    private readonly DataGridView _specialGrid = NewGrid();
    private readonly TextBox _testInput = new() { Width = 260, PlaceholderText = "e.g. 150 Type 1 sub-base" };
    private readonly Label _testResult = new() { AutoSize = true };
    private DataGridView _lastRuleGrid;
    private readonly NumericUpDown _pickScale = new() { Minimum = 1, Maximum = 1000, Value = 10, Width = 60 };

    private readonly ComboBox _textStyle = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _dimStyle = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly NumericUpDown _textHeight = new() { Minimum = 0.5m, Maximum = 50, DecimalPlaces = 2, Increment = 0.5m, Width = 70 };
    private readonly NumericUpDown _titleHeight = new() { Minimum = 0.5m, Maximum = 50, DecimalPlaces = 2, Increment = 0.5m, Width = 70 };
    private readonly NumericUpDown _membraneWidth = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 2, Increment = 0.05m, Width = 70 };
    private readonly CheckBox _upper = new() { Text = "Upper-case labels and title", AutoSize = true };
    private readonly TextBox _labelFormat = new() { Width = 320 };
    private readonly TextBox _membraneFormat = new() { Width = 320 };
    private readonly TextBox _dimFormat = new() { Width = 320 };
    private readonly TextBox _titleFormat = new() { Width = 320 };
    private readonly TextBox _scaleFormat = new() { Width = 320 };
    private readonly TextBox _totalFormat = new() { Width = 320 };
    private readonly ComboBox _drawingUnits = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _detailName = new() { Width = 320 };
    private readonly TextBox _barPrefix = new() { Width = 60 };
    private readonly TextBox _barFormat = new() { Width = 320 };
    private readonly TextBox _rebarLabel = new() { Width = 320 };
    private readonly TextBox _topFace = new() { Width = 80 };
    private readonly TextBox _bottomFace = new() { Width = 80 };
    private readonly CheckBox _coverDim = new() { Text = "Dimension the cover", AutoSize = true };
    private readonly TextBox _coverDimFormat = new() { Width = 320 };

    private readonly AutoCompleteStringCollection _drawingLayers = new();
    private readonly AutoCompleteStringCollection _drawingLinetypes = new();
    private readonly AutoCompleteStringCollection _patterns = new();

    public CadStandard Result => _standard;
    public string ResultPath => _path;

    public StandardForm(CadStandard standard, string path, DetailSettings settings, Document? doc)
    {
        _standard = standard.Clone().Normalize();
        _path = path;
        _settings = settings;
        _doc = doc;
        _pickScale.Value = (decimal)Math.Clamp(settings.ScaleDenominator, 1, 1000);
        _lastRuleGrid = _rulesGrid;
        _rulesGrid.Enter += (_, _) => _lastRuleGrid = _rulesGrid;
        _specialGrid.Enter += (_, _) => _lastRuleGrid = _specialGrid;

        Text = "Pavement Build-up — CAD Standard";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1150, 720);
        MinimumSize = new Size(900, 560);
        Font = SystemFonts.MessageBoxFont ?? Font;

        ReadDrawingNames();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Layers", BuildLayersTab()));
        tabs.TabPages.Add(Page("Hatches", BuildHatchTab()));
        tabs.TabPages.Add(Page("Text, labels && bars", BuildTextTab()));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader());
        root.Controls.Add(tabs);
        root.Controls.Add(BuildButtons());
        Controls.Add(root);

        LoadStandard(_standard);
    }

    // ------------------------------------------------------------------ layout -------------

    private Control BuildHeader()
    {
        var open = new Button { Text = "Open…", AutoSize = true };
        var saveAs = new Button { Text = "Save as…", AutoSize = true };
        var reset = new Button { Text = "Reset to built-in", AutoSize = true };
        open.Click += (_, _) => OpenFile();
        saveAs.Click += (_, _) => SaveAs();
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Replace everything in this standard with the built-in defaults?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                LoadStandard(CadStandard.CreateDefault());
        };

        var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        f.Controls.AddRange(new Control[] { Lbl("Standard name:"), _name, open, saveAs, reset, _pathLabel });
        return f;
    }

    private Control BuildLayersTab()
    {
        _layersGrid.AllowUserToAddRows = false;
        _layersGrid.AllowUserToDeleteRows = false;
        _layersGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LayerStyle.Element), HeaderText = "Used for", ReadOnly = true, FillWeight = 70 });
        _layersGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LayerStyle.Name), HeaderText = "Layer name", FillWeight = 150, Tag = _drawingLayers });
        _layersGrid.Columns.Add(ColorColumn(nameof(LayerStyle.Color), "Colour"));
        _layersGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(LayerStyle.Linetype), HeaderText = "Linetype", FillWeight = 80, Tag = _drawingLinetypes });
        _layersGrid.Columns.Add(LineWeightColumn());
        _layersGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(LayerStyle.Plot), HeaderText = "Plot", FillWeight = 30 });
        _layersGrid.DataSource = _layerRows;
        WireGrid(_layersGrid, allowByLayer: false);

        var help = Note("Layers that already exist in the drawing (e.g. from your company template) are used as they are. " +
                        "Colour, linetype, lineweight and plot only apply when the plugin has to create the layer. " +
                        "Double-click a colour to pick one. Several elements can share a layer.");
        return Stack(help, _layersGrid);
    }

    private Control BuildHatchTab()
    {
        AddRuleColumns(_rulesGrid, withKeywords: true);
        _rulesGrid.DataSource = _ruleRows;
        WireGrid(_rulesGrid, allowByLayer: true);
        _ruleRows.AddingNew += (_, e) => e.NewObject = new HatchRule { Name = "New rule", Pattern = "ANSI31", Scale = 0.5 };

        _specialGrid.AllowUserToAddRows = false;
        _specialGrid.AllowUserToDeleteRows = false;
        _specialGrid.Height = 90;
        _specialGrid.Dock = DockStyle.Bottom;
        AddRuleColumns(_specialGrid, withKeywords: false);
        _specialGrid.Columns[0].ReadOnly = true;
        _specialGrid.DataSource = _specialRows;
        WireGrid(_specialGrid, allowByLayer: true);

        var up = new Button { Text = "▲ Up", Width = 120 };
        var down = new Button { Text = "▼ Down", Width = 120 };
        var add = new Button { Text = "Add rule", Width = 120 };
        var remove = new Button { Text = "Remove", Width = 120 };
        var pick = new Button { Text = "Pick from drawing…", Width = 120, Enabled = _doc is not null };
        up.Click += (_, _) => MoveRule(-1);
        down.Click += (_, _) => MoveRule(+1);
        add.Click += (_, _) =>
        {
            _rulesGrid.EndEdit();
            _ruleRows.Add(new HatchRule { Name = "New rule", Keywords = "", Pattern = "ANSI31", Scale = 0.5 });
            _rulesGrid.CurrentCell = _rulesGrid.Rows[_ruleRows.Count - 1].Cells[0];
        };
        remove.Click += (_, _) =>
        {
            if (CurrentRuleIndex() is int i) _ruleRows.RemoveAt(i);
        };
        pick.Click += (_, _) => PickHatchFromDrawing();

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        side.Controls.AddRange(new Control[] { up, down, add, remove, new Label { Height = 10 }, pick, Lbl("picked hatch is at 1:"), _pickScale });

        var rules = new Panel { Dock = DockStyle.Fill };
        rules.Controls.Add(_rulesGrid);
        rules.Controls.Add(side);

        _testInput.TextChanged += (_, _) => UpdateTest();
        var test = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        test.Controls.AddRange(new Control[] { Lbl("Test a course description:"), _testInput, _testResult });

        var help = Note("Rules are checked top to bottom; the first rule with a keyword found in the course description wins. " +
                        "Scale is the pattern scale for a 1:1 detail in a millimetre drawing — it is multiplied by the detail scale (and drawing units) when drawn. " +
                        "Pattern can be any acadiso.pat pattern, a custom pattern (NAME.pat on the support path), SOLID, or NONE. " +
                        "Layer blank = the Hatch layer. Use Pick from drawing to copy an existing hatch into the selected rule.");
        var specialLabel = Lbl("When no rule matches, and for the subgrade strip:");
        specialLabel.Dock = DockStyle.Bottom;

        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(rules);
        panel.Controls.Add(specialLabel);
        panel.Controls.Add(_specialGrid);
        panel.Controls.Add(test);
        return Stack(help, panel);
    }

    private Control BuildTextTab()
    {
        _textStyle.Items.AddRange(ReadSymbolNames(db => db.TextStyleTableId).Prepend("").ToArray());
        _dimStyle.Items.AddRange(ReadSymbolNames(db => db.DimStyleTableId).Prepend("").ToArray());

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoScroll = true, Padding = new Padding(6) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c, string note)
        {
            t.Controls.Add(Lbl(label));
            t.Controls.Add(c);
            t.Controls.Add(new Label { Text = note, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 7, 3, 3) });
        }
        _drawingUnits.Items.AddRange(DetailLayout.StandardUnitChoices);
        Row("Drawing units:", _drawingUnits, "M = 1 drawing unit is 1 metre (40mm draws as 0.04). Thicknesses are always typed in mm. AUTO reads INSUNITS.");
        Row("Detail name:", _detailName, "Block/group name. # = next number, {name} = build-up name. e.g. \"TTW_pavement-profile_#\"");
        Row("Text style:", _textStyle, "Blank = the drawing's current style. Must exist in the drawing (put it in your template).");
        Row("Dimension style:", _dimStyle, "Blank = current style. A named style keeps its own text height; DIMSCALE is set from the detail scale.");
        Row("Label text height (mm):", _textHeight, "Plotted height.");
        Row("Title text height (mm):", _titleHeight, "Plotted height of the title line.");
        Row("Membrane line width (mm):", _membraneWidth, "Plotted width of zero-thickness courses (geotextile, DPM). 0 = thin line.");
        Row("", _upper, "");
        Row("Course label:", _labelFormat, "{thickness} {description}   e.g. \"{thickness}mm {description}\" or \"{description} ({thickness}mm THK)\"");
        Row("Membrane label:", _membraneFormat, "{description}");
        Row("Dimension text:", _dimFormat, "{thickness}   e.g. \"{thickness}\" or \"{thickness}mm\"");
        Row("Title:", _titleFormat, "{name} {scale} {total}");
        Row("Scale line:", _scaleFormat, "{scale}   blank = no line");
        Row("Total depth line:", _totalFormat, "{total}   blank = no line");
        Row("Bar prefix:", _barPrefix, "Used when the bar is typed without one, e.g. H, T, N, Ø. Reinforcement layer is on the Layers tab.");
        Row("Bar text:", _barFormat, "{prefix} {diameter} {spacing}   e.g. \"{prefix}{diameter} @ {spacing} c/c\" or \"{prefix}{diameter}-{spacing}\"");
        Row("Reinforcement label:", _rebarLabel, "{bars} {transverse} {face} {cover}   \"+ {transverse}\" is dropped when there are no transverse bars");
        Row("Top / bottom face text:", FacePanel(), "What {face} becomes for top and bottom mats.");
        Row("", _coverDim, "");
        Row("Cover dimension text:", _coverDimFormat, "{cover}   e.g. \"{cover}\" or \"{cover} COVER\"");
        return t;
    }

    private Control BuildButtons()
    {
        var ok = new Button { Text = "Save && close", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (TrySave(_path))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        CancelButton = cancel;
        var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        f.Controls.Add(cancel);
        f.Controls.Add(ok);
        return f;
    }

    private void AddRuleColumns(DataGridView grid, bool withKeywords)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Name), HeaderText = "Rule", FillWeight = 90 });
        if (withKeywords)
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Keywords), HeaderText = "Keywords (comma separated)", FillWeight = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Pattern), HeaderText = "Pattern", FillWeight = 70, Tag = _patterns });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Scale), HeaderText = "Scale (1:1)", FillWeight = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Angle), HeaderText = "Angle °", FillWeight = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HatchRule.Layer), HeaderText = "Layer (blank = Hatch layer)", FillWeight = 110, Tag = _drawingLayers });
        grid.Columns.Add(ColorColumn(nameof(HatchRule.Color), "Colour"));
        grid.Columns.Add(ColorColumn(nameof(HatchRule.BackgroundColor), "Background"));
    }

    // ------------------------------------------------------------------ behaviour ----------

    private void LoadStandard(CadStandard s)
    {
        _standard = s.Clone().Normalize();
        _name.Text = _standard.Name;
        _pathLabel.Text = _path;

        _layerRows.Clear();
        foreach (var l in _standard.Layers)
        {
            l.LineWeightMm = SnapLineWeight(l.LineWeightMm);
            _layerRows.Add(l);
        }
        _ruleRows.Clear();
        foreach (var r in _standard.HatchRules)
            _ruleRows.Add(r);
        _specialRows.Clear();
        _standard.DefaultHatch.Name = "(no match)";
        _standard.SubgradeHatch.Name = "(subgrade)";
        _specialRows.Add(_standard.DefaultHatch);
        _specialRows.Add(_standard.SubgradeHatch);
        foreach (var p in _standard.HatchRules.Select(r => r.Pattern).Where(p => !_patterns.Contains(p)))
            _patterns.Add(p);

        _textStyle.Text = _standard.TextStyle;
        _dimStyle.Text = _standard.DimensionStyle;
        _textHeight.Value = Clamp(_textHeight, _standard.TextHeightMm);
        _titleHeight.Value = Clamp(_titleHeight, _standard.TitleHeightMm);
        _membraneWidth.Value = Clamp(_membraneWidth, _standard.MembraneWidthMm);
        _upper.Checked = _standard.UpperCaseLabels;
        _labelFormat.Text = _standard.LabelFormat;
        _membraneFormat.Text = _standard.MembraneLabelFormat;
        _dimFormat.Text = _standard.DimensionFormat;
        _titleFormat.Text = _standard.TitleFormat;
        _scaleFormat.Text = _standard.ScaleFormat;
        _totalFormat.Text = _standard.TotalFormat;
        _drawingUnits.SelectedItem = DetailLayout.StandardUnitChoices.Contains((_standard.DrawingUnits ?? "").ToUpperInvariant())
            ? _standard.DrawingUnits!.ToUpperInvariant() : "M";
        _detailName.Text = _standard.DetailNameFormat;
        _barPrefix.Text = _standard.BarPrefix;
        _barFormat.Text = _standard.BarFormat;
        _rebarLabel.Text = _standard.ReinforcementLabelFormat;
        _topFace.Text = _standard.TopFaceText;
        _bottomFace.Text = _standard.BottomFaceText;
        _coverDim.Checked = _standard.ShowCoverDimension;
        _coverDimFormat.Text = _standard.CoverDimensionFormat;
        UpdateTest();
    }

    /// <summary>Collects the form into a standard (the grids already edit the row objects in place).</summary>
    private CadStandard Collect(bool endEdit = true)
    {
        if (endEdit)
            foreach (var g in new[] { _layersGrid, _rulesGrid, _specialGrid })
                g.EndEdit();

        var s = _standard.Clone();
        s.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Untitled" : _name.Text.Trim();
        s.Layers = _layerRows.Select(l => Trimmed(l.Clone())).ToList();
        s.HatchRules = _ruleRows.Select(r => Trimmed(r.Clone())).ToList();
        s.DefaultHatch = Trimmed(_specialRows[0].Clone());
        s.DefaultHatch.Name = "Default";
        s.SubgradeHatch = Trimmed(_specialRows[1].Clone());
        s.SubgradeHatch.Name = "Subgrade";
        s.TextStyle = _textStyle.Text.Trim();
        s.DimensionStyle = _dimStyle.Text.Trim();
        s.TextHeightMm = (double)_textHeight.Value;
        s.TitleHeightMm = (double)_titleHeight.Value;
        s.MembraneWidthMm = (double)_membraneWidth.Value;
        s.UpperCaseLabels = _upper.Checked;
        s.LabelFormat = _labelFormat.Text;
        s.MembraneLabelFormat = _membraneFormat.Text;
        s.DimensionFormat = _dimFormat.Text;
        s.TitleFormat = _titleFormat.Text;
        s.ScaleFormat = _scaleFormat.Text;
        s.TotalFormat = _totalFormat.Text;
        s.DrawingUnits = _drawingUnits.SelectedItem as string ?? "M";
        s.DetailNameFormat = string.IsNullOrWhiteSpace(_detailName.Text) ? DetailNaming.DefaultFormat : _detailName.Text.Trim();
        s.BarPrefix = _barPrefix.Text.Trim();
        s.BarFormat = _barFormat.Text;
        s.ReinforcementLabelFormat = _rebarLabel.Text;
        s.TopFaceText = _topFace.Text;
        s.BottomFaceText = _bottomFace.Text;
        s.ShowCoverDimension = _coverDim.Checked;
        s.CoverDimensionFormat = _coverDimFormat.Text;
        return s.Normalize();
    }

    private static LayerStyle Trimmed(LayerStyle l)
    {
        l.Name = l.Name?.Trim() ?? "";
        l.Linetype = string.IsNullOrWhiteSpace(l.Linetype) ? "Continuous" : l.Linetype.Trim();
        l.Color = l.Color?.Trim() ?? "7";
        return l;
    }

    private static HatchRule Trimmed(HatchRule r)
    {
        r.Name = r.Name?.Trim() ?? "";
        r.Keywords = r.Keywords?.Trim() ?? "";
        r.Pattern = (r.Pattern?.Trim() ?? "").ToUpperInvariant();
        r.Layer = r.Layer?.Trim() ?? "";
        r.Color = string.IsNullOrWhiteSpace(r.Color) ? "BYLAYER" : r.Color.Trim().ToUpperInvariant();
        r.BackgroundColor = r.BackgroundColor?.Trim() ?? "";
        return r;
    }

    private bool TrySave(string path)
    {
        var s = Collect();
        var errors = s.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, "Fix these before saving:\n\n" + string.Join("\n", errors.Take(15)), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        try
        {
            StandardStore.Save(s, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save {path}:\n{ex.Message}\n\nIf this is the shared company standard you may not have write access — use Save as… for a personal copy.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _standard = s;
        _path = path;
        _pathLabel.Text = path;
        return true;
    }

    private void OpenFile()
    {
        using var dlg = new OpenFileDialog { Filter = "CAD standard (*.json)|*.json", Title = "Open CAD standard", FileName = Path.GetFileName(_path) };
        TrySetFolder(dlg, _path);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _path = dlg.FileName;
            LoadStandard(StandardStore.Load(dlg.FileName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveAs()
    {
        using var dlg = new SaveFileDialog { Filter = "CAD standard (*.json)|*.json", Title = "Save CAD standard as", FileName = SafeFileName(_name.Text) + ".json" };
        TrySetFolder(dlg, _path);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            TrySave(dlg.FileName);
    }

    private void MoveRule(int delta)
    {
        if (CurrentRuleIndex() is not int i) return;
        int j = i + delta;
        if (j < 0 || j >= _ruleRows.Count) return;
        _rulesGrid.EndEdit();
        var item = _ruleRows[i];
        _ruleRows.RemoveAt(i);
        _ruleRows.Insert(j, item);
        _rulesGrid.CurrentCell = _rulesGrid.Rows[j].Cells[0];
    }

    private int? CurrentRuleIndex()
    {
        var row = _rulesGrid.CurrentRow;
        return row is null || row.IsNewRow || row.Index >= _ruleRows.Count ? null : row.Index;
    }

    private void UpdateTest()
    {
        var text = _testInput.Text.Trim();
        if (text.Length == 0)
        {
            _testResult.Text = "";
            return;
        }
        var probe = Collect(endEdit: false);
        var rule = probe.RuleFor(text);
        _testResult.Text = rule == probe.DefaultHatch
            ? $"→ no rule matches, uses the default hatch ({rule.Pattern})"
            : $"→ rule \"{rule.Name}\": {(rule.IsNone ? "not hatched" : $"{rule.Pattern} at scale {rule.Scale:0.###}")}";
    }

    /// <summary>Copies pattern, scale, angle, layer and colours from a hatch the user picks into the selected rule.</summary>
    private void PickHatchFromDrawing()
    {
        if (_doc is null) return;
        _rulesGrid.EndEdit();
        _specialGrid.EndEdit();

        HatchRule? target = _lastRuleGrid == _specialGrid && _specialGrid.CurrentRow?.Index is int si && si < _specialRows.Count
            ? _specialRows[si]
            : CurrentRuleIndex() is int ri ? _ruleRows[ri] : null;
        if (target is null)
        {
            MessageBox.Show(this, "Select the rule to copy the hatch into first.", Text);
            return;
        }

        var ed = _doc.Editor;
        using (ed.StartUserInteraction(Handle))
        {
            var opts = new PromptEntityOptions("\nSelect a hatch drawn to your company standard: ");
            opts.SetRejectMessage("\nThat is not a hatch.");
            opts.AddAllowedClass(typeof(Hatch), exactMatch: true);
            var res = ed.GetEntity(opts);
            if (res.Status != PromptStatus.OK)
                return;

            double unitsPerMm = DetailDrawer.ResolveUnitsPerMm(_doc.Database, _settings, Collect(endEdit: false));
            using var tr = _doc.Database.TransactionManager.StartOpenCloseTransaction();
            var h = (Hatch)tr.GetObject(res.ObjectId, OpenMode.ForRead);
            target.Pattern = h.PatternName.ToUpperInvariant();
            target.Scale = h.PatternType == HatchPatternType.UserDefined || target.Pattern == "SOLID"
                ? 1
                : Math.Round(h.PatternScale / ((double)_pickScale.Value * unitsPerMm), 4);
            target.Angle = Math.Round(h.PatternAngle * 180 / Math.PI, 2);
            target.Layer = h.Layer;
            target.Color = ColorText(h.Color);
            target.BackgroundColor = h.BackgroundColor is { IsNone: false } bg ? ColorText(bg) : "";
            tr.Commit();
        }

        if (!_patterns.Contains(target.Pattern)) _patterns.Add(target.Pattern);
        _ruleRows.ResetBindings();
        _specialRows.ResetBindings();
        UpdateTest();
    }

    // ------------------------------------------------------------------ grid plumbing ------

    private void WireGrid(DataGridView grid, bool allowByLayer)
    {
        grid.DataError += (_, e) => e.ThrowException = false;

        // Autocomplete from the drawing for columns tagged with a name list.
        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox tb) return;
            if (grid.CurrentCell?.OwningColumn.Tag is AutoCompleteStringCollection source)
            {
                tb.AutoCompleteCustomSource = source;
                tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
                tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            }
            else
            {
                tb.AutoCompleteMode = AutoCompleteMode.None;
            }
        };

        // Colour swatches.
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Tag as string != "color" || e.Graphics is null) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            var text = e.FormattedValue as string ?? "";
            var swatch = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 4, 14, e.CellBounds.Height - 9);
            if (Swatch(text) is { } c)
            {
                using var b = new SolidBrush(c);
                e.Graphics.FillRectangle(b, swatch);
                e.Graphics.DrawRectangle(Pens.Gray, swatch);
            }
            TextRenderer.DrawText(e.Graphics, text, e.CellStyle?.Font ?? Font, new Rectangle(swatch.Right + 4, e.CellBounds.Y, e.CellBounds.Width - 24, e.CellBounds.Height),
                e.CellStyle?.ForeColor ?? Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            e.Handled = true;
        };

        // Double-click a colour cell: AutoCAD's own colour dialog.
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Tag as string != "color") return;
            var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (cell.ReadOnly || grid.Rows[e.RowIndex].IsNewRow) return;
            var dlg = new Autodesk.AutoCAD.Windows.ColorDialog { IncludeByBlockByLayer = allowByLayer };
            if (ColorSpec.TryParse(cell.Value as string, out var current))
                dlg.Color = ToAcColor(current);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                grid.EndEdit();
                cell.Value = ColorText(dlg.Color);
                grid.InvalidateCell(cell);
            }
        };

        grid.CellEndEdit += (_, _) => UpdateTest();
    }

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersWidth = 28,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
    };

    private static DataGridViewTextBoxColumn ColorColumn(string property, string header) =>
        new() { DataPropertyName = property, HeaderText = header, FillWeight = 60, Tag = "color", ToolTipText = "ACI number, R,G,B, BYLAYER or BYBLOCK. Double-click to pick." };

    private static readonly double[] LineWeightChoices =
        { -1, 0, 0.05, 0.09, 0.13, 0.15, 0.18, 0.2, 0.25, 0.3, 0.35, 0.4, 0.5, 0.53, 0.6, 0.7, 0.8, 0.9, 1.0, 1.06, 1.2, 1.4, 1.58, 2.0, 2.11 };

    private static DataGridViewComboBoxColumn LineWeightColumn()
    {
        var col = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(LayerStyle.LineWeightMm), HeaderText = "Lineweight", FillWeight = 60,
            ValueMember = "Value", DisplayMember = "Text", FlatStyle = FlatStyle.Flat, ValueType = typeof(double),
        };
        col.DataSource = LineWeightChoices
            .Select(v => new { Value = v, Text = v < 0 ? "Default" : v.ToString("0.00", CultureInfo.InvariantCulture) + " mm" })
            .ToList();
        return col;
    }

    private static double SnapLineWeight(double mm) =>
        mm < 0 ? -1 : LineWeightChoices.Where(v => v >= 0).MinBy(v => Math.Abs(v - mm));

    // ------------------------------------------------------------------ drawing data -------

    private void ReadDrawingNames()
    {
        foreach (var p in MaterialLibrary.KnownPatterns.Where(p => p != MaterialLibrary.Auto))
            _patterns.Add(p);
        _drawingLayers.AddRange(ReadSymbolNames(db => db.LayerTableId).ToArray());
        _drawingLinetypes.AddRange(ReadSymbolNames(db => db.LinetypeTableId).ToArray());
    }

    private List<string> ReadSymbolNames(Func<Database, ObjectId> table)
    {
        var names = new List<string>();
        if (_doc is null) return names;
        using var tr = _doc.Database.TransactionManager.StartOpenCloseTransaction();
        var t = (SymbolTable)tr.GetObject(table(_doc.Database), OpenMode.ForRead);
        foreach (ObjectId id in t)
        {
            var r = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!r.IsDependent) names.Add(r.Name); // skip xref layers/styles
        }
        tr.Commit();
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // ------------------------------------------------------------------ helpers ------------

    private static Color? Swatch(string text)
    {
        if (!ColorSpec.TryParse(text, out var c)) return null;
        return c.Kind switch
        {
            ColorKind.Index => AcColor.FromColorIndex(AcColorMethod.ByAci, c.Index).ColorValue,
            ColorKind.Rgb => Color.FromArgb(c.R, c.G, c.B),
            _ => null,
        };
    }

    private static AcColor ToAcColor(ColorSpec c) => c.Kind switch
    {
        ColorKind.ByBlock => AcColor.FromColorIndex(AcColorMethod.ByBlock, 0),
        ColorKind.Index => AcColor.FromColorIndex(AcColorMethod.ByAci, c.Index),
        ColorKind.Rgb => AcColor.FromRgb(c.R, c.G, c.B),
        _ => AcColor.FromColorIndex(AcColorMethod.ByLayer, 256),
    };

    internal static string ColorText(AcColor c) => c.ColorMethod switch
    {
        AcColorMethod.ByLayer => "BYLAYER",
        AcColorMethod.ByBlock => "BYBLOCK",
        AcColorMethod.ByAci => c.ColorIndex.ToString(CultureInfo.InvariantCulture),
        _ => $"{c.Red},{c.Green},{c.Blue}",
    };

    private static void TrySetFolder(FileDialog dlg, string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        }
        catch (ArgumentException) { }
    }

    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "standard" : s;
    }

    private Control FacePanel()
    {
        var f = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        f.Controls.AddRange(new Control[] { _topFace, _bottomFace });
        return f;
    }

    private static TabPage Page(string title, Control content)
    {
        var p = new TabPage(title) { Padding = new Padding(6) };
        p.Controls.Add(content);
        return p;
    }

    private static Control Stack(Control top, Control fill)
    {
        top.Dock = DockStyle.Top;
        fill.Dock = DockStyle.Fill;
        var p = new Panel { Dock = DockStyle.Fill };
        p.Controls.Add(fill);
        p.Controls.Add(top);
        return p;
    }

    private static Label Note(string text) => new()
    {
        Text = text, AutoSize = false, Height = 48, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 0, 0, 6),
    };

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

    private static decimal Clamp(NumericUpDown n, double v) =>
        Math.Min(n.Maximum, Math.Max(n.Minimum, double.IsFinite(v) ? (decimal)v : n.Minimum));
}
