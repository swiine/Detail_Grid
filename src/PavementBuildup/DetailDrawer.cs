using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>Turns a <see cref="DetailGeometry"/> into AutoCAD entities.</summary>
internal sealed class DetailDrawer
{
    private readonly Database _db;
    private readonly Transaction _tr;
    private readonly DetailSettings _settings;

    private ObjectId _outlineLayer, _hatchLayer, _textLayer, _dimLayer;

    public DetailDrawer(Database db, Transaction tr, DetailSettings settings)
    {
        _db = db;
        _tr = tr;
        _settings = settings;
    }

    /// <summary>Drawing units per mm, from the settings or INSUNITS.</summary>
    public static double ResolveUnitsPerMm(Database db, DetailSettings settings)
    {
        if (!string.Equals(settings.DrawingUnits, "AUTO", StringComparison.OrdinalIgnoreCase))
            return DetailLayout.UnitsPerMm(settings.DrawingUnits);

        return db.Insunits switch
        {
            UnitsValue.Centimeters => 0.1,
            UnitsValue.Meters => 0.001,
            UnitsValue.Inches => 1 / 25.4,
            UnitsValue.Feet => 1 / 304.8,
            _ => 1.0, // millimetres or unitless
        };
    }

    /// <summary>
    /// Draws the detail. <paramref name="placement"/> maps local detail coordinates
    /// (top-left of the surface at the origin) to WCS. Returns the block reference or null.
    /// </summary>
    public ObjectId Draw(Buildup buildup, DetailGeometry g, Matrix3d placement)
    {
        EnsureLayers();
        var entities = CreateEntities(g);
        var space = (BlockTableRecord)_tr.GetObject(_db.CurrentSpaceId, OpenMode.ForWrite);

        if (!_settings.CreateBlock)
        {
            foreach (var e in entities)
            {
                Append(space, e);
                e.TransformBy(placement);
                if (e is Hatch h)
                    h.EvaluateHatch(true);
            }
            return ObjectId.Null;
        }

        var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);
        var def = new BlockTableRecord { Name = UniqueBlockName(bt, buildup.Name), Origin = Point3d.Origin };
        var defId = bt.Add(def);
        _tr.AddNewlyCreatedDBObject(def, true);
        foreach (var e in entities)
            Append(def, e);

