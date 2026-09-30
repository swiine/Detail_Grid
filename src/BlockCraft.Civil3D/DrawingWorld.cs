using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BlockCraft.Core;

namespace BlockCraft.Civil3D;

/// <summary>
/// A BlockCraft world living in a drawing.
/// <para>
/// The full voxel grid is saved in the drawing's named object dictionary, so it travels with the DWG
/// and follows UNDO. Only the visible "shell" of the world is drawn: one block reference (BC_GRASS,
/// BC_STONE, ...) per visible voxel, snapped to the grid. Because they are ordinary block references
/// they can be erased, copied, moved or arrayed with normal drafting commands; BCSYNC reads those
/// edits back into the voxel grid.
/// </para>
/// </summary>
internal sealed class DrawingWorld
{
    public const string DictionaryKey = "BLOCKCRAFT_WORLD";
    public const string BlockPrefix = "BC_";
    public const string LayerPrefix = "BLOCKCRAFT-";
    private const int ChunkSize = 127; // DXF binary chunks are limited to 127 bytes

    private readonly Database _db;
    private readonly Dictionary<BlockType, ObjectId> _blockIds = new();
    private readonly Dictionary<BlockType, ObjectId> _layerIds = new();
    private Dictionary<(int X, int Y, int Z), List<(ObjectId Id, BlockType Type)>>? _index;

    private DrawingWorld(Database db, World world)
    {
        _db = db;
        World = world;
    }

    public World World { get; }

    public int Inserted { get; private set; }
    public int Erased { get; private set; }

    // ------------------------------------------------------------------ persistence

    public static DrawingWorld Create(Database db, World world) => new(db, world);

    /// <summary>Loads the drawing's world, or returns null if the drawing has none.</summary>
    public static DrawingWorld? Load(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(DictionaryKey))
            return null;

