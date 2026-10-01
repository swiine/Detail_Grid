using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CivDoom.Engine;

namespace CivDoom.Civil3D;

/// <summary>
/// The drawing conventions for levels: DOOM-* layers, and marker blocks whose names say what they are.
/// <list type="bullet">
/// <item>DOOM-START: where you spawn; its rotation is the direction you face; its scale is the wall height.</item>
/// <item>DOOM-MONSTER (random) or DOOM-MONSTER-&lt;id&gt; (e.g. DOOM-MONSTER-IMP).</item>
/// <item>DOOM-WEAPON (any) or DOOM-WEAPON-&lt;id&gt; (e.g. DOOM-WEAPON-SHOTGUN).</item>
/// <item>DOOM-HEALTH, DOOM-AMMO.</item>
/// </list>
/// Markers are ordinary blocks: move, copy, rotate, erase or redefine them with normal drafting commands.
/// </summary>
internal static class DoomBlocks
{
    public const string WallsLayer = "DOOM-WALLS";
    public const string MonstersLayer = "DOOM-MONSTERS";
    public const string ItemsLayer = "DOOM-ITEMS";
    public const string StartLayer = "DOOM-START";

    /// <summary>Any linework on this layer is a locked gate rather than a wall.</summary>
    public const string GateLayer = "DOOM-GATE";

    public const string Start = "DOOM-START";
    public const string Monster = "DOOM-MONSTER";
    public const string Weapon = "DOOM-WEAPON";
    public const string Health = "DOOM-HEALTH";
    public const string Ammo = "DOOM-AMMO";
    public const string Exit = "DOOM-EXIT";
    public const string ExitFinal = "DOOM-EXIT-FINAL";
    public const string Boss = "DOOM-BOSS";
    public const string Theme = "DOOM-THEME";

    /// <summary>What a marker block means.</summary>
    public abstract record Marker;
    public sealed record StartMarker : Marker;
    public sealed record MonsterMarker(string? Id) : Marker;
    public sealed record PickupMarker(PickupKind Kind, string? WeaponId) : Marker;
    public sealed record ExitMarker(GateRule Rule) : Marker;
    public sealed record ThemeMarker(string Id) : Marker;

    /// <summary>Interprets a block name, or returns null if it isn't a DOOM marker.</summary>
    public static Marker? Parse(string blockName)
    {
        string n = blockName.ToUpperInvariant();
        if (n == Start) return new StartMarker();
        if (n == Health) return new PickupMarker(PickupKind.Health, null);
        if (n == Ammo) return new PickupMarker(PickupKind.Ammo, null);
        if (n == Exit) return new ExitMarker(GateRule.AllBosses);
        if (n == ExitFinal) return new ExitMarker(GateRule.FinalBoss);
        if (n == Boss) return new MonsterMarker(MonsterSet.RandomBoss);
        if (n.StartsWith(Theme + "-")) return new ThemeMarker(n[(Theme.Length + 1)..].ToLowerInvariant());
        if (n == Monster) return new MonsterMarker(null);
        if (n.StartsWith(Monster + "-")) return new MonsterMarker(n[(Monster.Length + 1)..].ToLowerInvariant());
        if (n == Weapon) return new PickupMarker(PickupKind.Weapon, null);
        if (n.StartsWith(Weapon + "-")) return new PickupMarker(PickupKind.Weapon, n[(Weapon.Length + 1)..].ToLowerInvariant());
        return null;
    }

    public static string ThemeBlock(string id) => $"{Theme}-{id.ToUpperInvariant()}";

    public static string MonsterBlock(string? id) => id == null ? Monster : $"{Monster}-{id.ToUpperInvariant()}";
    public static string WeaponBlock(string? id) => id == null ? Weapon : $"{Weapon}-{id.ToUpperInvariant()}";

