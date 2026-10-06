using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;

namespace PavementBuildup;

/// <summary>Turns a <see cref="DetailGeometry"/> into AutoCAD entities following a <see cref="CadStandard"/>.</summary>
internal sealed class DetailDrawer
{
    private readonly Database _db;
    private readonly Transaction _tr;
    private readonly DetailSettings _settings;
    private readonly CadStandard _standard;

    private readonly Dictionary<DetailElement, ObjectId> _layers = new();
    private ObjectId _textStyle, _dimStyle;
    private bool _namedDimStyle;

    /// <summary>Things that did not match the standard (missing styles, patterns, linetypes). Shown to the user.</summary>
    public List<string> Warnings { get; } = new();

    public DetailDrawer(Database db, Transaction tr, DetailSettings settings, CadStandard standard)
    {
        _db = db;
        _tr = tr;
        _settings = settings;
        _standard = standard;
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
        SetUpLayersAndStyles();
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

    // Hatch settings can only be applied once the hatch is database-resident.
    private readonly Dictionary<Hatch, (Band Band, double Width)> _pendingHatches = new();

    private List<Entity> CreateEntities(DetailGeometry g)
    {
        var list = new List<Entity>();
        var outline = _layers[DetailElement.Outline];
        var leaderLayer = _layers[DetailElement.Leader];

        foreach (var band in g.Bands)
            if (band.Hatch is not null)
                list.Add(NewHatch(band, g.Width));
        if (g.Subgrade is { Hatch: not null } sub)
            list.Add(NewHatch(sub, g.Width));

        foreach (double level in g.InterfaceLevels)
            list.Add(new Line(P(0, level), P(g.Width, level)) { LayerId = outline });

        foreach (double level in g.MembraneLevels)
        {
            var pl = new Polyline { LayerId = _layers[DetailElement.Membrane] };
            pl.AddVertexAt(0, new Point2d(0, level), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(g.Width, level), 0, 0, 0);
            pl.ConstantWidth = Math.Max(0, _standard.MembraneWidthMm) * g.AnnotationScale;
            list.Add(pl);
        }

        foreach (var edge in g.Edges)
        {
            var pl = new Polyline { LayerId = outline };
            for (int i = 0; i < edge.Count; i++)
                pl.AddVertexAt(i, new Point2d(edge[i].X, edge[i].Y), 0, 0, 0);
            list.Add(pl);
        }

        foreach (var label in g.Labels)
        {
            var leader = new Polyline { LayerId = leaderLayer };
            leader.AddVertexAt(0, new Point2d(label.Anchor.X, label.Anchor.Y), 0, 0, 0);
            leader.AddVertexAt(1, new Point2d(label.Elbow.X, label.Elbow.Y), 0, 0, 0);
            leader.AddVertexAt(2, new Point2d(label.TextAt.X - g.TextHeight * 0.5, label.TextAt.Y), 0, 0, 0);
            list.Add(leader);
            list.Add(Dot(label.Anchor, 0.6 * g.AnnotationScale, leaderLayer));
            list.Add(Text(label.Text, label.TextAt, g.TextHeight, underline: false, _layers[DetailElement.Text]));
        }

        foreach (var dim in g.Dimensions)
        {
            var d = new RotatedDimension(Math.PI / 2, P(0, dim.Top), P(0, dim.Bottom), P(dim.X, (dim.Top + dim.Bottom) / 2), dim.Text, _dimStyle)
            {
                LayerId = _layers[DetailElement.Dimension],
            };
            d.Dimscale = g.AnnotationScale;
            if (!_namedDimStyle)
                d.Dimtxt = _standard.TextHeightMm; // a company dim style keeps its own text height
            list.Add(d);
        }

        foreach (var t in g.Titles)
            list.Add(Text(t.Text, t.At, t.Height, t.Underline, _layers[DetailElement.Title]));

        return list;
    }

    private Hatch NewHatch(Band band, double width)
    {
        var h = new Hatch();
        _pendingHatches[h] = (band, width);
        return h;
    }

    private void FinishHatch(Hatch h)
    {
        if (!_pendingHatches.Remove(h, out var pending))
            return;

        var (band, width) = pending;
        var spec = band.Hatch!;
        h.SetDatabaseDefaults();
        h.LayerId = string.IsNullOrWhiteSpace(spec.Layer) ? _layers[DetailElement.Hatch] : RuleLayer(spec.Layer);
        h.Color = ToColor(spec.Color);
        h.Associative = false;

        if (string.Equals(spec.Pattern, "SOLID", StringComparison.OrdinalIgnoreCase))
        {
            h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        }
        else
        {
            h.PatternScale = band.HatchScale > 0 ? band.HatchScale : 1;
            h.PatternAngle = spec.AngleDeg * Math.PI / 180;
            SetPattern(h, spec.Pattern);
        }

        if (!string.IsNullOrWhiteSpace(spec.BackgroundColor))
            h.BackgroundColor = ToColor(spec.BackgroundColor);

        var loop = new HatchLoop(HatchLoopTypes.Polyline);
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Top), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(width, band.Top), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(width, band.Bottom), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Bottom), 0));
        loop.Polyline.Add(new BulgeVertex(new Point2d(0, band.Top), 0));
        h.AppendLoop(loop);
        h.EvaluateHatch(true);
    }

    /// <summary>
    /// acad(iso).pat patterns first, then a custom &lt;name&gt;.pat on the support path
    /// (how company patterns are normally deployed), then ANSI31 with a warning.
    /// </summary>
    private void SetPattern(Hatch h, string pattern)
    {
        foreach (var type in new[] { HatchPatternType.PreDefined, HatchPatternType.CustomDefined })
        {
            try
            {
                h.SetHatchPattern(type, pattern);
                return;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
            }
        }
        Warn($"Hatch pattern \"{pattern}\" was not found (not in acadiso.pat and no {pattern}.pat on the support path). Used ANSI31.");
        h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
    }

    private MText Text(string text, Pt at, double height, bool underline, ObjectId layer) => new()
    {
        Contents = underline ? @"\L" + Escape(text) + @"\l" : Escape(text),
        Location = P(at.X, at.Y),
        TextHeight = height,
        Attachment = AttachmentPoint.MiddleLeft,
        TextStyleId = _textStyle,
        LayerId = layer,
    };

    private static string Escape(string s) => s.Replace(@"\", @"\\").Replace("{", @"\{").Replace("}", @"\}");

    /// <summary>A filled dot (zero-inner-radius donut) marking the leader anchor.</summary>
    private static Polyline Dot(Pt c, double diameter, ObjectId layer)
    {
        double r = diameter / 4;
        var pl = new Polyline { LayerId = layer, Closed = true };
        pl.AddVertexAt(0, new Point2d(c.X - r, c.Y), 1, diameter / 2, diameter / 2);
        pl.AddVertexAt(1, new Point2d(c.X + r, c.Y), 1, diameter / 2, diameter / 2);
        return pl;
    }

    private static Point3d P(double x, double y) => new(x, y, 0);

    // ------------------------------------------------------------------ layers & styles -----

    private void SetUpLayersAndStyles()
    {
        foreach (var element in Enum.GetValues<DetailElement>())
            _layers[element] = EnsureLayer(_standard.Layer(element));

        _textStyle = _db.Textstyle;
        if (!string.IsNullOrWhiteSpace(_standard.TextStyle))
        {
            var table = (TextStyleTable)_tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(_standard.TextStyle))
                _textStyle = table[_standard.TextStyle];
            else
                Warn($"Text style \"{_standard.TextStyle}\" is not in this drawing. Used the current style.");
        }

        _dimStyle = _db.Dimstyle;
        if (!string.IsNullOrWhiteSpace(_standard.DimensionStyle))
        {
            var table = (DimStyleTable)_tr.GetObject(_db.DimStyleTableId, OpenMode.ForRead);
            if (table.Has(_standard.DimensionStyle))
            {
                _dimStyle = table[_standard.DimensionStyle];
                _namedDimStyle = true;
            }
            else
            {
                Warn($"Dimension style \"{_standard.DimensionStyle}\" is not in this drawing. Used the current style.");
            }
        }
    }

    /// <summary>Layer named by a hatch rule: created with the Hatch element's properties if missing.</summary>
    private ObjectId RuleLayer(string name)
    {
        var style = _standard.Layer(DetailElement.Hatch).Clone();
        style.Name = name;
        return EnsureLayer(style);
    }

    /// <summary>
    /// Returns the layer, creating it from the standard if missing. Existing layers are left
    /// exactly as they are, so a company template's own layer settings always win.
    /// </summary>
    private ObjectId EnsureLayer(LayerStyle style)
    {
        var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(style.Name))
            return lt[style.Name];

        var layer = new LayerTableRecord
        {
            Name = style.Name,
            Color = ToColor(style.Color, fallbackIndex: 7),
            LineWeight = ToLineWeight(style.LineWeightMm),
            LinetypeObjectId = Linetype(style.Linetype),
            IsPlottable = style.Plot,
        };
        lt.UpgradeOpen();
        var id = lt.Add(layer);
        _tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }

    private ObjectId Linetype(string name)
    {
        var table = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
        if (string.IsNullOrWhiteSpace(name) || table.Has(name))
            return string.IsNullOrWhiteSpace(name) ? _db.ContinuousLinetype : table[name];

        string file = _db.Measurement == MeasurementValue.Metric ? "acadiso.lin" : "acad.lin";
        try
        {
            _db.LoadLineTypeFile(name, file);
            if (table.Has(name))
                return table[name];
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
        }
        Warn($"Linetype \"{name}\" is not loaded and is not in {file}. Used Continuous.");
        return _db.ContinuousLinetype;
    }

    private static readonly int[] LineWeights =
        { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };

    /// <summary>Nearest valid AutoCAD lineweight; negative means the default.</summary>
    private static LineWeight ToLineWeight(double mm)
    {
        if (mm < 0 || double.IsNaN(mm))
            return LineWeight.ByLineWeightDefault;
        int target = (int)Math.Round(mm * 100);
        return (LineWeight)LineWeights.MinBy(w => Math.Abs(w - target));
    }

    private static Color ToColor(string text, short fallbackIndex = 256)
    {
        if (!ColorSpec.TryParse(text, out var c))
            return Color.FromColorIndex(ColorMethod.ByAci, fallbackIndex);
        return c.Kind switch
        {
            ColorKind.ByBlock => Color.FromColorIndex(ColorMethod.ByBlock, 0),
            ColorKind.Index => Color.FromColorIndex(ColorMethod.ByAci, c.Index),
            ColorKind.Rgb => Color.FromRgb(c.R, c.G, c.B),
            _ => fallbackIndex == 256
                ? Color.FromColorIndex(ColorMethod.ByLayer, 256)
                : Color.FromColorIndex(ColorMethod.ByAci, fallbackIndex),
        };
    }

    private void Warn(string message)
    {
        if (!Warnings.Contains(message))
            Warnings.Add(message);
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
