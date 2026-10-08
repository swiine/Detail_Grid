using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>Dialog for entering a build-up, managing presets and choosing drawing settings.</summary>
internal sealed class BuildupForm : Form
{
    private readonly PresetFile _file;
    private readonly PresetStore _store;
    private readonly BindingList<PavementLayer> _layers = new();

    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _presets = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _quick = new()
    {
        Dock = DockStyle.Fill, Multiline = true, Height = 48, ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "Type or paste the build-up as written, e.g. 60mm THICK PAVERS, 30mm THICK MORTAR, 2 x 150mm THICK LAYERS OF DGB20 ROAD BASE, SUBGRADE COMPACTED TO 98% STANDARD MDD",
    };
    private readonly DataGridView _grid = new();
    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private bool _loading;
    private readonly Label _total = new() { AutoSize = true, Font = new System.Drawing.Font(SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) };

    private readonly NumericUpDown _width = Num(100, 20000, 1000, 0, 100);
    private readonly NumericUpDown _scale = Num(1, 500, 10, 0, 1);
    private readonly NumericUpDown _hatchMult = Num(0.01m, 100, 1, 2, 0.1m);
    private readonly NumericUpDown _subgradeDepth = Num(0, 5000, 150, 0, 25);
    private readonly ComboBox _units = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox _subgradeText = new() { Width = 140 };
    private readonly Label _standardLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    private readonly CheckBox _block = new() { Text = "Create as block", AutoSize = true };
    private readonly CheckBox _dims = new() { Text = "Show dimensions", AutoSize = true };
    private readonly CheckBox _breaks = new() { Text = "Break lines", AutoSize = true };
    private readonly CheckBox _title = new() { Text = "Title", AutoSize = true };
    private readonly CheckBox _subgrade = new() { Text = "Show subgrade", AutoSize = true };

    public Buildup Result { get; private set; } = new();
    public DetailSettings ResultSettings { get; private set; } = new();
    public CadStandard ResultStandard => _standard;

    private readonly Document? _doc;
    private CadStandard _standard = CadStandard.CreateDefault();
    private string _standardPath = "";

    private readonly bool _editing;
    private Button _okButton = null!;

    /// <param name="editing">An existing detail to edit (PAVEEDIT); null to draw a new one.</param>
    public BuildupForm(PresetFile file, PresetStore store, Document? doc, DetailRecord? editing = null)
    {
        _file = file;
        _store = store;
        _doc = doc;
        _editing = editing is not null;

        Text = "Pavement Build-up Detail";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(980, 680);
        Size = new System.Drawing.Size(1180, 760);
        Font = SystemFonts.MessageBoxFont ?? Font;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeader());
        root.Controls.Add(BuildQuickEntry());
        root.Controls.Add(BuildLayersPanel());
        root.Controls.Add(BuildSettings());
        root.Controls.Add(BuildButtons());
        Controls.Add(root);

        _units.Items.AddRange(DetailLayout.SettingsUnitChoices);
        RefreshPresetList(null);
        var settings = editing?.Settings ?? _file.Settings;
        LoadSettings(settings);
        LoadStandard(settings.StandardPath);

        if (editing is not null)
        {
            LoadBuildup(editing.Buildup);
            Text = "Edit Pavement Detail — " + editing.Buildup.Name;
            _okButton.Text = "Update detail";
            // The detail stays a block / group, and keeps the size it was drawn at.
            _block.Enabled = false;
            _units.Enabled = false;
        }
        else if (_file.Presets.Count > 0)
            _presets.SelectedIndex = 0;
        else
            LoadBuildup(new Buildup());

