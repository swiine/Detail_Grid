using System.Diagnostics;
using PavementBuildup.Core;
using PavementBuildup.Export;
using PavementBuildup.Reports;

namespace PavementBuildup;

/// <summary>
/// Batch from Excel: a list of pavement types (one per row) read and turned into all the details at once,
/// as one DXF (each detail a block, in a grid) and / or a PDF with a sheet per detail.
/// </summary>
internal sealed class BatchForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { ForeColor = Brand.Muted, Text = "Open an Excel list of pavement types, or make the template first." };
    private readonly NumericUpDown _columns = new() { Minimum = 1, Maximum = 10, Value = 3, Width = 55 };
    private readonly ComboBox _paper = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    private readonly Button _dxf = new() { Text = "Save all as one DXF…", AutoSize = true, Tag = Brand.PrimaryTag, Enabled = false };
    private readonly Button _pdf = new() { Text = "Save all as PDF…", AutoSize = true, Enabled = false };
    private readonly Func<DetailSettings> _settings;
    private readonly Func<CadStandard> _standard;
    private readonly string _job, _project;
    private List<BatchItem> _items = new();
    private string? _source;

    public BatchForm(Func<DetailSettings> settings, Func<CadStandard> standard, string jobNumber, string project)
    {
        _settings = settings;
        _standard = standard;
        _job = jobNumber;
        _project = project;
        Text = "Batch from Excel";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Brand.Px(1000), Brand.Px(600));
        MinimumSize = new Size(Brand.Px(760), Brand.Px(420));

        var template = new Button { Text = "Make the Excel template…", AutoSize = true };
        template.Click += (_, _) => MakeTemplate();
        var open = new Button { Text = "Open Excel list…", AutoSize = true, Tag = Brand.PrimaryTag };
        open.Click += (_, _) => OpenList(null);
        var reload = new Button { Text = "Read again", AutoSize = true };
        reload.Click += (_, _) => { if (_source is not null) OpenList(_source); };
        var top = Row(Brand.Step("①"), template, open, reload);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Brand.Surface;
        foreach (var (name, weight) in new[] { ("Row", 30), ("Pavement", 140), ("Layers", 260), ("Depth", 50), ("Status", 160) })
            _grid.Columns[_grid.Columns.Add(name, name)].FillWeight = weight;

        _paper.Items.AddRange(new object[] { "A4", "A3" });
        _paper.SelectedIndex = 0;
        _dxf.Click += (_, _) => Save(pdf: false);
        _pdf.Click += (_, _) => Save(pdf: true);
        var bottom = Row(Brand.Step("②"), _dxf, new Label { Text = "details across", AutoSize = true }, _columns, _pdf, new Label { Text = "paper", AutoSize = true }, _paper);
        bottom.Dock = DockStyle.Bottom;
        top.Dock = DockStyle.Top;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };
        body.Controls.Add(_grid);
        body.Controls.Add(top);
        body.Controls.Add(bottom);
        _grid.BringToFront();
        Controls.Add(body);
        Brand.Decorate(this, "Batch from Excel", "A list of pavement types in, all the details out  ·  uses the Detail tab's scale and standard", _status);
        body.BringToFront();
    }

    private void MakeTemplate()
    {
        using var dlg = new SaveFileDialog
        {
            InitialDirectory = AppFolders.Exports, Filter = "Excel workbook (*.xlsx)|*.xlsx", Title = "Save the batch template", FileName = "Pavement types.xlsx",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            BatchSheet.WriteTemplate(dlg.FileName);
            _source = dlg.FileName;
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            _status.ForeColor = Brand.Muted;
            _status.Text = "Template opened in Excel: fill it in, save it, then press Read again.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not make the template: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenList(string? path)
    {
        if (path is null)
        {
            using var dlg = new OpenFileDialog { InitialDirectory = AppFolders.Exports, Filter = "Excel workbook (*.xlsx)|*.xlsx", Title = "Open the list of pavement types" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        try
        {
            _items = BatchSheet.Read(path);
            _source = path;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "✖ " + ex.Message;
            return;
        }
        _grid.Rows.Clear();
        foreach (var item in _items)
        {
            var b = item.Buildup;
            int row = _grid.Rows.Add(item.Row, item.Name,
                b is null ? item.Text.Replace("\n", " / ") : string.Join(" / ", b.Layers.Select(l => (l.TotalMm > 0 ? $"{l.TotalMm:0} " : "") + l.Description)),
                b is null ? "" : $"{b.TotalThicknessMm:0} mm",
                b is null ? "✖ " + string.Join(" ", item.Problems) : item.Problems.Count > 0 ? "✔ (" + string.Join(" ", item.Problems) + ")" : "✔");
            if (b is null) _grid.Rows[row].DefaultCellStyle.ForeColor = Brand.Failure;
        }
        int good = _items.Count(i => i.Buildup is not null);
        _dxf.Enabled = _pdf.Enabled = good > 0;
        _status.ForeColor = good == _items.Count ? Brand.Success : Brand.Failure;
        _status.Text = $"{good} of {_items.Count} pavement types read from {Path.GetFileName(path)}"
                       + (good < _items.Count ? ": the red rows are left out (fix them in Excel, save, Read again)." : ".");
    }

    private void Save(bool pdf)
    {
        var buildups = _items.Where(i => i.Buildup is not null).Select(i => i.Buildup!).ToList();
        if (buildups.Count == 0) return;
        var folder = AppFolders.ForDesign(_job, "Batch details");
        var name = Path.GetFileNameWithoutExtension(_source ?? "Pavement details");
        using var dlg = new SaveFileDialog
        {
            InitialDirectory = folder, Filter = pdf ? "PDF (*.pdf)|*.pdf" : "DXF drawing (*.dxf)|*.dxf",
            Title = pdf ? "Save all the details as a PDF" : "Save all the details in one DXF", FileName = name + (pdf ? ".pdf" : ".dxf"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.Tidy(folder);
            return;
        }
        try
        {
            using (var fs = File.Create(dlg.FileName))
            {
                if (pdf)
                    PdfExport.WriteMany(buildups, _settings(), _standard(), new PdfSheet { Project = _project, JobNumber = _job, Paper = (string)_paper.SelectedItem! }, fs);
                else
                    DxfExport.WriteMany(buildups, _settings(), _standard(), fs, (int)_columns.Value);
            }
            _status.ForeColor = Brand.Success;
            _status.Text = $"Saved {buildups.Count} details to {Path.GetFileName(dlg.FileName)}."
                           + (pdf ? "" : " In CAD: INSERT it (each detail is a block named TTW_PAVE_<name>).");
            if (pdf) Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        foreach (var c in controls) c.Margin = new Padding(0, c is Label ? 8 : 3, 10, 3);
        f.Controls.AddRange(controls);
        return f;
    }
}
