using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CivilFifa;

/// <summary>Draws a regulation 105 x 68 m pitch into model space as real drawing objects.</summary>
internal static class Pitch
{
    public const string LineLayer = "FIFA-PITCH";
    public const string GrassLayer = "FIFA-GRASS";

    /// <returns>Ids of everything created, so the pitch can be removed afterwards.</returns>
    public static List<ObjectId> Draw(Database db, Point3d centre)
    {
        var ids = new List<ObjectId>();
        const double L = Match.HalfLength, W = Match.HalfWidth;
        double cx = centre.X, cy = centre.Y;
        Point3d P(double x, double y) => new(cx + x, cy + y, centre.Z);

        using var tr = db.TransactionManager.StartTransaction();
        EnsureLayer(tr, db, LineLayer, Color.FromColorIndex(ColorMethod.ByAci, 7));
        EnsureLayer(tr, db, GrassLayer, Color.FromRgb(46, 125, 50));
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        void Add(Entity e, string layer)
        {
            e.SetDatabaseDefaults(db);
            e.Layer = layer;
            ids.Add(ms.AppendEntity(e));
            tr.AddNewlyCreatedDBObject(e, true);
        }

        // Mowed grass stripes (drawn first so the lines sit on top).
        const int stripes = 14;
        double stripe = 2 * (L + 4) / stripes;
        for (int i = 0; i < stripes; i++)
        {
            double x0 = -L - 4 + i * stripe;
            var hatch = new Hatch();
            Add(hatch, GrassLayer);
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Color = i % 2 == 0 ? Color.FromRgb(46, 125, 50) : Color.FromRgb(67, 150, 71);
            hatch.AppendLoop(HatchLoopTypes.Polyline,
                new Point2dCollection(new[]
                {
                    new Point2d(cx + x0, cy - W - 4), new Point2d(cx + x0 + stripe, cy - W - 4),
                    new Point2d(cx + x0 + stripe, cy + W + 4), new Point2d(cx + x0, cy + W + 4),
                    new Point2d(cx + x0, cy - W - 4),
                }),
                new DoubleCollection(new double[5]));
            hatch.EvaluateHatch(true);
        }

        void Line(double x1, double y1, double x2, double y2) => Add(new Line(P(x1, y1), P(x2, y2)), LineLayer);
        void Rect(double x1, double y1, double x2, double y2)
        {
            Line(x1, y1, x2, y1); Line(x2, y1, x2, y2); Line(x2, y2, x1, y2); Line(x1, y2, x1, y1);
        }
        void Spot(double x, double y) => Add(new Circle(P(x, y), Vector3d.ZAxis, 0.25), LineLayer);

        // Touchlines, goal lines, halfway line, centre circle.
        Rect(-L, -W, L, W);
        Line(0, -W, 0, W);
        Add(new Circle(P(0, 0), Vector3d.ZAxis, 9.15), LineLayer);
        Spot(0, 0);

        const double arcHalf = 0.927295218; // acos(5.5 / 9.15): D arc outside the box
        foreach (int s in new[] { -1, 1 })
        {
            double gl = s * L;
            // Penalty area, goal area, penalty spot, the "D".
            Rect(gl, -Match.PenaltyHalfWidth, gl - s * Match.PenaltyDepth, Match.PenaltyHalfWidth);
            Rect(gl, -9.16, gl - s * 5.5, 9.16);
            Spot(gl - s * 11, 0);
            double a0 = s < 0 ? -arcHalf : Math.PI - arcHalf;
            Add(new Arc(P(gl - s * 11, 0), 9.15, a0, a0 + 2 * arcHalf), LineLayer);
            // Goal frame and net behind the line.
            Rect(gl, -Match.GoalHalfWidth, gl + s * 2.0, Match.GoalHalfWidth);
            for (double y = -Match.GoalHalfWidth + 0.61; y < Match.GoalHalfWidth; y += 0.61)
                Line(gl, y, gl + s * 2.0, y);
        }

        // Corner arcs.
        Add(new Arc(P(-L, -W), 1, 0, Math.PI / 2), LineLayer);
        Add(new Arc(P(L, -W), 1, Math.PI / 2, Math.PI), LineLayer);
        Add(new Arc(P(L, W), 1, Math.PI, 1.5 * Math.PI), LineLayer);
        Add(new Arc(P(-L, W), 1, 1.5 * Math.PI, 2 * Math.PI), LineLayer);

        var title = new DBText
        {
            TextString = "CIVIL 3D  FC  -  2026",
            Height = 2.5,
            Position = P(-L, -W - 7),
        };
        Add(title, LineLayer);

        tr.Commit();
        return ids;
    }

    public static void Erase(Database db, IEnumerable<ObjectId> ids)
    {
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in ids)
        {
            if (id.IsNull || id.IsErased || !id.IsValid) continue;
            tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
        tr.Commit();
    }

    static void EnsureLayer(Transaction tr, Database db, string name, Color color)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return;
        lt.UpgradeOpen();
        var ltr = new LayerTableRecord { Name = name, Color = color };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
    }
}
