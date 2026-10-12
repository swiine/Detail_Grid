using PavementBuildup.Core;
using PavementBuildup.Export;

namespace PavementBuildup;

/// <summary>
/// TTW Pavement without Civil 3D: paste a build-up, check the picture, save a DXF (or copy it for TTWPVMT).
/// Same reader, layout and standard as the plugin.
/// </summary>
internal sealed class StandaloneForm : Form
{
    private readonly TextBox _text = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true,
        PlaceholderText = "e.g.  60mm pavers, 30mm sand, 150mm road base, subgrade\r\n\r\n" +
                          "Or paste anything: a list, a table from Excel, a spec paragraph, a note from a drawing.",
    };
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 8, 3, 2) };
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Type or paste a build-up. It's read as you type." };
    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly ToolTip _tips = new();

    private readonly NumericUpDown _scale = Num(1, 500, 10, 1);
    private readonly NumericUpDown _width = Num(100, 20000, 1000, 100);
    private readonly ComboBox _units = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _dims = new() { Text = "Dimensions", AutoSize = true, Checked = true };
    private readonly CheckBox _breaks = new() { Text = "Break lines", AutoSize = true, Checked = true };
    private readonly CheckBox _title = new() { Text = "Title", AutoSize = true, Checked = true };
    private readonly CheckBox _subgrade = new() { Text = "Subgrade", AutoSize = true, Checked = true };
    private readonly Label _standardLabel = new() { AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(3, 9, 3, 3) };

    private CadStandard _standard = CadStandard.CreateDefault();
    private Buildup? _buildup;
    private readonly Button _save = new() { Text = "Save DXF…", AutoSize = true };

    public StandaloneForm()
    {
        Text = "TTW Pavement";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(Brand.Px(1180), Brand.Px(760));
        MinimumSize = new Size(Brand.Px(860), Brand.Px(560));
        Brand.Decorate(this, "TTW Pavement", "Paste a build-up, check the picture, save a DXF  ·  no Civil 3D needed", _status);

        _units.Items.AddRange(new object[] { "Metres", "Millimetres" });
        _units.SelectedIndex = 0;
        LoadSavedStandard();

        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Brand.Bg };
        Load += (_, _) => split.SplitterDistance = (int)(split.Width * 0.42);
        split.Panel1.Controls.Add(BuildLeft());
        split.Panel2.Controls.Add(BuildRight());

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 8) };
        root.Controls.Add(split);
        Controls.Add(root);
        root.BringToFront(); // fills what the header and status line leave

        _text.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        _timer.Tick += (_, _) => { _timer.Stop(); ReadText(); };
        foreach (var n in new[] { _scale, _width }) n.ValueChanged += (_, _) => Redraw();
        _units.SelectedIndexChanged += (_, _) => Redraw();
        foreach (var c in new[] { _dims, _breaks, _title, _subgrade }) c.CheckedChanged += (_, _) => Redraw();
        FormClosed += (_, _) => { _timer.Dispose(); _tips.Dispose(); };
        ShowSummary(null);
    }

    // ---------------------------------------------------------------- layout -------

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
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { paste, ai, clear });
        t.Controls.Add(buttons);

        var options = new GroupBox { Text = "Drawing", Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        var rows = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var loadStd = new Button { Text = "Load CAD standard…", AutoSize = true };
        loadStd.Click += (_, _) => ChooseStandard();
        rows.Controls.AddRange(new Control[]
        {
            Lbl("Scale 1:"), _scale, Lbl("Width (mm):"), _width, Lbl("Units:"), _units,
            _dims, _breaks, _title, _subgrade, loadStd, _standardLabel,
        });
        foreach (Control c in rows.Controls) c.Margin = new Padding(3, 6, 10, 3);
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
        var copy = new Button { Text = "Copy for TTWPVMT", AutoSize = true, MinimumSize = new Size(0, Brand.Px(40)) };
        copy.Click += (_, _) => CopyForPlugin();
        _tips.SetToolTip(copy, "Copies the tidy build-up. In Civil 3D: TTWPVMT > New pavement detail, paste into box ①.");
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(Brand.Step("③"));
        bar.Controls.Add(_save);
        bar.Controls.Add(copy);
        copy.Margin = new Padding(10, 3, 3, 3);
        t.Controls.Add(bar);
        return t;
    }

    // ---------------------------------------------------------------- behaviour ----

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

    private DetailSettings Settings() => new()
    {
        ScaleDenominator = (double)_scale.Value,
        WidthMm = (double)_width.Value,
        UnitsOverride = _units.SelectedIndex == 1 ? "MM" : "M",
        ShowDimensions = _dims.Checked,
        ShowBreakLines = _breaks.Checked,
        ShowTitle = _title.Checked,
        CreateBlock = false,
    };

    private void Redraw()
    {
        _save.Enabled = _buildup is not null;
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
        using var dlg = new SaveFileDialog
        {
            Filter = "DXF drawing (*.dxf)|*.dxf",
            Title = "Save the pavement detail",
            FileName = SafeFileName(_buildup.Name) + ".dxf",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (var fs = File.Create(dlg.FileName))
                DxfExport.Write(_buildup, Settings(), _standard, fs);
            _status.ForeColor = Brand.Success;
            _status.Text = $"Saved {Path.GetFileName(dlg.FileName)}. In CAD: INSERT it (or drag the file into the drawing).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, "Could not save the DXF: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        catch (System.ComponentModel.Win32Exception) { /* no browser: the message is on the clipboard */ }
        _status.ForeColor = Brand.Muted;
        _status.Text = "Claude.ai opened (Ctrl+V if the box is empty). Send it, copy the reply, and paste it into box ①.";
    }

    // ---------------------------------------------------------------- CAD standard ---

    /// <summary>The same personal standard the plugin uses (%APPDATA%\PavementBuildup\standard.json), if there is one.</summary>
    private void LoadSavedStandard()
    {
        try
        {
            var settings = PresetStore.Default().Load().Settings;
            _standard = StandardStore.Load(StandardStore.PathFor(settings));
            _standardLabel.Text = "Standard: " + _standard.Name;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _standard = CadStandard.CreateDefault();
            _standardLabel.Text = "Standard: built-in";
        }
    }

    private void ChooseStandard()
    {
        using var dlg = new OpenFileDialog { Filter = "CAD standard (*.json)|*.json", Title = "Load CAD standard (e.g. the company one on a shared drive)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _standard = StandardStore.Load(dlg.FileName);
            _standardLabel.Text = "Standard: " + _standard.Name;
            Redraw();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------------- helpers ------

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true };

    private static NumericUpDown Num(decimal min, decimal max, decimal value, decimal step) =>
        new() { Minimum = min, Maximum = max, Value = value, Increment = step, Width = 75 };

    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "pavement detail" : s;
    }
}
