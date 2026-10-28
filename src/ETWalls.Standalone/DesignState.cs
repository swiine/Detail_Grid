using ETWalls.Core.Design;
using ETWalls.Core.Drawing;
using Timer = System.Windows.Forms.Timer;

namespace ETWalls.Standalone;

/// <summary>
/// The design being worked on. Edits are collected for a moment, then the wall is sized and checked again
/// (the cost search takes about a tenth of a second) and <see cref="Computed"/> fires.
/// </summary>
internal sealed class DesignState : IDisposable
{
    private readonly Timer _timer = new() { Interval = 250 };

    public DesignState(WallInput input)
    {
        Input = input;
        _timer.Tick += (_, _) => { _timer.Stop(); Compute(); };
    }

    /// <summary>What was typed (sizes as entered when Custom is on).</summary>
    public WallInput Input { get; private set; }
    /// <summary>The latest result, or null while something can't be read.</summary>
    public DesignRun? Run { get; private set; }
    /// <summary>The first thing that needs fixing ("Retained height: Must be more than 0."), or null.</summary>
    public string? Problem { get; private set; }
    /// <summary>Which wizard step the problem is on (0-2).</summary>
    public int ProblemStep { get; private set; }

    public event Action? Computed;

    /// <summary>Asks the wizard for the first problem before computing.</summary>
    public Func<(string? Problem, int Step)>? Validate { get; set; }

    public void Edit(Action<WallInput> change) { change(Input); Later(); }

    public void Replace(WallInput input) { Input = input; ComputeNow(); }

    public void Later() { _timer.Stop(); _timer.Start(); }

    public void ComputeNow() { _timer.Stop(); Compute(); }

    private void Compute()
    {
        var (problem, step) = Validate?.Invoke() ?? (null, 0);
        Problem = problem; ProblemStep = step;
        try
        {
            Run = problem is null ? DesignRun.For(Input) : null;
        }
        catch (Exception ex) when (ex is ArithmeticException or ArgumentException or InvalidOperationException)
        {
            Run = null; Problem = "These values can't be worked out: " + ex.Message;
        }
        Computed?.Invoke();
    }

    public SectionDrawing? Drawing(bool full, int scale = 0) =>
        Run is { } r ? SectionBuilder.Build(r.Input, r.Result, full, scale) : null;

    public void Dispose() => _timer.Dispose();
}
