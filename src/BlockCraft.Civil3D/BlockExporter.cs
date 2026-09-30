using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using BlockCraft.Core;

namespace BlockCraft.Civil3D;

/// <summary>Writes the blocks a player placed into model space as 3D solids, one layer per block type.</summary>
internal static class BlockExporter
{
    private const int ConfirmAbove = 5000;

    public static void Export(Document doc, World world)
    {
        Editor ed = doc.Editor;
        var placed = world.PlacedBlocks().ToList();
        if (placed.Count == 0)
        {
            ed.WriteMessage("\nYou have not placed any blocks yet.");
            return;
        }

        if (placed.Count > ConfirmAbove)
        {
            var pko = new PromptKeywordOptions($"\nExport {placed.Count:N0} solids? This may take a while. [Yes/No] <No>: ");
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "No";
            pko.AllowNone = true;
            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult != "Yes") return;
        }

        var map = world.Mapping;
        Database db = doc.Database;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var layerIds = new Dictionary<BlockType, ObjectId>();

            foreach (var (x, y, z, type) in placed)
            {
                if (!layerIds.TryGetValue(type, out var layerId))
                    layerIds[type] = layerId = EnsureLayer(tr, db, type);

                var (cx, cy, bottom) = map.VoxelBase(x, y, z);
                var solid = new Solid3d();
                solid.CreateBox(map.CellSize, map.CellSize, map.CellHeight);
                solid.TransformBy(Matrix3d.Displacement(new Vector3d(cx, cy, bottom + map.CellHeight / 2)));
                solid.LayerId = layerId;
                if (Blocks.IsTranslucent(type))
                    solid.Transparency = new Transparency((byte)127);

                modelSpace.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
            }

            tr.Commit();
        }

        ed.WriteMessage($"\nExported {placed.Count:N0} block(s) to BLOCKCRAFT-* layers.");
        ed.Regen();
    }

    private static ObjectId EnsureLayer(Transaction tr, Database db, BlockType type)
    {
        var info = Blocks.Info(type);
        string name = "BLOCKCRAFT-" + info.Name.ToUpperInvariant();
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
            return layers[name];

        layers.UpgradeOpen();
        var layer = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, info.AciColor),
            Description = $"BlockCraft {info.Name} blocks",
        };
        ObjectId id = layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return id;
    }
}
