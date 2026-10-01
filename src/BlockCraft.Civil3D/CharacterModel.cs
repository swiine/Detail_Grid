using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using BlockCraft.Core;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace BlockCraft.Civil3D;

/// <summary>
/// A blocky player figure (head, body, arms, legs) drawn as transient graphics, so it is visible
/// in the viewport but never added to the drawing. It turns with the player, the head follows the
/// look direction and the arms and legs swing while walking.
/// </summary>
internal sealed class CharacterModel : IDisposable
{
    /// <summary>The figure is 2 blocks tall in these units; scaled to the player's 1.8.</summary>
    private const double Scale = Player.Height / 2.0;

    private const int Skin = 0xC69C77, Shirt = 0x00A8A8, Trousers = 0x3B3F9C, Hair = 0x3A2715, Eye = 0x1A1A40;

    private enum Swing { None, Positive, Negative, Head }

    /// <summary>One box: size, the pivot it rotates about, and its centre relative to that pivot (block units, Z up, Y forward).</summary>
    private sealed record PartSpec(double SX, double SY, double SZ, Point3d Pivot, Vector3d Offset, int Rgb, Swing Swing);

    private static readonly PartSpec[] Specs =
    {
        new(0.25, 0.25, 0.75, new Point3d(-0.125, 0, 0.75), new Vector3d(0, 0, -0.375), Trousers, Swing.Positive), // left leg
        new(0.25, 0.25, 0.75, new Point3d(0.125, 0, 0.75), new Vector3d(0, 0, -0.375), Trousers, Swing.Negative),  // right leg
        new(0.5, 0.25, 0.75, new Point3d(0, 0, 1.125), new Vector3d(0, 0, 0), Shirt, Swing.None),                   // body
        new(0.25, 0.25, 0.75, new Point3d(-0.375, 0, 1.45), new Vector3d(0, 0, -0.3), Skin, Swing.Negative),        // left arm
        new(0.25, 0.25, 0.75, new Point3d(0.375, 0, 1.45), new Vector3d(0, 0, -0.3), Skin, Swing.Positive),         // right arm
        new(0.5, 0.5, 0.5, new Point3d(0, 0, 1.5), new Vector3d(0, 0, 0.25), Skin, Swing.Head),                     // head
        new(0.52, 0.52, 0.12, new Point3d(0, 0, 1.5), new Vector3d(0, -0.01, 0.46), Hair, Swing.Head),              // hair
        new(0.1, 0.02, 0.08, new Point3d(0, 0, 1.5), new Vector3d(-0.12, 0.26, 0.3), Eye, Swing.Head),              // left eye
        new(0.1, 0.02, 0.08, new Point3d(0, 0, 1.5), new Vector3d(0.12, 0.26, 0.3), Eye, Swing.Head),               // right eye
    };

    private readonly WorldMapping _map;
    private readonly Solid3d[] _parts;
    private readonly Matrix3d[] _applied;
    private bool _shown;
    private double _phase;

    public CharacterModel(WorldMapping map)
    {
        _map = map;
        _parts = new Solid3d[Specs.Length];
        _applied = new Matrix3d[Specs.Length];
        for (int i = 0; i < Specs.Length; i++)
        {
            var s = Specs[i];
            var solid = new Solid3d();
            solid.CreateBox(s.SX * Scale * map.CellSize, s.SY * Scale * map.CellSize, s.SZ * Scale * map.CellHeight);
            solid.Color = AcColor.FromRgb((byte)(s.Rgb >> 16), (byte)(s.Rgb >> 8), (byte)s.Rgb);
            _parts[i] = solid;
            _applied[i] = Matrix3d.Identity;
        }
    }

    /// <summary>Moves the figure to the player and animates it. <paramref name="walkDistance"/> is how far it walked this frame (blocks).</summary>
    public void Update(Player player, double walkDistance, bool visible)
    {
        var tm = TransientManager.CurrentTransientManager;
        if (!visible)
        {
            Hide();
            return;
        }

        bool moving = walkDistance > 1e-4 && (player.OnGround || player.InWater);
        _phase += walkDistance * 3.2;
        double swing = moving ? Math.Sin(_phase) * 0.75 : 0;

        var (fx, fy, fz) = _map.ToDrawing(player.X, player.Y, player.Z);
        Matrix3d body = Matrix3d.Displacement(new Vector3d(fx, fy, fz))
                      * Matrix3d.Rotation(-player.Yaw, Vector3d.ZAxis, Point3d.Origin);

        for (int i = 0; i < Specs.Length; i++)
        {
            var s = Specs[i];
            double angle = s.Swing switch
            {
                Swing.Positive => swing,
                Swing.Negative => -swing,
                Swing.Head => player.Pitch * 0.8,
                _ => 0,
            };

            Matrix3d world = body
                * Matrix3d.Displacement(ToDrawingVector(s.Pivot.GetAsVector()))
                * Matrix3d.Rotation(angle, Vector3d.XAxis, Point3d.Origin)
                * Matrix3d.Displacement(ToDrawingVector(s.Offset));

            // Solids can only be moved relatively, so apply the change since the last frame.
            _parts[i].TransformBy(world * _applied[i].Inverse());
            _applied[i] = world;

            if (_shown) tm.UpdateTransient(_parts[i], new IntegerCollection());
            else tm.AddTransient(_parts[i], TransientDrawingMode.Main, 128, new IntegerCollection());
        }
        _shown = true;
    }

    private Vector3d ToDrawingVector(Vector3d v) =>
        new(v.X * Scale * _map.CellSize, v.Y * Scale * _map.CellSize, v.Z * Scale * _map.CellHeight);

    public void Hide()
    {
        if (!_shown) return;
        var tm = TransientManager.CurrentTransientManager;
        foreach (var part in _parts)
            tm.EraseTransient(part, new IntegerCollection());
        _shown = false;
    }

    public void Dispose()
    {
        Hide();
        foreach (var part in _parts)
            part.Dispose();
    }
}
