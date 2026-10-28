using ETWalls.Core.Standard;
using ETWalls.Export;

namespace ETWalls.Standalone;

/// <summary>
/// The Detail tab: the wall section at a drawing scale, on the chosen CAD standard, saved as a DXF to insert into
/// Civil 3D / AutoCAD.
/// </summary>
internal sealed class DetailPanel : UserControl
{
    private readonly DesignState _state;
    private readonly Settings _settings;
    private readonly SectionView _view = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _scale = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(200) };
    private readonly ComboBox _standard = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Brand.Px(200) };
    private readonly System.Windows.Forms.Label _about = new() { AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(240), 0) };
    private static readonly int[] Scales = { 0, 10, 20, 25, 50, 100 };

    public event Action<string>? Status;

    public DetailPanel(DesignState state, Settings settings)
    {
        _state = state; _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Brand.Bg;

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, Width = Brand.Px(270), FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 12, 8, 12), AutoScroll = true };
        left.Controls.Add(Brand.Step("Wall section"));
        left.Controls.Add(_about);
        left.Controls.Add(new System.Windows.Forms.Label { Text = "Scale", AutoSize = true, Margin = new Padding(3, 10, 3, 2) });
        foreach (var s in Scales) _scale.Items.Add(s == 0 ? "Fit (pick for me)" : $"1:{s}");
        _scale.SelectedIndex = Math.Max(0, Array.IndexOf(Scales, settings.Scale));
        _scale.SelectedIndexChanged += (_, _) => { _settings.Scale = Scales[_scale.SelectedIndex]; _settings.Save(); Redraw(true); };
        left.Controls.Add(_scale);
        left.Controls.Add(new System.Windows.Forms.Label { Text = "CAD standard", AutoSize = true, Margin = new Padding(3, 10, 3, 2) });
        left.Controls.Add(_standard);
        var folder = new LinkLabel { Text = "Standards folder…", AutoSize = true };
        folder.LinkClicked += (_, _) => PickStandardsFolder();
        left.Controls.Add(folder);
        _standard.SelectedIndexChanged += (_, _) => { if (_standard.SelectedItem is string f) { _settings.StandardFile = f; _settings.Save(); } };

        var save = new Button { Text = "Save DXF…", AutoSize = true, Tag = Brand.PrimaryTag, Margin = new Padding(3, 16, 3, 4) };
        save.Click += (_, _) => SaveDxf();
        var flip = new Button { Text = "Flip (mirror the wall)", AutoSize = true };
        flip.Click += (_, _) => _state.Edit(i => i.FacesRight = !i.FacesRight);
        var open = new Button { Text = "Open the exports folder", AutoSize = true };
        open.Click += (_, _) => OpenFolder(AppFolders.Exports);
        foreach (var b in new[] { save, flip, open }) left.Controls.Add(b);
        left.Controls.Add(new System.Windows.Forms.Label
        {
            Text = "Layers, text and hatches come from the CAD standard. The DXF is drawn in metres at true size: INSERT it (or drag it) into Civil 3D / AutoCAD.\r\n\r\nPicture: scroll to zoom, drag to move, double-click to fit.",
            AutoSize = true, ForeColor = Brand.Muted, MaximumSize = new Size(Brand.Px(240), 0), Margin = new Padding(3, 12, 3, 3),
        });

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Brand.Border, Margin = new Padding(0) };
        frame.Controls.Add(_view);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 22, 12) };
        host.Controls.Add(frame);
        Controls.Add(host);
        Controls.Add(left);

        LoadStandards();
        _state.Computed += () => Redraw(false);
    }

    private int DrawScale => Scales[Math.Max(0, _scale.SelectedIndex)];

    public void Redraw(bool fit)
    {
        var d = _state.Drawing(full: true, scale: DrawScale);
        _view.SetDrawing(d, _state.Problem is { } p ? "Fix this on the Design tab to see the wall: " + p : null, fit);
        var i = _state.Input;
        _about.Text = d is null ? "" : $"{(string.IsNullOrWhiteSpace(i.WallName) ? "This wall" : i.WallName)}, drawn at 1:{d.Scale}.";
    }

    private string? StandardsFolder => Directory.Exists(_settings.StandardsFolder) ? _settings.StandardsFolder : AppFolders.Standards;

    private void LoadStandards()
    {
        _standard.Items.Clear();
        if (StandardsFolder is { } dir)
            foreach (var f in Directory.EnumerateFiles(dir, "*.json").Select(Path.GetFileName).OrderBy(f => f)) _standard.Items.Add(f!);
        if (_standard.Items.Count == 0) _standard.Items.Add("TTW (built in)");
        int pick = _settings.StandardFile is { } want ? _standard.Items.IndexOf(want) : -1;
        if (pick < 0) pick = Math.Max(0, _standard.Items.IndexOf("TTW.json"));
        _standard.SelectedIndex = pick;
    }

    private CadStandard Standard()
    {
        if (StandardsFolder is { } dir && _standard.SelectedItem is string f && File.Exists(Path.Combine(dir, f)))
        {
            try { return CadStandard.Load(Path.Combine(dir, f)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                Status?.Invoke($"Couldn't read {f} ({ex.Message}). Using the built-in TTW layers.");
            }
        }
        return CadStandard.Default;
    }

    private void PickStandardsFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder with the CAD standards (.json)", SelectedPath = StandardsFolder ?? "" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.StandardsFolder = dlg.SelectedPath;
        _settings.Save();
        LoadStandards();
    }

    private void SaveDxf()
    {
        var d = _state.Drawing(full: true, scale: DrawScale);
        if (d is null) { Status?.Invoke("Fix this on the Design tab first: " + _state.Problem); return; }
        var i = _state.Input;
        var folder = AppFolders.ForDesign(i.JobNumber, i.WallName);
        using var dlg = new SaveFileDialog { Filter = "DXF drawing (*.dxf)|*.dxf", InitialDirectory = folder, FileName = $"{AppFolders.Part(i.WallName, "Wall")} - section 1-{d.Scale}.dxf" };
        if (dlg.ShowDialog(this) != DialogResult.OK) { AppFolders.Tidy(folder); return; }
        try
        {
            using var fs = File.Create(dlg.FileName);
            DxfExport.Write(d, Standard(), fs);
            Status?.Invoke("Saved " + dlg.FileName);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, "Couldn't save the DXF. Is it open in another program?\n\n" + ex.Message, "ET Walls", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenFolder(string dir)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
