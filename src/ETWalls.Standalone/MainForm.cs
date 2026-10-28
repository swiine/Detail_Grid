using System.Globalization;
using ETWalls.Core.Design;
using ETWalls.Reports;

namespace ETWalls.Standalone;

/// <summary>The ET Walls window: a Detail tab (section and DXF) and a Design tab (the 4-step wizard).</summary>
internal sealed class MainForm : Form
{
    private readonly Settings _settings = Settings.Load();
    private readonly DesignState _state;
    private readonly System.Windows.Forms.Label _status = new() { Text = "Ready." };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    private readonly DesignWizard _wizard;

    internal DesignState State => _state;
    /// <summary>For the self test: load a design and compute it now.</summary>
    internal void LoadDesign(WallInput input) { _state.Replace(input); _wizard.LoadFrom(input); }
    internal void Step(int step) => _wizard.Go(step);

    public MainForm()
    {
        Text = "ET Walls";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(Brand.Px(980), Brand.Px(640));
        Size = new Size(Brand.Px(1280), Brand.Px(860));
        Brand.Decorate(this, "ET Walls", "Standalone  ·  retaining wall design and details", _status);

        _state = new DesignState(NewInput(_settings));
        var wizard = _wizard = new DesignWizard(_state, _settings);
        var detail = new DetailPanel(_state, _settings);
        var detailPage = new TabPage("Detail") { BackColor = Brand.Bg };
        var designPage = new TabPage("Design") { BackColor = Brand.Bg };
        detailPage.Controls.Add(detail);
        designPage.Controls.Add(wizard);
        _tabs.TabPages.Add(detailPage);
        _tabs.TabPages.Add(designPage);
        _tabs.SelectedTab = designPage;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 0) };
        host.Controls.Add(_tabs);
        Controls.Add(host);
        host.BringToFront();   // under the header, above the status line

        wizard.Status += s => _status.Text = s;
        detail.Status += s => _status.Text = s;
        wizard.DrawRequested += () => { _tabs.SelectedTab = detailPage; detail.Redraw(true); _status.Text = "Pick a scale, then Save DXF."; };
        _state.Computed += ShowStatus;
        FormClosed += (_, _) => { _settings.Save(); _state.Dispose(); };
        Shown += (_, _) => _state.ComputeNow();
    }

    /// <summary>A new design with the remembered rates and name.</summary>
    internal static WallInput NewInput(Settings settings) => new()
    {
        Project = "", JobNumber = "", WallName = "W1", DesignedBy = settings.DesignedBy ?? "",
        Rates = settings.Rates?.Clone() ?? Rates.Defaults,
    };

    private void ShowStatus()
    {
        if (_state.Run is not { } run) { _status.Text = _state.Problem is { } p ? $"Step {_state.ProblemStep + 1}: {p}" : "Ready."; _status.ForeColor = Brand.Failure; return; }
        var r = run.Result;
        _status.ForeColor = r.Pass ? Brand.Success : Brand.Failure;
        string name = string.IsNullOrWhiteSpace(run.Input.WallName) ? "This wall" : run.Input.WallName;
        _status.Text = $"{name}  {(r.Pass ? "all checks pass" : r.Checks.Count(c => !c.Pass) + " failing")} · governing {r.Worst.Name.ToLowerInvariant()} {Math.Round(r.Worst.Util * 100).ToString(CultureInfo.InvariantCulture)}% · {ReportRows.Money(run.Cost.PerMetre)}/m";
    }
}
