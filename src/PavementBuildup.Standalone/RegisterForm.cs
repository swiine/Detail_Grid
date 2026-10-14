using System.Diagnostics;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

/// <summary>
/// The project pavement register: every design saved for a job number, with its result, thickness, rough
/// cost and report. Open one, open its report, compare options side by side, or export the register to Excel.
/// </summary>
internal sealed class RegisterForm : Form
{
    private readonly TextBox _job = new() { Width = 140, PlaceholderText = "job no." };
    private readonly CheckBox _all = new() { Text = "Show every saved version", AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { ForeColor = Brand.Muted };
    private readonly string _project;
    private RateTable _rates;
    private List<RegisterEntry> _entries = new();

    /// <summary>The design to open in the wizard (set when "Open in the wizard" is used).</summary>
    public DesignFile? OpenThis { get; private set; }

    public RegisterForm(string jobNumber, string project, RateTable rates)
    {
        _project = project;
        _rates = rates;
        Text = "Project register";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Brand.Px(1180), Brand.Px(640));
        MinimumSize = new Size(Brand.Px(820), Brand.Px(420));
        _job.Text = jobNumber;

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => Scan();
        _job.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Scan(); } };
        _all.CheckedChanged += (_, _) => Scan();
        var top = Row(new Label { Text = "Job number", AutoSize = true }, _job, refresh, _all);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Brand.Surface;
        foreach (var (name, weight) in new[] { ("Pavement", 150), ("Type", 80), ("CBR", 45), ("Traffic / loads", 140), ("Result", 260), ("Thickness", 65), ("$/m²", 55), ("Saved", 95), ("Report", 55) })
            _grid.Columns[_grid.Columns.Add(name, name)].FillWeight = weight;
        _grid.Columns[6].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenReport(); };
        _grid.SelectionChanged += (_, _) => UpdateButtons();

        var open = new Button { Text = "Open in the wizard", AutoSize = true, Tag = Brand.PrimaryTag };
        open.Click += (_, _) =>
        {
            if (Selected().FirstOrDefault() is not { } e) return;
            OpenThis = e.File;
            DialogResult = DialogResult.OK;
        };
        var report = new Button { Text = "Open report", AutoSize = true };
        report.Click += (_, _) => OpenReport();
        var compare = new Button { Text = "Compare selected…", AutoSize = true };
        compare.Click += (_, _) => Compare();
        var export = new Button { Text = "Export register to Excel…", AutoSize = true };
        export.Click += (_, _) => Export();
        var editRates = new Button { Text = "Rates…", AutoSize = true };
        editRates.Click += (_, _) => DesignWizard.EditSupportFile(RateTable.FileName, path => RateTable.CreateDefault().Save(path), () => { _rates = RateTable.LoadCompany(); Scan(); });
        var folder = new Button { Text = "Open exports folder", AutoSize = true };
        folder.Click += (_, _) => Shell(AppFolders.Exports);
        var bottom = Row(open, report, compare, export, editRates, folder);
        bottom.Dock = DockStyle.Bottom;
        bottom.Padding = new Padding(0, 8, 0, 0);
        _openButtons = new[] { open, report };
        _compareButton = compare;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };
        top.Dock = DockStyle.Top;
        body.Controls.Add(_grid);
        body.Controls.Add(top);
        body.Controls.Add(bottom);
        _grid.BringToFront();
        Controls.Add(body);
        Brand.Decorate(this, "Project register", "Every pavement design saved for a job  ·  tick two or more rows to compare", _status);
        body.BringToFront();
        Shown += (_, _) => Scan();
    }

    private readonly Button[] _openButtons;
    private readonly Button _compareButton;

    private void Scan()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _entries = ProjectRegister.Scan(AppFolders.Exports, _job.Text.Trim(), _rates, _all.Checked);
        }
        finally { Cursor = Cursors.Default; }
        _grid.Rows.Clear();
        foreach (var e in _entries)
        {
            int row = _grid.Rows.Add(e.Name, e.Type, e.Cbr, e.Loads, e.Outcome, e.Thickness,
                e.CostPerM2 is { } c ? $"{_rates.Currency}{c:0}" : "-", e.Saved.ToString("d MMM yyyy HH:mm"), e.ReportHtml is null ? "" : "✔");
            if (e.Result?.Pass == false || e.Error is not null) _grid.Rows[row].Cells[4].Style.ForeColor = Brand.Failure;
        }
        _status.ForeColor = Brand.Muted;
        _status.Text = _entries.Count == 0
            ? (_job.Text.Trim().Length == 0 ? "No saved designs yet." : $"No saved designs for job {_job.Text.Trim()}. Save a design (or make its report) in the Design tab.")
            : $"{_entries.Count} pavement{(_entries.Count == 1 ? "" : "s")}. Select rows (Ctrl+click) to compare. Costs are rough (rates file{(_rates.SourcePath is null ? ": placeholder rates" : "")}).";
        UpdateButtons();
    }

    private List<RegisterEntry> Selected() =>
        _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => _entries[r.Index]).ToList();

    private void UpdateButtons()
    {
        int n = _grid.SelectedRows.Count;
        foreach (var b in _openButtons) b.Enabled = n == 1;
        _compareButton.Enabled = n >= 2;
    }

    private void OpenReport()
    {
        if (Selected().FirstOrDefault() is not { } e) return;
        if (e.ReportHtml is null)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "No report saved for this design yet: open it in the wizard and make the calculation report.";
            return;
        }
        Shell(e.ReportHtml);
    }

    private void Compare()
    {
        var picked = Selected().Where(e => e.Result is not null).ToList();
        if (picked.Count < 2)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "Select two or more designs that calculate (Ctrl+click rows).";
            return;
        }
        var result = DesignCompare.Compare(picked.Select(e => new DesignOption(e.Name, e.File, e.Result!)).ToList(), _rates.Currency);
        var job = _job.Text.Trim();
        var folder = AppFolders.ForDesign(job, "Options comparison");
        using var dlg = new SaveFileDialog
        {
            InitialDirectory = folder, Filter = "Comparison report (*.html; an Excel copy is saved next to it)|*.html", Title = "Save the options comparison (HTML + Excel)",
            FileName = Safe($"{job} options comparison".Trim()) + ".html",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.Tidy(folder);
            return;
        }
        try
        {
            var html = dlg.FileName;
            var xlsx = Path.ChangeExtension(html, ".xlsx");
            var projectLine = string.Join("  ·  ", new[] { _project, job.Length > 0 ? "Job " + job : "" }.Where(s => s.Length > 0));
            File.WriteAllText(html, CalcReport.Html(result, projectLine, null, null, Path.GetFileName(xlsx)));
            PavementBuildup.Reports.ExcelReport.Save(result, xlsx, projectLine, null, null, Path.GetFileName(html));
            Shell(html);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save the comparison: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Export()
    {
        var job = _job.Text.Trim();
        var folder = AppFolders.ForDesign(job, "Register");
        using var dlg = new SaveFileDialog
        {
            InitialDirectory = folder, Filter = "Excel workbook (*.xlsx)|*.xlsx", Title = "Export the pavement register",
            FileName = Safe($"{job} pavement register".Trim()) + ".xlsx",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.Tidy(folder);
            return;
        }
        try
        {
            PavementBuildup.Reports.RegisterReport.Save(_entries, dlg.FileName, job, _project, _rates.Currency);
            Shell(dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save the register: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Shell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _status.ForeColor = Brand.Failure;
            _status.Text = "Could not open " + Path.GetFileName(path) + ": " + ex.Message;
        }
    }

    private static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "register" : s;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        foreach (var c in controls) c.Margin = new Padding(0, c is Label or CheckBox ? 7 : 3, 10, 3);
        f.Controls.AddRange(controls);
        return f;
    }
}
