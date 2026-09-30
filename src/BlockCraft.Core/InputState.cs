namespace BlockCraft.Core;

/// <summary>One frame of player input, independent of the UI toolkit.</summary>
public sealed class InputState
{
    public bool Forward, Back, Left, Right;
    public bool Jump, Sneak, Sprint;

    /// <summary>Mouse movement since the last frame, in pixels.</summary>
    public double MouseDX, MouseDY;

    /// <summary>Edge-triggered actions: set by the UI, consumed by <see cref="Game.Update"/>.</summary>
    public bool BreakPressed, PlacePressed, PickPressed, ToggleFly;

    /// <summary>Hotbar slot chosen this frame (0-based), or -1.</summary>
    public int SelectSlot = -1;

    /// <summary>Scroll-wheel notches this frame (positive = next slot).</summary>
    public int Scroll;

    public void ClearEdges()
    {
        MouseDX = MouseDY = 0;
        BreakPressed = PlacePressed = PickPressed = ToggleFly = false;
        SelectSlot = -1;
        Scroll = 0;
    }
}
