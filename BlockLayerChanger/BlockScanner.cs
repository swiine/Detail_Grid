using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace BlockLayerChanger
{
    /// <summary>A named block definition and what it contains.</summary>
    internal sealed class BlockInfo
    {
        public ObjectId Id;
        public string Name;

        /// <summary>Layers used by entities directly inside this block.</summary>
        public readonly HashSet<string> Layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Named blocks inserted inside this block (nested blocks).</summary>
        public readonly HashSet<ObjectId> Children = new HashSet<ObjectId>();

        /// <summary>
        /// The definition itself plus, for dynamic blocks, the anonymous (*U) copies
        /// AutoCAD makes for each modified insert. All of them must be edited.
        /// </summary>
        public readonly List<ObjectId> Representations = new List<ObjectId>();

        public override string ToString() => Name;
    }

    internal sealed class ChangeResult
    {
        public int Entities;
        public int Attributes;
        public int Blocks;
    }

    internal static class BlockScanner
    {
        /// <summary>Reads every named, non-xref block definition in the drawing.</summary>
        public static Dictionary<ObjectId, BlockInfo> Scan(Database db)
        {
            var blocks = new Dictionary<ObjectId, BlockInfo>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference ||
                        btr.IsFromOverlayReference || btr.IsDependent)
                        continue;

                    var info = new BlockInfo { Id = id, Name = btr.Name };
                    info.Representations.Add(id);
                    if (btr.IsDynamicBlock)
                    {
                        foreach (ObjectId anonId in btr.GetAnonymousBlockIds())
                            if (!anonId.IsErased) info.Representations.Add(anonId);
                    }

                    foreach (ObjectId repId in info.Representations)
                    {
                        var rep = (BlockTableRecord)tr.GetObject(repId, OpenMode.ForRead);
                        foreach (ObjectId entId in rep)
                        {
                            if (!(tr.GetObject(entId, OpenMode.ForRead) is Entity ent)) continue;
                            info.Layers.Add(ent.Layer);
                            if (ent is BlockReference br)
                                info.Children.Add(br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord);
                        }
                    }

                    blocks[id] = info;
                }
                tr.Commit();
            }
            return blocks;
        }

        /// <summary>The selected blocks, plus every block nested inside them when requested.</summary>
        public static List<BlockInfo> Expand(IEnumerable<ObjectId> selected,
            IDictionary<ObjectId, BlockInfo> blocks, bool includeNested)
        {
            var visited = new HashSet<ObjectId>();
            var result = new List<BlockInfo>();
            var stack = new Stack<ObjectId>(selected);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!visited.Add(id) || !blocks.TryGetValue(id, out var info)) continue;
                result.Add(info);
                if (includeNested)
                    foreach (var child in info.Children) stack.Push(child);
            }
            return result;
        }

        /// <summary>
        /// Moves every entity inside <paramref name="blocks"/> that sits on one of
        /// <paramref name="fromLayers"/> onto <paramref name="targetLayer"/> (created if missing).
        /// </summary>
        public static ChangeResult ChangeLayers(Database db, IEnumerable<BlockInfo> blocks,
            ISet<string> fromLayers, string targetLayer, bool includeAttributes)
        {
            var result = new ChangeResult();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId targetId = EnsureLayer(tr, db, targetLayer);

                foreach (var info in blocks)
                {
                    int before = result.Entities + result.Attributes;
                    foreach (ObjectId repId in info.Representations)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(repId, OpenMode.ForRead);

                        foreach (ObjectId entId in btr)
                        {
                            if (TrySetLayer(tr, entId, fromLayers, targetId)) result.Entities++;
                        }

                        // Attribute values live on each insert, not in the definition.
                        if (includeAttributes)
                        {
                            foreach (ObjectId brId in btr.GetBlockReferenceIds(true, false))
                            {
                                if (brId.IsErased) continue;
                                var br = (BlockReference)tr.GetObject(brId, OpenMode.ForRead);
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    if (TrySetLayer(tr, attId, fromLayers, targetId)) result.Attributes++;
                                }
                            }
                        }
                    }
                    if (result.Entities + result.Attributes > before) result.Blocks++;
                }

                tr.Commit();
            }
            return result;
        }

        private static bool TrySetLayer(Transaction tr, ObjectId id, ISet<string> fromLayers, ObjectId targetId)
        {
            if (id.IsErased) return false;
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) return false;
            if (ent.LayerId == targetId || !fromLayers.Contains(ent.Layer)) return false;

            // openOnLockedLayer: true so entities on locked layers are changed too.
            var writable = (Entity)tr.GetObject(id, OpenMode.ForWrite, false, true);
            writable.LayerId = targetId;
            return true;
        }

        private static ObjectId EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                var existing = lt[name];
                if (!existing.IsErased) return existing;
            }

            SymbolUtilityServices.ValidateSymbolName(name, false);
            lt.UpgradeOpen();
            var layer = new LayerTableRecord { Name = name };
            ObjectId id = lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        /// <summary>All layer names in the drawing, sorted.</summary>
        public static List<string> GetLayerNames(Database db)
        {
            var names = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!layer.IsDependent) names.Add(layer.Name);
                }
                tr.Commit();
            }
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