        var reference = new BlockReference(Point3d.Origin, defId) { Layer = "0" };
        reference.TransformBy(placement);
        return Append(space, reference);
    }

    private ObjectId Append(BlockTableRecord owner, Entity e)
    {
        var id = owner.AppendEntity(e);
        _tr.AddNewlyCreatedDBObject(e, true);
        if (e is Hatch h)
            FinishHatch(h);
        return id;
    }

    // Hatch geometry: kept as pending data until the hatch is database-resident.
    private readonly Dictionary<Hatch, (Band Band, double Width)> _pendingHatches = new();

    private List<Entity> CreateEntities(DetailGeometry g)
    {
        var list = new List<Entity>();

        foreach (var band in g.Bands)
            if (band.Hatch is not null)
                list.Add(NewHatch(band, g.Width));
        if (g.Subgrade is { Hatch: not null } sub)
            list.Add(NewHatch(sub, g.Width));

        foreach (double level in g.InterfaceLevels)
            list.Add(new Line(P(0, level), P(g.Width, level)) { LayerId = _outlineLayer });

        foreach (double level in g.MembraneLevels)
        {
            // Membranes: a heavier line so they read as a separate element.
            var pl = new Polyline { LayerId = _outlineLayer, ColorIndex = 1 };
            pl.AddVertexAt(0, new Point2d(0, level), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(g.Width, level), 0, 0, 0);
            pl.ConstantWidth = 0.35 * g.AnnotationScale;
            list.Add(pl);
        }

        foreach (var edge in g.Edges)
        {
            var pl = new Polyline { LayerId = _outlineLayer };
            for (int i = 0; i < edge.Count; i++)
                pl.AddVertexAt(i, new Point2d(edge[i].X, edge[i].Y), 0, 0, 0);
            list.Add(pl);
        }

        foreach (var label in g.Labels)
        {
            var leader = new Polyline { LayerId = _textLayer };
            leader.AddVertexAt(0, new Point2d(label.Anchor.X, label.Anchor.Y), 0, 0, 0);
            leader.AddVertexAt(1, new Point2d(label.Elbow.X, label.Elbow.Y), 0, 0, 0);
            leader.AddVertexAt(2, new Point2d(label.TextAt.X - g.TextHeight * 0.5, label.TextAt.Y), 0, 0, 0);
            list.Add(leader);
            list.Add(Dot(label.Anchor, 0.6 * g.AnnotationScale));
            list.Add(Text(label.Text, label.TextAt, g.TextHeight, underline: false));
        }

        foreach (var dim in g.Dimensions)
        {
            var d = new RotatedDimension(Math.PI / 2, P(0, dim.Top), P(0, dim.Bottom), P(dim.X, (dim.Top + dim.Bottom) / 2), dim.Text, _db.Dimstyle)
            {
                LayerId = _dimLayer,
            };
            d.Dimscale = g.AnnotationScale;
            d.Dimtxt = _settings.TextHeightPaperMm;
            list.Add(d);
        }

        foreach (var t in g.Titles)
            list.Add(Text(t.Text, t.At, t.Height, t.Underline));

        return list;
    }

    private Hatch NewHatch(Band band, double width)
    {
        var h = new Hatch { LayerId = _hatchLayer };
        _pendingHatches[h] = (band, width);
        return h;
    }

    /// <summary>Pattern, scale and boundary can only be set once the hatch is in the database.</summary>
    private void FinishHatch(Hatch h)
    {
        if (!_pendingHatches.Remove(h, out var pending))
            return;

        var (band, width) = pending;
        var spec = band.Hatch!;
        h.SetDatabaseDefaults();
        h.LayerId = _hatchLayer;
        h.Associative = false;
        if (string.Equals(spec.Pattern, "SOLID", StringComparison.OrdinalIgnoreCase))
        {
            h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        }
        else
        {
            h.PatternScale = band.HatchScale > 0 ? band.HatchScale : 1;
            h.PatternAngle = spec.AngleDeg * Math.PI / 180;
            try
            {
                h.SetHatchPattern(HatchPatternType.PreDefined, spec.Pattern);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Unknown pattern name: fall back rather than abort the whole detail.
                h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
            }
        }

        var loop = new HatchLoop(HatchLoopTypes.Polyline);
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Top), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(width, band.Top), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(width, band.Bottom), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Bottom), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Top), 0));
        h.AppendLoop(loop);
        h.EvaluateHatch(true);
    }

    private MText Text(string text, Pt at, double height, bool underline) => new()
    {
        Contents = underline ? @"\L" + Escape(text) + @"\l" : Escape(text),
        Location = P(at.X, at.Y),
        TextHeight = height,
        Attachment = AttachmentPoint.MiddleLeft,
        TextStyleId = _db.Textstyle,
        LayerId = _textLayer,
    };

    private static string Escape(string s) => s.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}");

    /// <summary>A filled dot (zero-inner-radius donut) marking the leader anchor.</summary>
    private Polyline Dot(Pt c, double diameter)
    {
        double r = diameter / 4;
        var pl = new Polyline { LayerId = _textLayer, Closed = true };
        pl.AddVertexAt(0, new Point2d(c.X - r, c.Y), 1, diameter / 2, diameter / 2);
        pl.AddVertexAt(1, new Point2d(c.X + r, c.Y), 1, diameter / 2, diameter / 2);
        return pl;
    }

    private static Point3d P(double x, double y) => new(x, y, 0);

    private void EnsureLayers()
    {
        string p = _settings.LayerPrefix ?? "";
        _outlineLayer = EnsureLayer(p + "OUTLINE", 7, LineWeight.LineWeight035);
        _hatchLayer = EnsureLayer(p + "HATCH", 8, LineWeight.LineWeight013);
        _textLayer = EnsureLayer(p + "TEXT", 2, LineWeight.LineWeight018);
        _dimLayer = EnsureLayer(p + "DIM", 3, LineWeight.LineWeight018);
    }

    private ObjectId EnsureLayer(string name, short color, LineWeight weight)
    {
        var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name))
            return lt[name];

        lt.UpgradeOpen();
        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color), LineWeight = weight };
        var id = lt.Add(layer);
        _tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }

    private static string UniqueBlockName(BlockTable bt, string name)
    {
        var invalid = "<>/\\\":;?*|,='`".ToCharArray();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (clean.Length == 0)
            clean = "BUILDUP";
        string baseName = "PAV_" + clean;
        string candidate = baseName;
        for (int i = 2; bt.Has(candidate); i++)
            candidate = $"{baseName}_{i}";
        return candidate;
    }
}
