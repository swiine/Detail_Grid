using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivDoom.Engine;
using CivDoom.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CivDoom.Civil3D.Commands))]
[assembly: ExtensionApplication(typeof(CivDoom.Civil3D.Plugin))]

namespace CivDoom.Civil3D;

public sealed class Plugin : IExtensionApplication
{
    public void Initialize()
    {
        AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
            "\nCivDOOM loaded. Type CIVDOOM to turn this drawing into a level.\n");
    }

    public void Terminate()
    {
    }
}

public sealed class Commands
{
    /// <summary>
    /// CIVDOOM: turns the current drawing's linework into a Doom-style level and drops you into it,
    /// either right in the AutoCAD viewport or in a separate game window.
    /// </summary>
    [CommandMethod("CIVDOOM", CommandFlags.Modal)]
    public void CivDoom() => Play(forceWindow: false);

    /// <summary>
    /// CIVDOOMWINDOW: the original pop-out version. Same level options and all the weapons,
    /// but always plays in its own game window (smoothest frame rate, minimap, mouse look).
    /// </summary>
    [CommandMethod("CIVDOOMWINDOW", CommandFlags.Modal)]
    public void CivDoomWindow() => Play(forceWindow: true);

    private static void Play(bool forceWindow)
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        var sourceOpts = new PromptKeywordOptions("\nLevel source [Drawing/Selection/Demo] <Drawing>: ", "Drawing Selection Demo")
        {
            AllowNone = true,
        };
        PromptResult source = ed.GetKeywords(sourceOpts);
        if (source.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        string mode = string.IsNullOrEmpty(source.StringResult) ? "Drawing" : source.StringResult;

        if (mode == "Demo")
        {
            if (!forceWindow)
                ed.WriteMessage("\nThe demo level plays in a window. Tip: CIVDOOMGEN draws a level you can play right in the viewport.");
            PlayInWindow(BuiltInLevels.DetailGrid);
            return;
        }

        ObjectId[] ids;
        if (mode == "Selection")
        {
            PromptSelectionResult sel = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect linework to use as walls, plus any DOOM-* blocks, POINTs or COGO points: ",
            });
            if (sel.Status != PromptStatus.OK) return;
            ids = sel.Value.GetObjectIds();
        }
        else
        {
            ids = ModelSpaceIds(db);
        }

        DrawingGeometry geometry;
        int skipped, markers;
        using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var extractor = new DrawingExtractor(tr, DefaultWallHeight(db), visibleLayersOnly: mode == "Drawing");
            extractor.AddObjects(ids);
            geometry = extractor.Geometry;
            skipped = extractor.SkippedEntities;
            markers = extractor.Markers;
            tr.Commit();
        }

        if (geometry.Segments.Count == 0)
        {
            ed.WriteMessage("\nNo linework found to build walls from. Run CIVDOOMGEN to draw a level, or CIVDOOM > Demo.");
            return;
        }

        var heightOpts = new PromptDoubleOptions("\nWall height in drawing units")
        {
            DefaultValue = geometry.SuggestedWallHeight ?? DefaultWallHeight(db),
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = false,
        };
        PromptDoubleResult height = ed.GetDouble(heightOpts);
        if (height.Status != PromptStatus.OK) return;
        double wallHeight = height.Value;

        if (geometry.PlayerStart == null)
        {
            if (!PromptStart(ed, geometry)) return;
        }
        else
        {
            ed.WriteMessage("\nStarting at the DOOM-START block.");
        }

        ed.WriteMessage($"\nBuilt {geometry.Segments.Count:N0} wall segments" +
                        (markers > 0 ? $", {markers} DOOM blocks" : "") +
                        (geometry.EnemyPoints.Count > 0 ? $", {geometry.EnemyPoints.Count} enemy points" : "") +
                        (skipped > 0 ? $" ({skipped:N0} unsupported objects ignored)." : "."));
        if (geometry.Segments.Count >= LevelBuilder.MaxWalls)
            ed.WriteMessage($"\nThat's a lot of linework: capped at {LevelBuilder.MaxWalls:N0} segments. Use Selection to pick an area.");

        bool inViewport = false;
        if (!forceWindow)
        {
            var playOpts = new PromptKeywordOptions("\nPlay in [Viewport/Window] <Viewport>: ", "Viewport Window") { AllowNone = true };
            PromptResult play = ed.GetKeywords(playOpts);
            if (play.Status is not (PromptStatus.OK or PromptStatus.None)) return;
            inViewport = play.StringResult is null or "" or "Viewport";
        }
        if (inViewport && !db.TileMode)
        {
            ed.WriteMessage("\nViewport mode needs the Model tab; playing in a window instead.");
            inViewport = false;
        }

        string name = Path.GetFileNameWithoutExtension(doc.Name);
        int baseSeed = Environment.TickCount;
        if (inViewport)
        {
            var game = new ViewportGame(
                doc,
                seed => new Game(LevelBuilder.FromDrawing(geometry, wallHeight, baseSeed + seed, name), baseSeed + seed,
                                 MonsterSet.Load(DesignFolder("monsters")), WeaponSet.Load(DesignFolder("weapons"))),
                () => (MonsterSet.Load(DesignFolder("monsters")), WeaponSet.Load(DesignFolder("weapons"))));
            game.Run();
        }
        else
        {
            int seed = baseSeed;
            PlayInWindow(() => LevelBuilder.FromDrawing(geometry, wallHeight, seed++, name));
        }
    }

    private static bool PromptStart(Editor ed, DrawingGeometry geometry)
    {
        Matrix3d ucs = ed.CurrentUserCoordinateSystem;
        PromptPointResult startPick = ed.GetPoint(new PromptPointOptions("\nPick player start <middle of drawing>: ")
        {
            AllowNone = true,
        });
        if (startPick.Status is not (PromptStatus.OK or PromptStatus.None)) return false;
        if (startPick.Status != PromptStatus.OK) return true;

        Point3d start = startPick.Value.TransformBy(ucs);
        geometry.PlayerStart = new Vec2(start.X, start.Y);
        PromptPointResult facePick = ed.GetPoint(new PromptPointOptions("\nPick a point to face <East>: ")
        {
            AllowNone = true,
            BasePoint = startPick.Value,
            UseBasePoint = true,
            UseDashedLine = true,
        });
        if (facePick.Status == PromptStatus.Cancel) return false;
        if (facePick.Status == PromptStatus.OK)
        {
            Point3d face = facePick.Value.TransformBy(ucs);
            geometry.PlayerAngle = Math.Atan2(face.Y - start.Y, face.X - start.X);
        }
        return true;
    }

    /// <summary>
    /// CIVDOOMGEN: draws a random level into model space as ordinary, editable objects: closed polylines on
    /// DOOM-WALLS and DOOM-* marker blocks for the start, monsters, weapons and items.
    /// </summary>
    [CommandMethod("CIVDOOMGEN", CommandFlags.Modal)]
    public void Generate()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        PromptResult sizeRes = ed.GetKeywords(new PromptKeywordOptions("\nLevel size [Small/Medium/Large] <Medium>: ", "Small Medium Large")
        {
            AllowNone = true,
        });
        if (sizeRes.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        LevelSize size = sizeRes.StringResult switch
        {
            "Small" => LevelSize.Small,
            "Large" => LevelSize.Large,
            _ => LevelSize.Medium,
        };

        PromptDoubleResult height = ed.GetDouble(new PromptDoubleOptions("\nWall height in drawing units")
        {
            DefaultValue = DefaultWallHeight(db),
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = false,
        });
        if (height.Status != PromptStatus.OK) return;
        double h = height.Value;

        PromptPointResult corner = ed.GetPoint(new PromptPointOptions("\nLower-left corner of the level <0,0>: ") { AllowNone = true });
        if (corner.Status is not (PromptStatus.OK or PromptStatus.None)) return;
        Point3d origin = corner.Status == PromptStatus.OK ? corner.Value.TransformBy(ed.CurrentUserCoordinateSystem) : Point3d.Origin;

        int seed = Environment.TickCount;
        GeneratedLevel gen = LevelGenerator.Generate(seed, size);
        MonsterSet monsters = MonsterSet.Load(DesignFolder("monsters"));
        WeaponSet weapons = WeaponSet.Load(DesignFolder("weapons"));
        var rng = new Random(seed);
        List<WeaponDesign> dealable = weapons.Designs.Where(w => !w.StartWith).ToList();
        int dealt = 0;

        Point3d P(Vec2 v) => new(origin.X + v.X * h, origin.Y + v.Y * h, origin.Z);

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            foreach (string layer in new[] { DoomBlocks.WallsLayer, DoomBlocks.MonstersLayer, DoomBlocks.ItemsLayer, DoomBlocks.StartLayer })
                DoomBlocks.EnsureLayer(tr, db, layer, DoomBlocks.LayerColor(layer));

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            foreach (List<Vec2> loop in gen.Walls)
            {
                var pl = new Polyline();
                pl.SetDatabaseDefaults(db);
                for (int i = 0; i < loop.Count; i++)
                    pl.AddVertexAt(i, new Point2d(origin.X + loop[i].X * h, origin.Y + loop[i].Y * h), 0, 0, 0);
                pl.Closed = true;
                pl.Elevation = origin.Z;
                pl.Layer = DoomBlocks.WallsLayer;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
            }

            void Insert(string block, string label, Point3d at, double rotation, string layer)
            {
                ObjectId btr = DoomBlocks.EnsureBlock(tr, db, block, label);
                var br = new BlockReference(at, btr);
                br.SetDatabaseDefaults(db);
                br.ScaleFactors = new Scale3d(h);
                br.Rotation = rotation;
                br.Layer = layer;
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
            }

            Insert(DoomBlocks.Start, "START", P(gen.Start), gen.StartAngle, DoomBlocks.StartLayer);
            foreach (EnemySpawn m in gen.Monsters)
            {
                MonsterDesign d = monsters.Resolve(m.Kind, rng);
                Insert(DoomBlocks.MonsterBlock(d.Id), d.Name.ToUpperInvariant(), P(m.Position), 0, DoomBlocks.MonstersLayer);
            }
            foreach (PickupSpawn p in gen.Pickups)
            {
                switch (p.Kind)
                {
                    case PickupKind.Health:
                        Insert(DoomBlocks.Health, "HEALTH", P(p.Position), 0, DoomBlocks.ItemsLayer);
                        break;
                    case PickupKind.Ammo:
                        Insert(DoomBlocks.Ammo, "AMMO", P(p.Position), 0, DoomBlocks.ItemsLayer);
                        break;
                    case PickupKind.Weapon when dealable.Count > 0:
                        WeaponDesign w = dealable[dealt++ % dealable.Count];
                        Insert(DoomBlocks.WeaponBlock(w.Id), w.Name.ToUpperInvariant(), P(p.Position), 0, DoomBlocks.ItemsLayer);
                        break;
                }
            }
            tr.Commit();
        }

        ed.WriteMessage($"\nDrew a {size.ToString().ToLowerInvariant()} level: {gen.Walls.Count} wall polylines, " +
                        $"{gen.Monsters.Count} monsters, {gen.Pickups.Count} items and weapons." +
                        "\nEdit it with any drafting commands (STRETCH, MOVE, COPY, ERASE, PLINE...), then run CIVDOOM to play.");
        try
        {
            ed.Command("_.ZOOM", "_EXTENTS");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Zooming is only a convenience.
        }
    }

    /// <summary>
    /// CIVDOOMBLOCKS: adds every DOOM-* marker block (one per monster and weapon file) to the drawing,
    /// so you can place them yourself with INSERT.
    /// </summary>
    [CommandMethod("CIVDOOMBLOCKS", CommandFlags.Modal)]
    public void Blocks()
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Database db = doc.Database;
        var names = new List<string>();
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            foreach (string layer in new[] { DoomBlocks.WallsLayer, DoomBlocks.MonstersLayer, DoomBlocks.ItemsLayer, DoomBlocks.StartLayer })
                DoomBlocks.EnsureLayer(tr, db, layer, DoomBlocks.LayerColor(layer));

            void Add(string block, string label)
            {
                DoomBlocks.EnsureBlock(tr, db, block, label);
                names.Add(block);
            }

            Add(DoomBlocks.Start, "START");
            Add(DoomBlocks.Monster, "MONSTER?");
            foreach (MonsterDesign m in MonsterSet.Load(DesignFolder("monsters")).Designs)
                Add(DoomBlocks.MonsterBlock(m.Id), m.Name.ToUpperInvariant());
            Add(DoomBlocks.Weapon, "WEAPON?");
            foreach (WeaponDesign w in WeaponSet.Load(DesignFolder("weapons")).Designs)
                Add(DoomBlocks.WeaponBlock(w.Id), w.Name.ToUpperInvariant());
            Add(DoomBlocks.Health, "HEALTH");
            Add(DoomBlocks.Ammo, "AMMO");
            tr.Commit();
        }
        doc.Editor.WriteMessage($"\nAdded {names.Count} blocks: {string.Join(", ", names)}." +
                                "\nINSERT them at a scale equal to your wall height. DOOM-START's rotation is the direction you face.");
    }

    private static void PlayInWindow(Func<Level> levelFactory)
    {
        using var form = new GameForm(levelFactory, DesignFolder("monsters"), DesignFolder("weapons"));
        AcApp.ShowModalDialog(form);
    }

    /// <summary>The editable monster and weapon files live in folders next to the plugin DLL.</summary>
    private static string DesignFolder(string name) =>
        Path.Combine(Path.GetDirectoryName(typeof(Commands).Assembly.Location) ?? AppContext.BaseDirectory, name);

    private static ObjectId[] ModelSpaceIds(Database db)
    {
        using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        ObjectId[] ids = ms.Cast<ObjectId>().ToArray();
        tr.Commit();
        return ids;
    }

    /// <summary>Guess a sensible storey height from the drawing units.</summary>
    private static double DefaultWallHeight(Database db)
    {
        switch (db.Insunits)
        {
            case UnitsValue.Inches: return 120;
            case UnitsValue.Feet: return 10;
            case UnitsValue.Millimeters: return 3000;
            case UnitsValue.Centimeters: return 300;
            case UnitsValue.Meters: return 3;
        }

        // Unknown units: aim for the drawing being roughly 60 wall-heights across.
        // (An empty drawing has inverted extents, which yields no diagonal.)
        Point3d min = db.Extmin, max = db.Extmax;
        double diag = max.X > min.X && max.Y > min.Y ? min.DistanceTo(max) : 0;
        return diag > 0 ? Math.Round(diag / 60, 2) : 10;
    }
}