        var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForRead);
        using var ms = new MemoryStream();
        foreach (TypedValue tv in xrec.Data ?? new ResultBuffer())
        {
            if (tv.TypeCode == (short)DxfCode.BinaryChunk && tv.Value is byte[] chunk)
                ms.Write(chunk, 0, chunk.Length);
        }
        return new DrawingWorld(db, World.Deserialize(ms.ToArray()));
    }

    public void Save(Transaction tr)
    {
        byte[] data = World.Serialize();
        var rb = new ResultBuffer();
        for (int i = 0; i < data.Length; i += ChunkSize)
        {
            var chunk = new byte[Math.Min(ChunkSize, data.Length - i)];
            Buffer.BlockCopy(data, i, chunk, 0, chunk.Length);
            rb.Add(new TypedValue((int)DxfCode.BinaryChunk, chunk));
        }

        var nod = (DBDictionary)tr.GetObject(_db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(DictionaryKey))
        {
            var xrec = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForWrite);
            xrec.Data = rb;
        }
        else
        {
            nod.UpgradeOpen();
            var xrec = new Xrecord { Data = rb };
            nod.SetAt(DictionaryKey, xrec);
            tr.AddNewlyCreatedDBObject(xrec, true);
        }
    }

    /// <summary>Erases every BlockCraft block in model space and forgets the stored world.</summary>
    public static int Clear(Database db, Transaction tr)
    {
        int erased = 0;
        ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        foreach (var (id, _) in BlockCraftReferences(db, tr, modelSpaceId))
        {
            tr.GetObject(id, OpenMode.ForWrite).Erase();
            erased++;
        }

        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(DictionaryKey))
        {
            ObjectId xid = nod.GetAt(DictionaryKey);
            nod.UpgradeOpen();
            nod.Remove(DictionaryKey);
            tr.GetObject(xid, OpenMode.ForWrite).Erase();
        }
        return erased;
    }

    // ------------------------------------------------------------------ drawing <-> voxels

    /// <summary>Draws the whole shell of the world (used right after generating it).</summary>
    public void DrawAll(Transaction tr, IReadOnlyList<(int X, int Y, int Z)> voxels, Action? progress = null)
    {
        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
        _index = new();
        foreach (var v in voxels)
        {
            Insert(tr, modelSpace, v, World.Get(v.X, v.Y, v.Z));
            progress?.Invoke();
        }
    }

    /// <summary>The voxel a BlockCraft block reference stands for (from its snapped insertion point).</summary>
    public (int X, int Y, int Z) VoxelOf(BlockReference br) =>
        World.Mapping.NearestVoxelCorner(br.Position.X, br.Position.Y, br.Position.Z);

    public static BlockType? TypeOfBlockName(string name)
    {
        if (!name.StartsWith(BlockPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        string typeName = name[BlockPrefix.Length..];
        for (int i = 1; i < Blocks.Count; i++)
        {
            var t = (BlockType)i;
            if (string.Equals(Blocks.Info(t).Name, typeName, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    /// <summary>Every BlockCraft block reference in model space, by voxel.</summary>
    public IReadOnlyDictionary<(int X, int Y, int Z), List<(ObjectId Id, BlockType Type)>> Index(Transaction tr)
    {
        if (_index != null)
            return _index;

        _index = new();
        ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(_db);
        foreach (var (id, type) in BlockCraftReferences(_db, tr, modelSpaceId))
        {
            var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var key = VoxelOf(br);
            if (!_index.TryGetValue(key, out var list))
                _index[key] = list = new();
            list.Add((id, type));
        }
        return _index;
    }

    private static IEnumerable<(ObjectId Id, BlockType Type)> BlockCraftReferences(Database db, Transaction tr, ObjectId ownerId)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (TypeOfBlockName(btr.Name) is not { } type)
                continue;

            foreach (ObjectId refId in btr.GetBlockReferenceIds(true, false))
            {
                if (refId.IsErased) continue;
                var br = (BlockReference)tr.GetObject(refId, OpenMode.ForRead);
                if (br.OwnerId == ownerId)
                    yield return (refId, type);
            }
        }
    }

    /// <summary>
    /// Brings the drawing in line with the voxel grid around the given voxels: removes blocks that
    /// were dug out or changed, and draws neighbours that have just become visible.
    /// </summary>
    public void Reconcile(Transaction tr, IEnumerable<(int X, int Y, int Z)> changed)
    {
        var index = (Dictionary<(int X, int Y, int Z), List<(ObjectId Id, BlockType Type)>>)Index(tr);
        var affected = new HashSet<(int X, int Y, int Z)>();
        foreach (var (x, y, z) in changed)
        {
            affected.Add((x, y, z));
            affected.Add((x + 1, y, z)); affected.Add((x - 1, y, z));
            affected.Add((x, y + 1, z)); affected.Add((x, y - 1, z));
            affected.Add((x, y, z + 1)); affected.Add((x, y, z - 1));
        }

        BlockTableRecord? modelSpace = null;
        foreach (var v in affected)
        {
            if (!World.InBounds(v.X, v.Y, v.Z))
                continue;

            BlockType type = World.Get(v.X, v.Y, v.Z);
            bool needRef = true;
            if (index.TryGetValue(v, out var refs))
            {
                // Keep a single reference of the right type; erase the rest.
                for (int i = refs.Count - 1; i >= 0; i--)
                {
                    bool wanted = type != BlockType.Air && refs[i].Type == type && needRef;
                    if (wanted) { needRef = false; continue; }
                    tr.GetObject(refs[i].Id, OpenMode.ForWrite).Erase();
                    refs.RemoveAt(i);
                    Erased++;
                }
                if (refs.Count == 0) index.Remove(v);
            }

            if (needRef && World.IsDrawn(v.X, v.Y, v.Z))
            {
                modelSpace ??= (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                Insert(tr, modelSpace, v, type);
            }
        }
    }

    private void Insert(Transaction tr, BlockTableRecord modelSpace, (int X, int Y, int Z) v, BlockType type)
    {
        var map = World.Mapping;
        var (cx, cy, cz) = map.VoxelCorner(v.X, v.Y, v.Z);
        var br = new BlockReference(new Point3d(cx, cy, cz), BlockId(tr, type))
        {
            ScaleFactors = new Scale3d(map.CellSize, map.CellSize, map.CellHeight),
            LayerId = LayerId(tr, type),
        };
        ObjectId id = modelSpace.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);
        Inserted++;

        if (_index != null)
        {
            if (!_index.TryGetValue(v, out var list))
                _index[v] = list = new();
            list.Add((id, type));
        }
    }

    // ------------------------------------------------------------------ block definitions and layers

    public static string BlockName(BlockType type) => BlockPrefix + Blocks.Info(type).Name.ToUpperInvariant();

    /// <summary>A 1 x 1 x 1 cube definition for the block type; references are scaled to the cell size.</summary>
    private ObjectId BlockId(Transaction tr, BlockType type)
    {
        if (_blockIds.TryGetValue(type, out var id))
            return id;

        string name = BlockName(type);
        var bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(name))
            return _blockIds[type] = bt[name];

        var info = Blocks.Info(type);
        var btr = new BlockTableRecord { Name = name, Origin = Point3d.Origin, Comments = $"BlockCraft {info.Name} block" };
        bt.UpgradeOpen();
        id = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        byte? alpha = Blocks.IsTranslucent(type) ? (byte)110 : null;
        if (type is BlockType.Grass or BlockType.Snow)
        {
            // Coloured top layer over a dirt body, like the in-game texture.
            AddBox(tr, btr, 0.8, 0.4, info.SideColor, alpha);
            AddBox(tr, btr, 0.2, 0.9, info.TopColor, alpha);
        }
        else if (type == BlockType.Water)
        {
            // Water is only drawn at its surface; a slightly lowered slab reads better.
            AddBox(tr, btr, 0.9, 0.45, info.TopColor, alpha);
        }
        else
        {
            AddBox(tr, btr, 1, 0.5, info.SideColor, alpha);
        }

        return _blockIds[type] = id;
    }

    private static void AddBox(Transaction tr, BlockTableRecord btr, double height, double centreZ, int rgb, byte? alpha)
    {
        var solid = new Solid3d();
        solid.CreateBox(1, 1, height);
        solid.TransformBy(Matrix3d.Displacement(new Vector3d(0.5, 0.5, centreZ)));
        solid.Layer = "0";
        solid.Color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (alpha is { } a)
            solid.Transparency = new Transparency(a);
        btr.AppendEntity(solid);
        tr.AddNewlyCreatedDBObject(solid, true);
    }

    private ObjectId LayerId(Transaction tr, BlockType type)
    {
        if (_layerIds.TryGetValue(type, out var id))
            return id;

        var info = Blocks.Info(type);
        string name = LayerPrefix + info.Name.ToUpperInvariant();
        var layers = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
        if (layers.Has(name))
            return _layerIds[type] = layers[name];

        layers.UpgradeOpen();
        var layer = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, info.AciColor),
            Description = $"BlockCraft {info.Name} blocks",
        };
        id = layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
        return _layerIds[type] = id;
    }
}
