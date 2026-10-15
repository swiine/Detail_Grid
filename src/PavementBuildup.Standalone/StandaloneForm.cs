using PavementBuildup.Core;
using PavementBuildup.Export;

namespace PavementBuildup;

internal sealed class StandaloneForm : Form
{
    private readonly TextBox _text = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true,
        PlaceholderText = "e.g.  60mm pavers, 30mm sand, 150mm road base, subgrade\r\n\r\n" +
                          "Or paste anything: a list, a table from Excel, a spec paragraph, a note from a drawing.",
    };
    private readonly Label _summary = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 2) };
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Type or paste a build-up. It's read as you type." };
    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly ToolTip _tips = new();

    private readonly ComboBox _scale = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 80 };
    private readonly NumericUpDown _width = Num(100, 20000, 1000, 100);
    private readonly ComboBox _units = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _dims = new() { Text = "Dimensions", AutoSize = true, Checked = true };
    private readonly CheckBox _breaks = new() { Text = "Break lines", AutoSize = true, Checked = true };
    private readonly CheckBox _title = new() { Text = "Title", AutoSize = true, Checked = true };
    private readonly CheckBox _subgrade = new() { Text = "Subgrade", AutoSize = true, Checked = true };
    private readonly ComboBox _standards = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly Label _folderLabel = new() { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(300), 0) };
    private readonly StandaloneSettings _settings = StandaloneSettings.Load();
    private bool _loadingStandards;

    private CadStandard _standard = CadStandard.CreateDefault();
    private readonly DesignWizard _design = new();
    private Buildup? _buildup;
    private readonly Button _save = new() { Text = "Save DXF…", AutoSize = true };
    private readonly Button _savePdf = new() { Text = "Save PDF…", AutoSize = true };
    private readonly ComboBox _paper = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    private readonly Panel _updateBar = new() { Dock = DockStyle.Top, Visible = false, Padding = new Padding(18, 6, 18, 6) };

    public StandaloneForm()
    {
        Text = "TTW Pavement";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Brand.Px(1180), Brand.Px(760));
        MinimumSize = new Size(Brand.Px(860), Brand.Px(560));
        Brand.Decorate(this, "TTW Pavement", "Draw a build-up or design one  ·  no Civil 3D needed", _status);

        _units.Items.AddRange(new object[] { "Metres", "Millimetres" });
        _units.SelectedIndex = 0;
        _scale.Items.AddRange(new object[] { "1:1", "1:2", "1:5", "1:10", "1:20", "1:25", "1:50", "1:100" });
        _scale.Text = "1:" + CadStandard.Number(_settings.Scale > 0 ? _settings.Scale : 10);
        FillStandards();
        _paper.Items.AddRange(new object[] { "A4", "A3" });
        _paper.SelectedItem = _settings.Paper == "A3" ? "A3" : "A4";
        _paper.SelectedIndexChanged += (_, _) => { _settings.Paper = (string)_paper.SelectedItem!; _settings.Save(); };

        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Brand.Bg };
        Load += (_, _) => { split.SplitterDistance = (int)(split.Width * 0.42); FitSummary(); };
        split.SplitterMoved += (_, _) => FitSummary();
        split.Panel1.Controls.Add(BuildLeft());
        split.Panel2.Controls.Add(BuildRight());

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        var detailTab = new TabPage("Detail") { Padding = new Padding(4, 8, 4, 4) };
        detailTab.Controls.Add(split);
        var designTab = new TabPage("Design") { Padding = new Padding(4, 4, 4, 4) };
        var design = _design;
        design.CurrentStandard = () => _standard;
        design.UseBuildup += text =>
        {
            _text.Text = text;
            tabs.SelectedTab = detailTab;
            _status.ForeColor = Brand.Success;
            _status.Text = "Design sent to the Detail tab. Check the picture, then Save DXF.";
        };
        designTab.Controls.Add(design);
        tabs.TabPages.Add(detailTab);
        tabs.TabPages.Add(designTab);
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 18, 8) };
        root.Controls.Add(tabs);
        Controls.Add(root);
        if (StandaloneTraining.On)
        {
            Text = "TTW Pavement - Training";
            Brand.SetHeader(this, "TTW Pavement - TRAINING", "Practice mode  ·  files go to a temporary folder, nothing is counted");
            var training = new TrainingPanel();
            training.FillBuildup += text => { tabs.SelectedTab = detailTab; _text.Text = text.Replace("\n", Environment.NewLine); };
            training.OpenDesign += file => { tabs.SelectedTab = designTab; _design.Open(file); };
            root.Controls.Add(training);
            tabs.BringToFront();
        }
        Controls.Add(_updateBar);
        _updateBar.BringToFront();
        root.BringToFront();
        Shown += async (_, _) => await CheckForUpdate();

        _text.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        _timer.Tick += (_, _) => { _timer.Stop(); ReadText(); };
        _width.ValueChanged += (_, _) => Redraw();
        _scale.TextChanged += (_, _) => Redraw();
        _scale.Leave += (_, _) => _scale.Text = "1:" + CadStandard.Number(ScaleValue());
        _standards.SelectedIndexChanged += (_, _) => { if (!_loadingStandards) UseSelectedStandard(); };
        Resize += (_, _) => FitSummary();
        _units.SelectedIndexChanged += (_, _) => Redraw();
        foreach (var c in new[] { _dims, _breaks, _title, _subgrade }) c.CheckedChanged += (_, _) => Redraw();
        FormClosed += (_, _) => { _settings.Scale = ScaleValue(); _settings.Save(); _timer.Dispose(); _tips.Dispose(); };
        ShowSummary(null);
    }

    private Control BuildLeft()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(Brand.Step("① Paste or type the build-up (any wording)"));
        _text.Font = new Font("Consolas", 10f);
        t.Controls.Add(_text);
        t.Controls.Add(_summary);

        var paste = new Button { Text = "Paste", AutoSize = true };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) _text.Text = Clipboard.GetText(); };
        var ai = new Button { Text = "Ask AI (free)…", AutoSize = true };
        ai.Click += (_, _) => AskAi();
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) => _text.Clear();
        var batch = new Button { Text = "Batch from Excel…", AutoSize = true };
        batch.Click += (_, _) =>
        {
            using var form = new BatchForm(Settings, () => _standard, _design.JobNumber, _design.ProjectName);
            form.ShowDialog(this);
        };
        _tips.SetToolTip(batch, "A list of pavement types in Excel (one per row) turned into all the details: one DXF, or a PDF with a sheet each.");
        var problem = new Button { Text = "Report a problem…", AutoSize = true };
        problem.Click += (_, _) => { using var form = new ProblemForm("TTW Pavement standalone", Program.Screenshot()); form.ShowDialog(this); };
        var saved = new Button { Text = "Time saved…", AutoSize = true };
        saved.Click += (_, _) => { StandaloneTraining.Done("timesaved"); using var form = new UsageForm(); form.ShowDialog(this); };
        _tips.SetToolTip(saved, "How much time the tools have saved you (and the team): uses and hours by tool, month and person.");
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { paste, ai, clear, batch, saved, problem });
        t.Controls.Add(buttons);

        var options = new GroupBox { Text = "Drawing", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3, 10, 3, 3) };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        var folder = new Button { Text = "Standards folder…", AutoSize = true };
        folder.Click += (_, _) => ChooseFolder();
        rows.Controls.Add(Row(Lbl("Scale"), _scale, Lbl("Width (mm)"), _width, Lbl("Units"), _units));
        rows.Controls.Add(Row(_dims, _breaks, _title, _subgrade, Lbl("PDF paper"), _paper));
        rows.Controls.Add(Row(Lbl("CAD standard"), _standards));
        rows.Controls.Add(Row(folder, _folderLabel));
        options.Controls.Add(rows);
        t.Controls.Add(options);
        return t;
    }

    private Control BuildRight()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10, 0, 0, 0) };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(Brand.Step("② Check the picture"));
        t.Controls.Add(_preview);

        _save.Tag = Brand.PrimaryTag;
        _save.Font = Brand.Sized(11f, FontStyle.Bold);
        _save.MinimumSize = new Size(0, Brand.Px(40));
        _save.Click += (_, _) => SaveDxf();
        _savePdf.MinimumSize = new Size(0, Brand.Px(40));
        _savePdf.Click += (_, _) => SavePdf();
        _tips.SetToolTip(_savePdf, "The detail as a PDF sheet (to scale when it fits the paper) for quick emails without CAD.");
        var copy = new Button { Text = "Copy for TTWPVMT", AutoSize = true, MinimumSize = new Size(0, Brand.Px(40)) };
        copy.Click += (_, _) => CopyForPlugin();
        _tips.SetToolTip(copy, "Copies the tidy build-up. In Civil 3D: TTWPVMT > New pavement detail, paste into box ①.");
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(Brand.Step("③"));
        bar.Controls.Add(_save);
        bar.Controls.Add(_savePdf);
        bar.Controls.Add(copy);
        _savePdf.Margin = copy.Margin = new Padding(10, 3, 3, 3);
        t.Controls.Add(bar);
        return t;
    }

    private void ReadText()
    {
        var errors = new List<string>();
        var read = SmartReader.Read(_text.Text, errors);
        var parsed = read.Parsed;
        ShowSummary(parsed);
        var all = errors.Select(e => "Couldn't read: " + e).Concat(read.Notes).ToList();
        _status.ForeColor = errors.Count > 0 ? Brand.Failure : Brand.Muted;
        _status.Text = all.Count == 0
            ? (parsed.Layers.Count > 0 ? "Read it as written." : "Type or paste a build-up. It's read as you type.")
            : "Read: " + string.Join("  ·  ", all.Take(3)) + (all.Count > 3 ? $"  (+{all.Count - 3} more, hover)" : "");
        _tips.SetToolTip(_status, string.Join(Environment.NewLine, all));
        _buildup = parsed.Layers.Count == 0 ? null : parsed.ToBuildup(parsed.Name ?? "PAVEMENT BUILD-UP");
        if (_buildup is not null) StandaloneTraining.Done("buildup");
        Redraw();
    }

    private void ShowSummary(BuildupParseResult? parsed)
    {
        if (parsed is null || parsed.Layers.Count == 0)
        {
            _summary.ForeColor = parsed is null || string.IsNullOrWhiteSpace(_text.Text) ? Brand.Text : Brand.Failure;
            _summary.Font = Brand.Body();
            _summary.Text = parsed is null || string.IsNullOrWhiteSpace(_text.Text)
                ? "Write each layer with its thickness, top to bottom, e.g. 60mm pavers, 30mm sand, 150mm road base, subgrade."
                : "Can't find any layers yet. Give each layer a thickness (e.g. 150mm road base), or try Ask AI (free).";
            return;
        }
        double total = parsed.Layers.Sum(l => l.TotalMm);
        var parts = new List<string> { $"✔ {parsed.Layers.Count} layer{(parsed.Layers.Count == 1 ? "" : "s")}, {total:0.#} mm deep" };
        if (parsed.SubgradeText is { } sg) parts.Add("sits on: " + BuildupParser.Core(sg));
        if (parsed.Name is { } n) parts.Add("title: " + n);
        _summary.ForeColor = Brand.Success;
        _summary.Font = Brand.Body(FontStyle.Bold);
        _summary.Text = string.Join("  ·  ", parts);
    }

    private double ScaleValue()
    {
        var text = _scale.Text.Replace(" ", "");
        int colon = text.IndexOf(':');
        if (colon >= 0) text = text[(colon + 1)..];
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 1 and <= 1000 ? v : 10;
    }

    private DetailSettings Settings() => new()
    {
        ScaleDenominator = ScaleValue(),
        WidthMm = (double)_width.Value,
        UnitsOverride = _units.SelectedIndex == 1 ? "MM" : "M",
        ShowDimensions = _dims.Checked,
        ShowBreakLines = _breaks.Checked,
        ShowTitle = _title.Checked,
        CreateBlock = false,
    };

    private void Redraw()
    {
        if (Math.Abs(ScaleValue() - 20) < 1e-9 && _buildup is not null) StandaloneTraining.Done("scale");
        _save.Enabled = _savePdf.Enabled = _buildup is not null;
        if (_buildup is null)
        {
            _preview.UpdatePreview(new Buildup { Layers = new() }, Settings(), _standard);
            return;
        }
        _buildup.ShowSubgrade = _subgrade.Checked;
        _preview.UpdatePreview(_buildup, Settings(), _standard);
    }

    private void SaveDxf()
    {
        if (_buildup is null) return;
        var folder = AppFolders.ForDesign(_design.JobNumber, _buildup.Name);
        using var dlg = new SaveFileDialog
        { InitialDirectory = folder,
            Filter = "DXF drawing (*.dxf)|*.dxf",
            Title = "Save the pavement detail",
            FileName = SafeFileName(_buildup.Name) + ".dxf",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.Tidy(folder);
            return;
        }
        try
        {
            using (var fs = File.Create(dlg.FileName))
                DxfExport.Write(_buildup, Settings(), _standard, fs);
            UsageLog.Record(UsageTools.DxfSaved, "Standalone");
            StandaloneTraining.Done("dxf");
            _status.ForeColor = Brand.Success;
            _status.Text = $"Saved {Path.GetFileName(dlg.FileName)}. In CAD: INSERT it (or drag the file into the drawing).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, "Could not save the DXF: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SavePdf()
    {
        if (_buildup is null) return;
        var folder = AppFolders.ForDesign(_design.JobNumber, _buildup.Name);
        using var dlg = new SaveFileDialog
        { InitialDirectory = folder, Filter = "PDF (*.pdf)|*.pdf", Title = "Save the pavement detail as a PDF", FileName = SafeFileName(_buildup.Name) + ".pdf" };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.Tidy(folder);
            return;
        }
        try
        {
            using (var fs = File.Create(dlg.FileName))
                PdfExport.Write(_buildup, Settings(), _standard,
                    new PdfSheet { Project = _design.ProjectName, JobNumber = _design.JobNumber, Paper = (string)_paper.SelectedItem! }, fs);
            UsageLog.Record(UsageTools.PdfSaved, "Standalone");
            StandaloneTraining.Done("pdf");
            _status.ForeColor = Brand.Success;
            _status.Text = $"Saved {Path.GetFileName(dlg.FileName)}.";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not save the PDF: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckForUpdate()
    {
        var current = typeof(StandaloneForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var latest = await VersionCheck.CheckAsync(new[] { AppContext.BaseDirectory }, current, TimeSpan.FromSeconds(5));
        if (latest is null || IsDisposed) return;
        _updateBar.BackColor = Color.FromArgb(253, 243, 214);
        var text = new Label
        {
            AutoSize = true, Margin = new Padding(0, 7, 12, 0), Font = Brand.Body(FontStyle.Bold), ForeColor = Brand.Text,
            Text = $"A newer version of TTW Pavement ({latest.Version}) is on the network. You have {current}."
                   + (latest.Notes.Length > 0 ? "  What's new: " + latest.Notes : ""),
        };
        var open = new Button { Text = "Open the release folder", AutoSize = true, Tag = Brand.PrimaryTag };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(latest.Folder) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { _status.Text = "Could not open " + latest.Folder + ": " + ex.Message; }
        };
        var dismiss = new Button { Text = "Later", AutoSize = true };
        dismiss.Click += (_, _) => _updateBar.Visible = false;
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        row.Controls.AddRange(new Control[] { text, open, dismiss });
        _updateBar.Controls.Add(row);
        _updateBar.Height = Brand.Px(46);
        Brand.Apply(this, _updateBar);
        _updateBar.Visible = true;
    }

    private void CopyForPlugin()
    {
        if (_buildup is null) return;
        Clipboard.SetText(BuildupParser.Format(_buildup));
        _status.ForeColor = Brand.Success;
        _status.Text = "Copied. In Civil 3D: TTWPVMT > New pavement detail, paste into box ①.";
    }

    private void AskAi()
    {
        if (string.IsNullOrWhiteSpace(_text.Text))
        {
            MessageBox.Show(this, "Type or paste the build-up into the box first.", Text);
            return;
        }
        var message = AiPrompt.ForChat(AiPrompt.Load(_standard.AiPromptFile), _text.Text);
        Clipboard.SetText(message);
        var url = "https://claude.ai/new" + (message.Length < 6000 ? "?q=" + Uri.EscapeDataString(message) : "");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) {  }
        _status.ForeColor = Brand.Muted;
        _status.Text = "Claude.ai opened (Ctrl+V if the box is empty). Send it, copy the reply, and paste it into box ①.";
    }

    private sealed record StandardChoice(string Label, string? Path)
    {
        public override string ToString() => Label;
    }

    private void FillStandards()
    {
        _loadingStandards = true;
        _standards.Items.Clear();
        _standards.Items.Add(new StandardChoice("Built-in TTW standard", null));
        if (File.Exists(StandardStore.DefaultPath))
            _standards.Items.Add(new StandardChoice("My saved standard (plugin)", StandardStore.DefaultPath));
        var folder = string.IsNullOrWhiteSpace(_settings.StandardsFolder) ? AppFolders.Standards : _settings.StandardsFolder;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                _standards.Items.Add(new StandardChoice(Path.GetFileNameWithoutExtension(file)
                    + (StandardStore.SamePath(file, StandardStore.CompanyDefault) ? " (default)" : ""), file));
            _folderLabel.Text = "Folder: " + folder;
        }
        else
        {
            _folderLabel.Text = string.IsNullOrWhiteSpace(folder)
                ? "Pick a standards folder to list the company standards (.json)."
                : "Standards folder not found: " + folder;
        }

        if (StandardStore.CompanyDefault is { } company && !_standards.Items.Cast<StandardChoice>().Any(c => StandardStore.SamePath(c.Path, company)))
            _standards.Items.Insert(1, new StandardChoice("TTW (default)", company));
        var wanted = _standards.Items.Cast<StandardChoice>()
            .FirstOrDefault(c => StandardStore.SamePath(c.Path, _settings.StandardFile))
            ?? _standards.Items.Cast<StandardChoice>().FirstOrDefault(c => StandardStore.SamePath(c.Path, StandardStore.CompanyDefault))
            ?? _standards.Items.Cast<StandardChoice>().FirstOrDefault(c => c.Path == StandardStore.DefaultPath)
            ?? (StandardChoice)_standards.Items[0]!;
        _standards.SelectedItem = wanted;
        _loadingStandards = false;
        UseSelectedStandard();
    }

    private void UseSelectedStandard()
    {
        if (_standards.SelectedItem is not StandardChoice choice) return;
        try
        {
            _standard = choice.Path is null ? CadStandard.CreateDefault() : StandardStore.Load(choice.Path);
            _settings.StandardFile = choice.Path;
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _standard = CadStandard.CreateDefault();
            _status.ForeColor = Brand.Failure;
            _status.Text = $"Couldn't read {choice.Label}: {ex.Message} Using the built-in standard.";
        }
        Redraw();
    }

    private void ChooseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder with the CAD standards (.json), e.g. the company folder on the shared drive",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_settings.StandardsFolder) ? _settings.StandardsFolder! : AppFolders.Standards ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.StandardsFolder = dlg.SelectedPath;
        _settings.Save();
        FillStandards();
        var first = _standards.Items.Cast<StandardChoice>().FirstOrDefault(c => c.Path is not null && c.Path.StartsWith(dlg.SelectedPath, StringComparison.OrdinalIgnoreCase));
        if (first is not null) _standards.SelectedItem = first;
    }

    private void FitSummary()
    {
        if (_summary.Parent is { } p)
            _summary.MaximumSize = new Size(Math.Max(200, p.ClientSize.Width - 12), 0);
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Top, Margin = Padding.Empty };
        foreach (var c in controls)
            c.Margin = new Padding(3, c is Label ? 8 : 4, 10, 3);
        f.Controls.AddRange(controls);
        return f;
    }

    private static NumericUpDown Num(decimal min, decimal max, decimal value, decimal step) =>
        new() { Minimum = min, Maximum = max, Value = value, Increment = step, Width = 75 };

    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "pavement detail" : s;
    }
}