    public static ObjectId EnsureLayer(Transaction tr, Database db, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, aci) };
        ObjectId id = lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        return id;
    }

    /// <summary>
    /// Makes sure the marker block exists (never overwrites one you've redefined). Symbols are drawn at unit
    /// size on layer 0 with ByBlock colour, so insert them scaled by the wall height on a DOOM layer.
    /// </summary>
    public static ObjectId EnsureBlock(Transaction tr, Database db, string name, string label)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(name)) return bt[name];
        bt.UpgradeOpen();
        var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
        ObjectId id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        foreach (Entity e in Symbol(name, label))
        {
            e.SetDatabaseDefaults(db);
            e.Layer = "0";
            e.Color = Color.FromColorIndex(ColorMethod.ByBlock, 0);
            btr.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
        }
        return id;
    }

    private static IEnumerable<Entity> Symbol(string name, string label)
    {
        if (name == Start)
        {
            // A circle with an arrow pointing along +X (the facing direction).
            yield return new Circle(Point3d.Origin, Vector3d.ZAxis, 0.15);
            var arrow = new Polyline();
            arrow.AddVertexAt(0, new Point2d(-0.1, 0), 0, 0, 0);
            arrow.AddVertexAt(1, new Point2d(0.3, 0), 0, 0, 0);
            arrow.AddVertexAt(2, new Point2d(0.2, 0.07), 0, 0, 0);
            arrow.AddVertexAt(3, new Point2d(0.3, 0), 0, 0, 0);
            arrow.AddVertexAt(4, new Point2d(0.2, -0.07), 0, 0, 0);
            yield return arrow;
        }
        else if (name.StartsWith(Theme))
        {
            // A little skyline in a frame.
            var frame = new Polyline { Closed = true };
            frame.AddVertexAt(0, new Point2d(-0.3, -0.15), 0, 0, 0);
            frame.AddVertexAt(1, new Point2d(0.3, -0.15), 0, 0, 0);
            frame.AddVertexAt(2, new Point2d(0.3, 0.25), 0, 0, 0);
            frame.AddVertexAt(3, new Point2d(-0.3, 0.25), 0, 0, 0);
            yield return frame;
            var sky = new Polyline();
            double[] xs = { -0.3, -0.22, -0.22, -0.12, -0.12, -0.02, -0.02, 0.1, 0.1, 0.2, 0.2, 0.3 };
            double[] ys = { 0.0, 0.0, 0.15, 0.15, 0.05, 0.05, 0.2, 0.2, 0.08, 0.08, 0.12, 0.12 };
            for (int i = 0; i < xs.Length; i++) sky.AddVertexAt(i, new Point2d(xs[i], ys[i] - 0.12), 0, 0, 0);
            yield return sky;
        }
        else if (name.StartsWith(Exit))
        {
            // A chequered square with a flag.
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    if ((i + j) % 2 == 1) continue;
                    var sq = new Solid(new Point3d(-0.2 + i * 0.1, -0.2 + j * 0.1, 0), new Point3d(-0.1 + i * 0.1, -0.2 + j * 0.1, 0),
                                       new Point3d(-0.2 + i * 0.1, -0.1 + j * 0.1, 0), new Point3d(-0.1 + i * 0.1, -0.1 + j * 0.1, 0));
                    yield return sq;
                }
            var border = new Polyline { Closed = true };
            border.AddVertexAt(0, new Point2d(-0.2, -0.2), 0, 0, 0);
            border.AddVertexAt(1, new Point2d(0.2, -0.2), 0, 0, 0);
            border.AddVertexAt(2, new Point2d(0.2, 0.2), 0, 0, 0);
            border.AddVertexAt(3, new Point2d(-0.2, 0.2), 0, 0, 0);
            yield return border;
        }
        else if (name.StartsWith(Monster) || name == Boss)
        {
            yield return new Circle(Point3d.Origin, Vector3d.ZAxis, 0.18);
            var x = new Polyline();
            x.AddVertexAt(0, new Point2d(-0.1, -0.1), 0, 0, 0);
            x.AddVertexAt(1, new Point2d(0.1, 0.1), 0, 0, 0);
            yield return x;
            var x2 = new Polyline();
            x2.AddVertexAt(0, new Point2d(-0.1, 0.1), 0, 0, 0);
            x2.AddVertexAt(1, new Point2d(0.1, -0.1), 0, 0, 0);
            yield return x2;
        }
        else
        {
            // Items and weapons: a square.
            var box = new Polyline { Closed = true };
            box.AddVertexAt(0, new Point2d(-0.12, -0.12), 0, 0, 0);
            box.AddVertexAt(1, new Point2d(0.12, -0.12), 0, 0, 0);
            box.AddVertexAt(2, new Point2d(0.12, 0.12), 0, 0, 0);
            box.AddVertexAt(3, new Point2d(-0.12, 0.12), 0, 0, 0);
            yield return box;
        }

        yield return new DBText
        {
            Position = new Point3d(-0.2, -0.36, 0),
            Height = 0.1,
            TextString = label,
        };
    }

    public static short LayerColor(string layer) => layer switch
    {
        WallsLayer => 8,
        MonstersLayer => 1,
        ItemsLayer => 2,
        StartLayer => 3,
        GateLayer => 40,
        _ => 7,
    };
}