        WirePreviewUpdates();
        RefreshPreview();
    }

    private void WirePreviewUpdates()
    {
        _layers.ListChanged += (_, _) => RefreshPreview();
        _grid.CellValueChanged += (_, _) => RefreshPreview();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            // Combo cells only commit on leave; commit straight away so the preview follows.
            if (_grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _name.TextChanged += (_, _) => RefreshPreview();
        _subgradeText.TextChanged += (_, _) => RefreshPreview();
        foreach (var n in new[] { _width, _scale, _hatchMult, _subgradeDepth })
            n.ValueChanged += (_, _) => RefreshPreview();
        foreach (var c in new[] { _block, _dims, _breaks, _title, _subgrade })
            c.CheckedChanged += (_, _) => RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_loading) return;
        _preview.UpdatePreview(CurrentBuildup(endEdit: false), CurrentSettings(), _standard);
    }

    // ---------------------------------------------------------------- layout -------

    private Control BuildHeader()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var save = new Button { Text = "Save preset", AutoSize = true };
        var delete = new Button { Text = "Delete", AutoSize = true };
        save.Click += (_, _) => SavePreset();
        delete.Click += (_, _) => DeletePreset();
        _presets.SelectedIndexChanged += (_, _) =>
        {
            if (_presets.SelectedItem is string n && PresetStore.Find(_file, n) is { } p)
                LoadBuildup(p);
        };

        t.Controls.Add(Lbl("Name:"), 0, 0);
        t.Controls.Add(_name, 1, 0);
        t.Controls.Add(Lbl("Preset:"), 2, 0);
        t.Controls.Add(_presets, 3, 0);
        t.Controls.Add(save, 4, 0);
        t.Controls.Add(delete, 5, 0);
        return t;
    }

    private Control BuildQuickEntry()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var parse = new Button { Text = "Fill table", AutoSize = true };
        var fromNote = new Button { Text = "From drawing note…", AutoSize = true, Enabled = _doc is not null };
        fromNote.Click += (_, _) => PickNote();
        parse.Click += (_, _) => ParseQuick();
        _quick.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { ParseQuick(); e.SuppressKeyPress = true; }
        };
        t.Controls.Add(Lbl("Quick entry (mm, top to bottom):"), 0, 0);
        t.Controls.Add(_quick, 1, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = System.Windows.Forms.FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.Add(parse);
        buttons.Controls.Add(fromNote);
        t.Controls.Add(buttons, 2, 0);

        // Interpret with AI: on/off, settings, what happened, and a way back to the original text.
        _useAi.Checked = _file.Ai.Enabled;
        _useAi.CheckedChanged += (_, _) => { _file.Ai.Enabled = _useAi.Checked; TrySave(); UpdateAiStatus(); };
        var aiSettings = new Button { Text = "AI settings…", AutoSize = true };
        aiSettings.Click += (_, _) =>
        {
            using var form = new AiSettingsForm(_file.Ai, _standard);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                _useAi.Checked = _file.Ai.Enabled;
                TrySave();
            }
            UpdateAiStatus();
        };
        _undoAi.LinkClicked += (_, _) =>
        {
            if (_beforeAi is null) return;
            _quick.Text = _beforeAi;
            _beforeAi = null;
            _undoAi.Visible = false;
            FillTable(_quick.Text);
            _aiStatus.Text = "Back to your original text (read without AI).";
        };
        var ai = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        // Free route: copy prompt + text, open Claude.ai, paste the reply back.
        var askFree = new Button { Text = "Ask AI (free)…", AutoSize = true };
        var pasteReply = new Button { Text = "Paste AI reply", AutoSize = true };
        askFree.Click += (_, _) => AskFreeAi();
        pasteReply.Click += (_, _) => PasteAiReply();
        ai.Controls.AddRange(new Control[] { askFree, pasteReply, _useAi, aiSettings, _aiStatus, _undoAi });
        t.Controls.Add(ai, 1, 1);
        UpdateAiStatus();
        return t;
    }

    private readonly CheckBox _useAi = new() { Text = "Auto AI (paid API)", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    private readonly Label _aiStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 9, 3, 3) };
    private readonly LinkLabel _undoAi = new() { Text = "Undo AI", AutoSize = true, Visible = false, Margin = new Padding(6, 9, 3, 3) };
    private string? _beforeAi;
    private bool _busy;

    private void UpdateAiStatus()
    {
        if (_busy) return;
        _aiStatus.Text = !_useAi.Checked ? ""
            : AiService.IsReady(_file.Ai, out var reason) ? "Text you enter is converted by AI, then shown here for checking."
            : !AiService.HasCredentials ? "No API key set up: use Ask AI (free) instead, or set up a key in AI settings."
            : reason;
    }

    /// <summary>Copies the AI prompt + the build-up text and opens Claude.ai (free plan) to paste it into.</summary>
    private void AskFreeAi()
    {
        if (string.IsNullOrWhiteSpace(_quick.Text))
        {
            MessageBox.Show(this, "Type or paste the build-up into the box first (or use From drawing note…).", Text);
            return;
        }
        Clipboard.SetText(AiPrompt.ForChat(AiPrompt.Load(_standard.AiPromptFile), _quick.Text));
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://claude.ai/new") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { /* no browser: the user can open any AI chat */ }
        _aiStatus.Text = "Copied. In the browser: Ctrl+V and send, copy Claude's reply, then click Paste AI reply.";
    }

    /// <summary>Puts the reply copied from the AI chat into the box (Undo AI goes back) and fills the table.</summary>
    private void PasteAiReply()
    {
        var reply = Clipboard.ContainsText() ? AiPrompt.CleanReply(Clipboard.GetText()) : "";
        if (reply.Length == 0 || reply.Contains("Convert this:", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Copy Claude's reply first (the converted line), then click Paste AI reply.", Text);
            return;
        }
        _beforeAi = _quick.Text;
        _quick.Text = reply;
        _undoAi.Visible = true;
        _aiStatus.Text = "AI reply pasted. Check the table and preview.";
        FillTable(reply);
    }

    private Control BuildLayersPanel()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersWidth = 28;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PavementLayer.Description), HeaderText = "Material / course (label text)", FillWeight = 260,
            DefaultCellStyle = { WrapMode = DataGridViewTriState.True }, // remarks show as extra lines
            ToolTipText = "Label text. Lines after the first (remarks such as REFER TO ...) and anything in (brackets) are label-only.",
        });
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PavementLayer.ThicknessMm), HeaderText = "Thickness (mm)", FillWeight = 70,
            ToolTipText = "Thickness of each layer. With No. of layers = 2, \"150\" means 2 x 150mm.",
            DefaultCellStyle = { Format = "0.#", Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PavementLayer.Lifts), HeaderText = "No. of layers", FillWeight = 45,
            ToolTipText = "2 = \"2 x 150mm ...\": drawn as one course with a line between the layers.",
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        var pattern = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(PavementLayer.HatchPattern), HeaderText = "Hatch", FillWeight = 80, FlatStyle = FlatStyle.Flat,
        };
        pattern.Items.AddRange(MaterialLibrary.KnownPatterns);
        _grid.Columns.Add(pattern);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PavementLayer.HatchScale), HeaderText = "Hatch scale ×", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PavementLayer.HatchAngle), HeaderText = "Angle °", FillWeight = 45, DefaultCellStyle = { DataSourceNullValue = null, NullValue = "" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PavementLayer.ReinforcementText), HeaderText = "Reinforcement (optional)", FillWeight = 150,
            ToolTipText = "Bars in this course: " + ReinforcementParser.Help + ". Face defaults to bottom; \"+ ...\" adds transverse bars.",
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hatch used (standard rule)", ReadOnly = true, FillWeight = 110, Name = "Resolved" });

        _grid.DataSource = _layers;
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            // Bad reinforcement text: keep the user in the cell and say why.
            var ex = e.Exception?.InnerException ?? e.Exception;
            if (ex is FormatException && e.RowIndex >= 0)
            {
                _grid.Rows[e.RowIndex].ErrorText = ex.Message;
                e.Cancel = true;
            }
        };
        _grid.CellEndEdit += (_, e) => { if (e.RowIndex >= 0) _grid.Rows[e.RowIndex].ErrorText = ""; };
        _grid.CellFormatting += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name == "Resolved" && e.RowIndex < _layers.Count)
            {
                var l = _layers[e.RowIndex];
                e.Value = l.ThicknessMm <= 0 ? "line"
                    : MaterialLibrary.RuleFor(l, _standard) is { } rule
                        ? (rule.IsNone ? "none" : rule.Pattern) + (string.IsNullOrEmpty(rule.Name) ? "" : $" ({rule.Name})")
                        : "none";
            }
        };
        _layers.ListChanged += (_, _) => UpdateTotal();
        _layers.AddingNew += (_, e) => e.NewObject = new PavementLayer { Description = "New course", ThicknessMm = 50 };

        var up = new Button { Text = "▲ Up", Width = 80 };
        var down = new Button { Text = "▼ Down", Width = 80 };
        var remove = new Button { Text = "Remove", Width = 80 };
        var clear = new Button { Text = "Clear", Width = 80 };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(+1);
        remove.Click += (_, _) =>
        {
            if (CurrentIndex() is int i) _layers.RemoveAt(i);
        };
        clear.Click += (_, _) => _layers.Clear();

        var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        side.Controls.AddRange(new Control[] { up, down, remove, clear, _total });

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(side);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        Load += (_, _) => split.SplitterDistance = (int)(split.Width * 0.55); // only valid once sized
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_preview);
        return split;
    }

    private Control BuildSettings()
    {
        // Fixed rows (a wrapping panel clipped its second line, hiding the on/off options).
        var box = new GroupBox { Text = "Drawing", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        rows.Controls.Add(Row(Lbl("Scale 1:"), _scale, Lbl("Width (mm):"), _width, Lbl("Drawing units:"), _units, Lbl("Hatch scale ×"), _hatchMult));
        rows.Controls.Add(Row(_subgrade, Lbl("Subgrade text:"), _subgradeText, Lbl("depth (mm):"), _subgradeDepth));
        rows.Controls.Add(Row(_dims, _breaks, _title, _block));
        box.Controls.Add(rows);

        var edit = new Button { Text = "Edit standard…", AutoSize = true };
        var choose = new Button { Text = "Use another…", AutoSize = true };
        edit.Click += (_, _) => EditStandard();
        choose.Click += (_, _) => ChooseStandard();
        var std = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        std.Controls.AddRange(new Control[] { Lbl("CAD standard:"), _standardLabel, edit, choose });
        var stdBox = new GroupBox { Text = "Layers, hatches and text", Dock = DockStyle.Top, AutoSize = true };
        stdBox.Controls.Add(std);

        var both = new Panel { Dock = DockStyle.Top, AutoSize = true };
        both.Controls.Add(box);
        both.Controls.Add(stdBox);
        return both;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top, Margin = Padding.Empty };
        foreach (var c in controls)
            c.Margin = new Padding(3, 6, 12, 3);
        f.Controls.AddRange(controls);
        return f;
    }

    private Control BuildButtons()
    {
        var ok = _okButton = new Button { Text = "Draw detail", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();
        AcceptButton = ok;
        CancelButton = cancel;
        var f = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        f.Controls.Add(cancel);
        f.Controls.Add(ok);
        return f;
    }

    // ---------------------------------------------------------------- behaviour ----

    /// <summary>Fill table: with AI on, the text is first converted by the AI (shown in the box, undoable), then read.</summary>
    private async void ParseQuick()
    {
        if (_busy) return;
        var text = _quick.Text;
        if (_useAi.Checked && !string.IsNullOrWhiteSpace(text))
        {
            if (!AiService.IsReady(_file.Ai, out var reason))
            {
                _aiStatus.Text = reason + " Read without AI.";
            }
            else
            {
                _busy = true;
                UseWaitCursor = true;
                _quick.Enabled = false;
                _aiStatus.Text = "Interpreting with AI…";
                try
                {
                    var converted = await AiService.InterpretAsync(text, _file.Ai, _standard);
                    _beforeAi = text;
                    _quick.Text = converted;
                    text = converted;
                    _aiStatus.Text = "Converted by AI. Check the table and preview.";
                    _undoAi.Visible = true;
                }
                catch (InvalidOperationException ex)
                {
                    _aiStatus.Text = ex.Message + " Read without AI.";
                }
                finally
                {
                    _busy = false;
                    UseWaitCursor = false;
                    _quick.Enabled = true;
                }
            }
        }
        FillTable(text);
    }

    private void FillTable(string text)
    {
        var errors = new List<string>();
        var parsed = BuildupParser.ParseBuildup(text, errors);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _grid.EndEdit();
        _layers.Clear();
        foreach (var l in parsed.Layers)
            _layers.Add(l);
        if (parsed.SubgradeText is not null)
        {
            _subgrade.Checked = true;
            _subgradeText.Text = parsed.SubgradeText;
        }
        if (parsed.Name is not null)
            _name.Text = parsed.Name;
    }

    /// <summary>Starts the dialog from a build-up converted from a note (PAVETEXT).</summary>
    public void Prefill(Buildup b) => LoadBuildup(b);

    /// <summary>Reads a spec note from the drawing into the quick-entry box and fills the table.</summary>
    private void PickNote()
    {
        if (_doc is null) return;
        string? note;
        using (_doc.Editor.StartUserInteraction(Handle))
            note = NoteReader.Pick(_doc.Editor);
        if (note is null) return;
        _quick.Text = note;
        ParseQuick();
    }

    private void LoadBuildup(Buildup b)
    {
        _grid.EndEdit();
        _loading = true;
        _name.Text = b.Name;
        _layers.RaiseListChangedEvents = false;
        _layers.Clear();
        foreach (var l in b.Layers)
        {
            EnsurePatternListed(l.HatchPattern);
            _layers.Add(l.Clone());
        }
        _layers.RaiseListChangedEvents = true;
        _layers.ResetBindings();
        _subgrade.Checked = b.ShowSubgrade;
        _subgradeText.Text = b.SubgradeText;
        _quick.Text = BuildupParser.Format(b);
        _loading = false;
        RefreshPreview();
    }

    /// <summary>A combo cell throws on values not in its list, so add custom patterns from presets.</summary>
    private void EnsurePatternListed(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return;
        var col = _grid.Columns.OfType<DataGridViewComboBoxColumn>().First();
        if (!col.Items.Contains(pattern))
            col.Items.Add(pattern);
    }

    private Buildup CurrentBuildup(bool endEdit = true)
    {
        if (endEdit) _grid.EndEdit();
        return new Buildup
        {
            Name = string.IsNullOrWhiteSpace(_name.Text) ? "PAVEMENT BUILD-UP" : _name.Text.Trim(),
            Layers = _layers.Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.ThicknessMm > 0).Select(l => l.Clone()).ToList(),
            ShowSubgrade = _subgrade.Checked,
            SubgradeText = _subgradeText.Text,
        };
    }

    private void LoadSettings(DetailSettings s)
    {
        _loading = true;
        _width.Value = Clamp(_width, s.WidthMm);
        _scale.Value = Clamp(_scale, s.ScaleDenominator);
        _hatchMult.Value = Clamp(_hatchMult, s.HatchScaleMultiplier);
        _subgradeDepth.Value = Clamp(_subgradeDepth, s.SubgradeDepthMm);
        var units = (s.UnitsOverride ?? "").ToUpperInvariant();
        _units.SelectedItem = _units.Items.Contains(units) ? units : DetailLayout.UseStandardUnits;
        _block.Checked = s.CreateBlock;
        _dims.Checked = s.ShowDimensions;
        _breaks.Checked = s.ShowBreakLines;
        _title.Checked = s.ShowTitle;
        _loading = false;
    }

    private DetailSettings CurrentSettings() => new()
    {
        WidthMm = (double)_width.Value,
        ScaleDenominator = (double)_scale.Value,
        HatchScaleMultiplier = (double)_hatchMult.Value,
        SubgradeDepthMm = (double)_subgradeDepth.Value,
        UnitsOverride = _units.SelectedItem as string ?? DetailLayout.UseStandardUnits,
        StandardPath = _standardPath,
        CreateBlock = _block.Checked,
        ShowDimensions = _dims.Checked,
        ShowBreakLines = _breaks.Checked,
        ShowTitle = _title.Checked,
    };

    private void SavePreset()
    {
        var b = CurrentBuildup();
        if (b.Layers.Count == 0)
        {
            MessageBox.Show(this, "Add at least one layer before saving.", Text);
            return;
        }
        PresetStore.Upsert(_file, b);
        _file.Settings = CurrentSettings();
        TrySave();
        RefreshPresetList(b.Name);
    }

    private void DeletePreset()
    {
        if (_presets.SelectedItem is not string n)
            return;
        if (MessageBox.Show(this, $"Delete preset \"{n}\"?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;
        _file.Presets.RemoveAll(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase));
        TrySave();
        RefreshPresetList(null);
    }

    private void RefreshPresetList(string? select)
    {
        _presets.Items.Clear();
        foreach (var p in _file.Presets)
            _presets.Items.Add(p.Name);
        if (select is not null)
        {
            int i = _presets.Items.IndexOf(_file.Presets.First(p => string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase)).Name);
            if (i >= 0) _presets.SelectedIndex = i;
        }
    }

    private void Accept()
    {
        var b = CurrentBuildup();
        var s = CurrentSettings();
        try
        {
            DetailLayout.Build(b, s, _standard, 1.0); // validate before closing
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_editing)
        {
            _file.Settings = s; // remember settings for next time / PAVEQUICK
            TrySave();
        }
        Result = b;
        ResultSettings = s;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void TrySave()
    {
        try { _store.Save(_file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save presets: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------------- CAD standard ---

    /// <summary>Loads the standard a settings path points at; on failure keeps the built-in one and says so.</summary>
    private void LoadStandard(string path)
    {
        _standardPath = path ?? "";
        try
        {
            _standard = StandardStore.Load(StandardStore.PathFor(CurrentSettings()));
            _standardLabel.ForeColor = SystemColors.ControlText;
            _standardLabel.Text = $"{_standard.Name}  —  {StandardStore.PathFor(CurrentSettings())}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _standard = CadStandard.CreateDefault();
            _standardLabel.ForeColor = Color.Firebrick;
            _standardLabel.Text = "Could not load standard, using built-in: " + ex.Message;
        }
        _grid.Invalidate();
        RefreshPreview();
    }

    private void EditStandard()
    {
        using var form = new StandardForm(_standard, StandardStore.PathFor(CurrentSettings()), CurrentSettings(), _doc);
        if (form.ShowDialog(this) != DialogResult.OK)
            return;
        // "Save as" in the editor may have moved it; the personal default path is stored as blank.
        _standardPath = string.Equals(form.ResultPath, StandardStore.DefaultPath, StringComparison.OrdinalIgnoreCase) ? "" : form.ResultPath;
        LoadStandard(_standardPath);
        _file.Settings = CurrentSettings();
        TrySave();
    }

    private void ChooseStandard()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "CAD standard (*.json)|*.json",
            Title = "Use CAD standard (e.g. the company one on a shared drive)",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        LoadStandard(dlg.FileName);
        _file.Settings = CurrentSettings();
        TrySave();
    }

    private void UpdateTotal()
    {
        double total = _layers.Sum(l => l.TotalMm);
        _total.Text = $"Total: {total.ToString("0.#", CultureInfo.CurrentCulture)} mm";
        _grid.InvalidateColumn(_grid.Columns["Resolved"]!.Index);
    }

    private int? CurrentIndex()
    {
        var row = _grid.CurrentRow;
        return row is null || row.IsNewRow || row.Index >= _layers.Count ? null : row.Index;
    }

    private void MoveSelected(int delta)
    {
        if (CurrentIndex() is not int i) return;
        int j = i + delta;
        if (j < 0 || j >= _layers.Count) return;
        _grid.EndEdit();
        var item = _layers[i];
        _layers.RemoveAt(i);
        _layers.Insert(j, item);
        _grid.CurrentCell = _grid.Rows[j].Cells[0];
    }

    // ---------------------------------------------------------------- helpers ------

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

    private static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals, decimal step) =>
        new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Increment = step, Width = 75 };

    private static decimal Clamp(NumericUpDown n, double v) =>
        Math.Min(n.Maximum, Math.Max(n.Minimum, double.IsFinite(v) ? (decimal)v : n.Value));
}
