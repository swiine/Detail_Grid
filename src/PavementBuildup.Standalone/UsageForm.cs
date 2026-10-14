using System.Diagnostics;
using PavementBuildup.Core;

namespace PavementBuildup;

internal sealed class UsageForm : Form
{
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ListBox _logs = new() { Height = Brand.Px(70), Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView _grid = new();
    private readonly Label _totals = new() { AutoSize = true, Font = Brand.Sized(12f, FontStyle.Bold), ForeColor = Brand.Heading, Margin = new Padding(3, 8, 3, 6) };
    private readonly Label _status = new() { ForeColor = Brand.Muted };
    private readonly List<string> _files = new() { UsageLog.FilePath };
    private TimeSaved _minutes = TimeSaved.LoadCompany();
    private UsageSummary? _summary;
    private List<UsageEvent> _events = new();

    public UsageForm()
    {
        Text = "Time saved";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Brand.Px(900), Brand.Px(640));
        MinimumSize = new Size(Brand.Px(700), Brand.Px(480));

        _period.Items.AddRange(new object[] { "This month", "Last month", "Last 3 months", "This year", "All time" });
        _period.SelectedIndex = 4;
        _period.SelectedIndexChanged += (_, _) => Refresh(reload: false);

        var add = new Button { Text = "Add other people's logs…", AutoSize = true };
        add.Click += (_, _) => AddLogs();
        var folder = new Button { Text = "Open my log folder", AutoSize = true };
        folder.Click += (_, _) => Shell(Path.GetDirectoryName(UsageLog.FilePath)!);
        var minutes = new Button { Text = "Minutes per tool…", AutoSize = true };
        minutes.Click += (_, _) => DesignWizard.EditSupportFile(TimeSaved.FileName, path => TimeSaved.CreateDefault().Save(path), () => { _minutes = TimeSaved.LoadCompany(); Refresh(reload: false); });
        var save = new Button { Text = "Save report (HTML + Excel)…", AutoSize = true, Tag = Brand.PrimaryTag };
        save.Click += (_, _) => SaveReport();

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Brand.Surface;
        foreach (var (name, weight) in new[] { ("Tool", 220), ("Uses", 60), ("Min / use", 70), ("Hours saved", 80) })
            _grid.Columns[_grid.Columns.Add(name, name)].FillWeight = weight;
        foreach (int c in new[] { 1, 2, 3 }) _grid.Columns[c].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        top.Controls.Add(Row(new Label { Text = "Period", AutoSize = true }, _period, add, folder, minutes));
        top.Controls.Add(new Label { Text = "Logs included (yours, plus any usage.jsonl files from the team):", AutoSize = true, ForeColor = Brand.Muted, Margin = new Padding(3, 6, 3, 2) });
        var logsHost = new Panel { Height = Brand.Px(70), Dock = DockStyle.Top };
        logsHost.Controls.Add(_logs);
        top.Controls.Add(logsHost);
        top.Controls.Add(_totals);
        var bottom = Row(save);
        bottom.Dock = DockStyle.Bottom;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8) };
        body.Controls.Add(_grid);
        body.Controls.Add(top);
        body.Controls.Add(bottom);
        _grid.BringToFront();
        Controls.Add(body);
        Brand.Decorate(this, "Time saved", "How much TTW Pavement has saved: uses x minutes saved per use", _status);
        body.BringToFront();
        Shown += (_, _) => Refresh(reload: true);
    }

    private (DateTime? From, DateTime? To) Period()
    {
        var today = DateTime.Today;
        var month = new DateTime(today.Year, today.Month, 1);
        return _period.SelectedIndex switch
        {
            0 => (month, month.AddMonths(1)),
            1 => (month.AddMonths(-1), month),
            2 => (month.AddMonths(-2), month.AddMonths(1)),
            3 => (new DateTime(today.Year, 1, 1), new DateTime(today.Year + 1, 1, 1)),
            _ => (null, null),
        };
    }

    private void Refresh(bool reload)
    {
        if (reload)
        {
            _events = UsageLog.Read(_files);
            _logs.Items.Clear();
            foreach (var f in _files) _logs.Items.Add(f + (File.Exists(f) ? "" : "  (nothing logged yet)"));
        }
        var (from, to) = Period();
        _summary = UsageReport.Summarise(_events, _minutes, from, to);
        _grid.Rows.Clear();
        foreach (var r in _summary.ByTool)
            _grid.Rows.Add(r.Name, r.Uses, _summary.MinutesPerUse.TryGetValue(r.Name, out var m) ? m.ToString("0.#") : "-", r.Hours.ToString("0.0"));
        _totals.Text = $"{_summary.Hours:0.#} hours saved  ·  {_summary.Hours / 7.6:0.#} working days  ·  {_summary.Uses} uses  ·  {_summary.People} {(_summary.People == 1 ? "person" : "people")}";
        _status.ForeColor = Brand.Muted;
        _status.Text = _minutes.SourcePath is null ? "Minutes per use: built-in estimates (Minutes per tool… to set the team's)." : "Minutes per use from " + _minutes.SourcePath;
    }

    private void AddLogs()
    {
        using var dlg = new OpenFileDialog { Filter = "Usage logs (*.jsonl)|*.jsonl|All files (*.*)|*.*", Multiselect = true, Title = "Add other people's usage logs (usage.jsonl)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in dlg.FileNames)
            if (!_files.Contains(f, StringComparer.OrdinalIgnoreCase)) _files.Add(f);
        Refresh(reload: true);
    }

    private void SaveReport()
    {
        if (_summary is null) return;
        var folder = AppFolders.Today;
        using var dlg = new SaveFileDialog
        {
            InitialDirectory = folder, Filter = "Time saved report (*.html; an Excel copy is saved next to it)|*.html", Title = "Save the time saved report",
            FileName = $"TTW Pavement time saved - {(string)_period.SelectedItem!}.html",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            AppFolders.TidyToday();
            return;
        }
        try
        {
            var (from, to) = Period();
            File.WriteAllText(dlg.FileName, UsageReport.Html(_summary));
            PavementBuildup.Reports.UsageExcel.Save(_summary, _events.Where(e => (from is null || e.Time >= from) && (to is null || e.Time < to)).ToList(),
                Path.ChangeExtension(dlg.FileName, ".xlsx"));
            Shell(dlg.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save the report: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Shell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { _status.Text = "Could not open " + path + ": " + ex.Message; }
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        foreach (var c in controls) c.Margin = new Padding(0, c is Label ? 8 : 3, 10, 3);
        f.Controls.AddRange(controls);
        return f;
    }
}
