using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BlockCraft.Core;
using TinSurface = Autodesk.Civil.DatabaseServices.TinSurface;

namespace BlockCraft.Civil3D;

/// <summary>Creates a Civil 3D TIN surface from the top of the block terrain.</summary>
internal static class CivilSurfaceWriter
{
    /// <summary>Trees and water are not ground, so they are ignored when finding each column's top.</summary>
    private static bool IsGround(BlockType b) =>
        b is not (BlockType.Air or BlockType.Water or BlockType.Leaves or BlockType.Log);

    public static (ObjectId Id, int Points) Create(Database db, Transaction tr, World world, string name)
    {
        var map = world.Mapping;
        var points = new Point3dCollection();
        for (int x = 0; x < world.SizeX; x++)
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int y = world.SizeY - 1; y >= 0; y--)
            {
                BlockType b = world.Get(x, y, z);
                if (!IsGround(b)) continue;
                if (b == BlockType.Bedrock && y == 0) break; // empty column outside the original surface

                var (cx, cy, bottom) = map.VoxelBase(x, y, z);
                points.Add(new Point3d(cx, cy, bottom + map.CellHeight));
                break;
            }
        }

        ObjectId id = TinSurface.Create(db, name);
        var surface = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
        surface.AddVertices(points);
        return (id, points.Count);
    }
}
