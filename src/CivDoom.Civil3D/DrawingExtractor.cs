using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CivDoom.Engine;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace CivDoom.Civil3D;

/// <summary>
/// Turns drawing entities into wall segments and enemy spawn points.
/// Anything that is a curve (lines, polylines, arcs, circles, splines, ...) becomes a wall. Blocks and
/// common Civil 3D objects (alignments, feature lines, parcels, survey figures) are exploded in memory
/// (the drawing is never modified). POINT and COGO point entities become enemy spawns.
/// </summary>
internal sealed class DrawingExtractor
{
    private const int MaxDepth = 6;
    private const int DefaultColor = 0xB4B4B4;

    // Civil 3D objects worth exploding into linework. Matched against the entity's DXF class name.
    private static readonly string[] CivilExplodable = { "ALIGNMENT", "FEATURE_LINE", "PARCEL", "SURVEY_FIGURE" };

    private readonly Transaction _tr;
    private readonly double _sampleLength;
    private readonly bool _visibleLayersOnly;
    private readonly Dictionary<ObjectId, (bool Visible, int Color)> _layers = new();

    public DrawingExtractor(Transaction tr, double wallHeight, bool visibleLayersOnly)
    {
        _tr = tr;
        _sampleLength = wallHeight * 0.25;
        _visibleLayersOnly = visibleLayersOnly;
    }

    public DrawingGeometry Geometry { get; } = new();
    public int SkippedEntities { get; private set; }

