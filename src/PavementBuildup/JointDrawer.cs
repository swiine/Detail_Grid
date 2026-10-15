using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;
using PavementBuildup.Core.Design;

namespace PavementBuildup;

internal static class JointDrawer
{
    public static List<List<Pt>> Loops(Transaction tr, ObjectId id)
    {
        var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
        return ent switch
        {
            Hatch hatch => HatchLoops(hatch),
            Curve curve when curve.Closed || curve is Circle || curve.StartPoint.DistanceTo(curve.EndPoint) < 1e-6 => new List<List<Pt>> { CurvePoints(curve) },
            _ => new List<List<Pt>>(),
        };
    }

    private static List<Pt> CurvePoints(Curve curve)
    {
        var pts = new List<Pt>();
        switch (curve)
        {
            case Polyline pl:
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint3dAt(i);
                    pts.Add(new Pt(p.X, p.Y));
                    double bulge = pl.GetBulgeAt(i);
                    if (Math.Abs(bulge) > 1e-9 && (i < pl.NumberOfVertices - 1 || pl.Closed))
                    {
                        int n = ArcSteps(bulge);
                        for (int k = 1; k < n; k++)
                        {
                            var q = pl.GetPointAtParameter(i + k / (double)n);
                            pts.Add(new Pt(q.X, q.Y));
                        }
                    }
                }
                break;
            case Circle c:
                for (int k = 0; k < 72; k++)
                {
                    double a = 2 * Math.PI * k / 72;
                    pts.Add(new Pt(c.Center.X + c.Radius * Math.Cos(a), c.Center.Y + c.Radius * Math.Sin(a)));
                }
                break;
            default:
                double start = curve.StartParam, end = curve.EndParam;
                for (int k = 0; k < 200; k++)
                {
                    var q = curve.GetPointAtParameter(start + (end - start) * k / 200);
                    pts.Add(new Pt(q.X, q.Y));
                }
                break;
        }
        return pts;
    }

    private static List<List<Pt>> HatchLoops(Hatch hatch)
    {
        var loops = new List<List<Pt>>();
        for (int i = 0; i < hatch.NumberOfLoops; i++)
        {
            var loop = hatch.GetLoopAt(i);
            var pts = new List<Pt>();
            if (loop.IsPolyline)
            {
                var v = loop.Polyline;
                for (int j = 0; j < v.Count; j++)
                {
                    var a = v[j].Vertex;
                    pts.Add(new Pt(a.X, a.Y));
                    if (Math.Abs(v[j].Bulge) > 1e-9)
                    {
                        var b = v[(j + 1) % v.Count].Vertex;
                        pts.AddRange(Arc(new Pt(a.X, a.Y), new Pt(b.X, b.Y), v[j].Bulge));
                    }
                }
            }
            else
            {
                foreach (Curve2d edge in loop.Curves)
                {
                    if (edge is LineSegment2d line) pts.Add(new Pt(line.StartPoint.X, line.StartPoint.Y));
                    else
                        foreach (var p in edge.GetSamplePoints(48).Take(47)) pts.Add(new Pt(p.X, p.Y));
                }
            }
            if (pts.Count >= 3) loops.Add(pts);
        }
        return loops;
    }

    private static IEnumerable<Pt> Arc(Pt a, Pt b, double bulge)
    {
        double theta = 4 * Math.Atan(bulge);
        double chord = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        double r = chord / (2 * Math.Sin(theta / 2));
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        double h = r * Math.Cos(theta / 2);
        double nx = -(b.Y - a.Y) / chord, ny = (b.X - a.X) / chord;
        double cx = mx + nx * h, cy = my + ny * h;
        double start = Math.Atan2(a.Y - cy, a.X - cx);
        int n = ArcSteps(bulge);
        for (int k = 1; k < n; k++)
        {
            double t = start + theta * k / n;
            yield return new Pt(cx + Math.Abs(r) * Math.Cos(t), cy + Math.Abs(r) * Math.Sin(t));
        }
    }

    private static int ArcSteps(double bulge) => Math.Max(8, (int)Math.Ceiling(Math.Abs(4 * Math.Atan(bulge)) * 180 / Math.PI / 3));

    public static double Area(IReadOnlyList<Pt> loop)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i];
            var q = loop[(i + 1) % loop.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a / 2);
    }

    public static (int Joints, int Marks) Draw(Database db, Transaction tr, SlabJointResult result, CadStandard standard, double unitsPerMetre,
        double planScale, Point3d? notePoint, string noteText)
    {
        var outline = standard.Layer(DetailElement.Outline).Name;
        var jointLayer = EnsureLayer(db, tr, outline + "-JOINT", 1, DashedLinetype(db, tr));
        var checkLayer = EnsureLayer(db, tr, outline + "-JOINT-CHECK", 2, db.ContinuousLinetype);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        double paper = unitsPerMetre / 1000 * planScale;

        foreach (var j in result.Joints)
        {
            var line = new Line(new Point3d(j.A.X, j.A.Y, 0), new Point3d(j.B.X, j.B.Y, 0)) { LayerId = jointLayer };
            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }
        foreach (var l in result.LongJoints ?? Array.Empty<IReadOnlyList<Pt>>())
        {
            if (l.Count < 2) continue;
            var pl = new Polyline { LayerId = jointLayer, Plinegen = true };
            for (int i = 0; i < l.Count; i++) pl.AddVertexAt(i, new Point2d(l[i].X, l[i].Y), 0, 0, 0);
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }
        foreach (var c in result.CheckPoints)
        {
            var circle = new Circle(new Point3d(c.X, c.Y, 0), Vector3d.ZAxis, 4 * paper) { LayerId = checkLayer };
            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }
        if (notePoint is { } at)
        {
            var text = new MText
            {
                Location = at, Contents = noteText.Replace("\n", "\\P"), TextHeight = standard.TextHeightMm * paper,
                Attachment = AttachmentPoint.TopLeft, LayerId = EnsureLayer(db, tr, standard.Layer(DetailElement.Text).Name, 7, db.ContinuousLinetype),
            };
            var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (!string.IsNullOrWhiteSpace(standard.TextStyle) && styles.Has(standard.TextStyle)) text.TextStyleId = styles[standard.TextStyle];
            ms.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }
        return (result.Joints.Count + (result.LongJoints?.Count ?? 0), result.CheckPoints.Count);
    }

    public static string Note(SlabJointResult r, JointRuleSet rules, double thicknessMm, SlabKind kind) =>
        $"SAWCUT CONTRACTION JOINTS (DASHED) {r.SawcutDepthMm}mm DEEP, MAX {r.SpacingM:0.0#}m CENTRES\n"
        + $"{thicknessMm:0}mm {JointLayout.Describe(kind).ToUpperInvariant()} SLAB - {rules.Name.ToUpperInvariant()}\n"
        + "ISOLATION JOINT AROUND SLAB EDGE, PITS AND COLUMNS\n"
        + (r.Strips > 1 ? $"LENGTHWAYS JOINTS: {r.Strips} EQUAL STRIPS ACROSS THE SLAB\n" : "")
        + (r.CheckPoints.Count > 0 ? "TRIMMER BARS AT CIRCLED LOCATIONS\n" : "")
        + "SAWCUT WITHIN 6-18 HOURS OF PLACING";

    private static ObjectId DashedLinetype(Database db, Transaction tr)
    {
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (!table.Has("DASHED"))
        {
            try { db.LoadLineTypeFile("DASHED", db.Measurement == MeasurementValue.Metric ? "acadiso.lin" : "acad.lin"); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return db.ContinuousLinetype; }
        }
        return table.Has("DASHED") ? table["DASHED"] : db.ContinuousLinetype;
    }

    private static ObjectId EnsureLayer(Database db, Transaction tr, string name, short color, ObjectId linetype)
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (table.Has(name)) return table[name];
        var layer = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color), LinetypeObjectId = linetype };
        if (!table.IsWriteEnabled) table.UpgradeOpen();
        var id = table.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }
}