    public void AddObjects(IEnumerable<ObjectId> ids)
    {
        foreach (ObjectId id in ids)
        {
            if (Geometry.Segments.Count >= LevelBuilder.MaxWalls) return;
            if (id.IsErased || _tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
            AddEntity(ent, DefaultColor, 0);
        }
    }

    private void AddEntity(Entity ent, int byBlockColor, int depth)
    {
        try
        {
            if (!ent.Visible) return;
            (bool layerVisible, int layerColor) = Layer(ent.LayerId);
            if (_visibleLayersOnly && !layerVisible) return;
            int color = ResolveColor(ent.Color, layerColor, byBlockColor);

            switch (ent)
            {
                case DBPoint pt:
                    Geometry.EnemyPoints.Add(Flat(pt.Position));
                    return;
                case Curve curve:
                    AddCurve(curve, color);
                    return;
                case BlockReference br when DoomBlocks.Parse(BlockName(br)) is { } marker:
                    AddMarker(br, marker);
                    return;
                case BlockReference br:
                    ExplodeInto(br, color, depth);
                    return;
            }

            string dxf = ent.GetRXClass().DxfName ?? string.Empty;
            if (dxf.Contains("COGO_POINT", StringComparison.OrdinalIgnoreCase))
            {
                // COGO points: no Civil 3D API reference needed, the extents centre is the point.
                Extents3d ext = ent.GeometricExtents;
                Geometry.EnemyPoints.Add(Flat(ext.MinPoint + (ext.MaxPoint - ext.MinPoint) / 2));
                return;
            }

            if (dxf.StartsWith("AECC", StringComparison.OrdinalIgnoreCase)
                && !dxf.Contains("LABEL", StringComparison.OrdinalIgnoreCase)
                && CivilExplodable.Any(n => dxf.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                ExplodeInto(ent, color, depth);
                return;
            }

            SkippedEntities++;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Proxy objects, degenerate geometry, non-uniformly scaled blocks... just skip them.
            SkippedEntities++;
        }
    }

    /// <summary>Counts of DOOM marker blocks found, for the summary message.</summary>
    public int Markers { get; private set; }

    private void AddMarker(BlockReference br, DoomBlocks.Marker marker)
    {
        Markers++;
        Vec2 at = Flat(br.Position);
        switch (marker)
        {
            case DoomBlocks.StartMarker:
                Geometry.PlayerStart = at;
                Geometry.PlayerAngle = br.Rotation;
                double scale = Math.Abs(br.ScaleFactors.X);
                if (scale > 1e-9) Geometry.SuggestedWallHeight = scale;
                break;
            case DoomBlocks.MonsterMarker m:
                Geometry.Monsters.Add(new EnemySpawn(at, m.Id));
                break;
            case DoomBlocks.PickupMarker p:
                Geometry.Pickups.Add(new PickupSpawn(at, p.Kind, p.WeaponId));
                break;
        }
    }

    private string BlockName(BlockReference br)
    {
        ObjectId id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return id.IsNull ? br.Name : ((BlockTableRecord)_tr.GetObject(id, OpenMode.ForRead)).Name;
    }

    private void ExplodeInto(Entity ent, int color, int depth)
    {
        if (depth >= MaxDepth) return;
        using var parts = new DBObjectCollection();
        ent.Explode(parts);
        try
        {
            foreach (DBObject obj in parts)
            {
                if (obj is Entity child) AddEntity(child, color, depth + 1);
            }
        }
        finally
        {
            foreach (DBObject obj in parts) obj.Dispose();
        }
    }

    private void AddCurve(Curve c, int color)
    {
        switch (c)
        {
            case Line line:
                AddSegment(line.StartPoint, line.EndPoint, color);
                return;

            case Polyline pl:
            {
                int segments = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
                for (int i = 0; i < segments; i++)
                {
                    if (pl.GetSegmentType(i) == SegmentType.Arc)
                    {
                        CircularArc3d arc = pl.GetArcSegmentAt(i);
                        int pieces = PiecesFor(arc.Radius * Math.Abs(arc.EndAngle - arc.StartAngle), 8);
                        AddSampled(t => pl.GetPointAtParameter(i + t), pieces, color);
                    }
                    else if (pl.GetSegmentType(i) == SegmentType.Line)
                    {
                        AddSegment(pl.GetPoint3dAt(i), pl.GetPoint3dAt((i + 1) % pl.NumberOfVertices), color);
                    }
                }
                return;
            }

            case Polyline2d or Polyline3d:
            {
                // Parameters are vertex indices, so stepping through integers hits every corner exactly.
                int start = (int)Math.Round(c.StartParam), end = (int)Math.Round(c.EndParam);
                for (int i = start; i < end; i++)
                {
                    int k = i;
                    AddSampled(t => c.GetPointAtParameter(k + t), 4, color);
                }
                return;
            }

            default:
            {
                double length = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
                if (length <= 0) return;
                int pieces = PiecesFor(length, 12);
                AddSampled(t => c.GetPointAtDist(t * length), pieces, color);
                return;
            }
        }
    }

    private int PiecesFor(double length, int min) => Math.Clamp((int)Math.Ceiling(length / _sampleLength), min, 256);

    private void AddSampled(Func<double, Point3d> at, int pieces, int color)
    {
        Point3d prev = at(0);
        for (int i = 1; i <= pieces; i++)
        {
            Point3d next = at((double)i / pieces);
            AddSegment(prev, next, color);
            prev = next;
        }
    }

    private void AddSegment(Point3d a, Point3d b, int color)
    {
        if (Geometry.Segments.Count >= LevelBuilder.MaxWalls) return;
        Geometry.Segments.Add(new DrawingSegment(Flat(a), Flat(b), color));
    }

    private static Vec2 Flat(Point3d p) => new(p.X, p.Y);

    private (bool Visible, int Color) Layer(ObjectId layerId)
    {
        if (_layers.TryGetValue(layerId, out var cached)) return cached;
        var result = (true, DefaultColor);
        if (!layerId.IsNull && _tr.GetObject(layerId, OpenMode.ForRead) is LayerTableRecord ltr)
        {
            result = (!ltr.IsOff && !ltr.IsFrozen, ToRgb(ltr.Color, DefaultColor));
        }
        _layers[layerId] = result;
        return result;
    }

    private static int ResolveColor(AcColor color, int layerColor, int byBlockColor)
    {
        if (color.IsByLayer) return layerColor;
        if (color.IsByBlock) return byBlockColor;
        return ToRgb(color, DefaultColor);
    }

    private static int ToRgb(AcColor color, int fallback)
    {
        try
        {
            System.Drawing.Color c = color.ColorValue;
            int r = c.R, g = c.G, b = c.B;
            // Very dark colours (e.g. ACI 250) would vanish against the sky; lift them.
            int lum = (r * 3 + g * 6 + b) / 10;
            if (lum < 70)
            {
                r = (r + 110) / 2 + 20; g = (g + 110) / 2 + 20; b = (b + 110) / 2 + 20;
            }
            return (r << 16) | (g << 8) | b;
        }
        catch (System.Exception)
        {
            return fallback;
        }
    }
}
