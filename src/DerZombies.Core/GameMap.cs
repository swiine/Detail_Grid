using System;
using System.Collections.Generic;
using System.Linq;

namespace DerZombies.Core
{
    public sealed class Door
    {
        public char Id { get; }
        public int Cost { get; }
        public List<(int Col, int Row)> Cells { get; } = new List<(int, int)>();
        public HashSet<char> Areas { get; } = new HashSet<char>();
        public bool Open { get; internal set; }
        public Vec2 Center { get; internal set; }

        internal Door(char id, int cost) { Id = id; Cost = cost; }
    }

    public sealed class Area
    {
        public char Id { get; }
        public string Name { get; }
        public List<(int Col, int Row)> Cells { get; } = new List<(int, int)>();
        public List<Vec2> Spawners { get; } = new List<Vec2>();
        public Vec2 LabelPoint { get; internal set; }
        public bool Open { get; internal set; }

        internal Area(char id, string name) { Id = id; Name = name; }
    }

    /// <summary>Grid map: walkability, doors, areas, line of sight and the zombie flow field.</summary>
    public sealed class GameMap
    {
        public double Cell { get; }
        public int Cols { get; }
        public int Rows { get; }
        public double Width => Cols * Cell;
        public double Height => Rows * Cell;

        public Dictionary<char, Door> Doors { get; } = new Dictionary<char, Door>();
        public Dictionary<char, Area> Areas { get; } = new Dictionary<char, Area>();

        private readonly char[,] _grid;      // [col,row], row 0 = north
        private readonly bool[,] _walkable;
        private readonly int[,] _flow;       // BFS step distance to the player
        private (int, int) _flowOrigin = (-1, -1);

        public const int Unreachable = int.MaxValue;

        public GameMap(string[] layout, double cell, IDictionary<char, int> doorCosts, IDictionary<char, string> areaNames)
        {
            Cell = cell;
            Rows = layout.Length;
            Cols = layout.Max(l => l.Length);
            _grid = new char[Cols, Rows];
            _walkable = new bool[Cols, Rows];
            _flow = new int[Cols, Rows];

            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                char ch = c < layout[r].Length ? layout[r][c] : '#';
                _grid[c, r] = ch;
                if (IsAreaChar(ch))
                {
                    _walkable[c, r] = true;
                    if (!Areas.TryGetValue(ch, out var area))
                    {
                        area = new Area(ch, areaNames.TryGetValue(ch, out var n) ? n : ch.ToString());
                        Areas[ch] = area;
                    }
                    area.Cells.Add((c, r));
                }
                else if (IsDoorChar(ch))
                {
                    if (!Doors.TryGetValue(ch, out var door))
                    {
                        door = new Door(ch, doorCosts.TryGetValue(ch, out var cost) ? cost : 1000);
                        Doors[ch] = door;
                    }
                    door.Cells.Add((c, r));
                }
            }

            foreach (var door in Doors.Values)
            {
                double sx = 0, sy = 0;
                foreach (var (c, r) in door.Cells)
                {
                    var p = CellCenter(c, r);
                    sx += p.X; sy += p.Y;
                    foreach (var (nc, nr) in Neighbours4(c, r))
                        if (IsAreaChar(_grid[nc, nr])) door.Areas.Add(_grid[nc, nr]);
                }
                door.Center = new Vec2(sx / door.Cells.Count, sy / door.Cells.Count);
            }

            foreach (var area in Areas.Values)
            {
                int minC = area.Cells.Min(x => x.Col), maxC = area.Cells.Max(x => x.Col);
                int minR = area.Cells.Min(x => x.Row), maxR = area.Cells.Max(x => x.Row);
                area.LabelPoint = (CellCenter(minC, minR) + CellCenter(maxC, maxR)) / 2;

                // Zombies climb in at the walkable cell nearest each corner of the area.
                foreach (var (cc, cr) in new[] { (minC, minR), (maxC, minR), (minC, maxR), (maxC, maxR) })
                {
                    var best = area.Cells.OrderBy(x => Math.Abs(x.Col - cc) + Math.Abs(x.Row - cr)).First();
                    var p = CellCenter(best.Col, best.Row);
                    if (!area.Spawners.Any(s => Vec2.Distance(s, p) < 0.1)) area.Spawners.Add(p);
                }
            }
        }

        public static GameMap CreateDefault() =>
            new GameMap(MapData.Layout, MapData.CellSize, MapData.DoorCosts, MapData.AreaNames);

        public static bool IsAreaChar(char ch) => ch >= 'A' && ch <= 'Z';
        public static bool IsDoorChar(char ch) => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z');

        public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < Cols && r < Rows;
        public char CharAt(int c, int r) => InBounds(c, r) ? _grid[c, r] : '#';

        /// <summary>True for floor cells and for door cells that have been opened.</summary>
        public bool IsWalkable(int c, int r) => InBounds(c, r) && _walkable[c, r];

        /// <summary>True for any cell that is not a wall (floor or door, open or closed).</summary>
        public bool IsFloorOrDoor(int c, int r)
        {
            char ch = CharAt(c, r);
            return IsAreaChar(ch) || IsDoorChar(ch);
        }

        public Vec2 CellCenter(int c, int r) => new Vec2((c + 0.5) * Cell, (Rows - 1 - r + 0.5) * Cell);

        public (int Col, int Row) CellOf(Vec2 p) =>
            ((int)Math.Floor(p.X / Cell), Rows - 1 - (int)Math.Floor(p.Y / Cell));

        public bool IsWalkableAt(Vec2 p)
        {
            var (c, r) = CellOf(p);
            return IsWalkable(c, r);
        }

        /// <summary>Checks a circle's bounding square against the grid.</summary>
        public bool CircleFree(Vec2 p, double radius) =>
            IsWalkableAt(p) &&
            IsWalkableAt(new Vec2(p.X - radius, p.Y - radius)) &&
            IsWalkableAt(new Vec2(p.X + radius, p.Y - radius)) &&
            IsWalkableAt(new Vec2(p.X - radius, p.Y + radius)) &&
            IsWalkableAt(new Vec2(p.X + radius, p.Y + radius));

        /// <summary>Area letter under a point, or '\0' (doorways and walls).</summary>
        public char AreaAt(Vec2 p)
        {
            var (c, r) = CellOf(p);
            char ch = CharAt(c, r);
            return IsAreaChar(ch) ? ch : '\0';
        }

        public void OpenDoor(char id)
        {
            if (!Doors.TryGetValue(id, out var door) || door.Open) return;
            door.Open = true;
            foreach (var (c, r) in door.Cells) _walkable[c, r] = true;
            foreach (var a in door.Areas) Areas[a].Open = true;
            _flowOrigin = (-1, -1); // force a flow-field rebuild
        }

        public void OpenArea(char id)
        {
            if (Areas.TryGetValue(id, out var a)) a.Open = true;
        }

        /// <summary>Distance along a ray until it hits a non-walkable cell (or maxDist).</summary>
        public double Raycast(Vec2 from, Vec2 dir, double maxDist)
        {
            const double step = 0.5;
            for (double t = step; t <= maxDist; t += step)
                if (!IsWalkableAt(from + dir * t)) return t - step;
            return maxDist;
        }

        public bool HasLineOfSight(Vec2 a, Vec2 b)
        {
            var d = b - a;
            double len = d.Length;
            if (len < 1e-6) return true;
            return Raycast(a, d / len, len) >= len - 0.5;
        }

        // ---------------------------------------------------------------- flow field

        /// <summary>Rebuilds the BFS distance field towards <paramref name="target"/> when its cell changes.</summary>
        public void UpdateFlowField(Vec2 target)
        {
            var origin = CellOf(target);
            if (origin == _flowOrigin) return;
            _flowOrigin = origin;

            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _flow[c, r] = Unreachable;

            var q = new Queue<(int, int)>();
            if (IsWalkable(origin.Col, origin.Row))
            {
                _flow[origin.Col, origin.Row] = 0;
                q.Enqueue(origin);
            }
            while (q.Count > 0)
            {
                var (c, r) = q.Dequeue();
                int d = _flow[c, r] + 1;
                foreach (var (nc, nr) in Neighbours8(c, r))
                {
                    if (_flow[nc, nr] <= d) continue;
                    _flow[nc, nr] = d;
                    q.Enqueue((nc, nr));
                }
            }
        }

        public int FlowDistance(Vec2 p)
        {
            var (c, r) = CellOf(p);
            return InBounds(c, r) ? _flow[c, r] : Unreachable;
        }

        /// <summary>The next waypoint (a cell centre) a zombie at <paramref name="p"/> should walk to.</summary>
        public Vec2? NextWaypoint(Vec2 p)
        {
            var (c, r) = CellOf(p);
            if (!InBounds(c, r)) return null;
            int best = _flow[c, r];
            (int, int)? bestCell = null;
            foreach (var n in Neighbours8(c, r))
            {
                int d = _flow[n.Item1, n.Item2];
                if (d < best) { best = d; bestCell = n; }
            }
            return bestCell.HasValue ? CellCenter(bestCell.Value.Item1, bestCell.Value.Item2) : (Vec2?)null;
        }

        private IEnumerable<(int, int)> Neighbours4(int c, int r)
        {
            if (InBounds(c + 1, r)) yield return (c + 1, r);
            if (InBounds(c - 1, r)) yield return (c - 1, r);
            if (InBounds(c, r + 1)) yield return (c, r + 1);
            if (InBounds(c, r - 1)) yield return (c, r - 1);
        }

        /// <summary>Walkable 8-neighbours; diagonals only when both orthogonal cells are free (no corner cutting).</summary>
        private IEnumerable<(int, int)> Neighbours8(int c, int r)
        {
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                if (dc == 0 && dr == 0) continue;
                int nc = c + dc, nr = r + dr;
                if (!IsWalkable(nc, nr)) continue;
                if (dc != 0 && dr != 0 && (!IsWalkable(c + dc, r) || !IsWalkable(c, r + dr))) continue;
                yield return (nc, nr);
            }
        }

        // ---------------------------------------------------------------- geometry export

        /// <summary>
        /// Outline of every wall, as merged straight segments: the edges between wall
        /// cells and floor/door cells. Doors stay as gaps; they are drawn separately.
        /// </summary>
        public List<(Vec2 A, Vec2 B)> WallSegments()
        {
            // Horizontal edges keyed by their grid line, vertical edges likewise.
            var horizontal = new Dictionary<int, List<int>>(); // line y index -> list of x cell indices
            var vertical = new Dictionary<int, List<int>>();   // line x index -> list of y cell indices

            for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (!IsFloorOrDoor(c, r)) continue;
                int yb = Rows - 1 - r; // cell's bottom grid line (in +Y up coordinates)
                if (!IsFloorOrDoor(c, r - 1)) Add(horizontal, yb + 1, c);
                if (!IsFloorOrDoor(c, r + 1)) Add(horizontal, yb, c);
                if (!IsFloorOrDoor(c - 1, r)) Add(vertical, c, yb);
                if (!IsFloorOrDoor(c + 1, r)) Add(vertical, c + 1, yb);
            }

            var result = new List<(Vec2, Vec2)>();
            foreach (var kv in horizontal)
                foreach (var (s, e) in Runs(kv.Value))
                    result.Add((new Vec2(s * Cell, kv.Key * Cell), new Vec2((e + 1) * Cell, kv.Key * Cell)));
            foreach (var kv in vertical)
                foreach (var (s, e) in Runs(kv.Value))
                    result.Add((new Vec2(kv.Key * Cell, s * Cell), new Vec2(kv.Key * Cell, (e + 1) * Cell)));
            return result;

            static void Add(Dictionary<int, List<int>> d, int key, int v)
            {
                if (!d.TryGetValue(key, out var l)) d[key] = l = new List<int>();
                l.Add(v);
            }

            static IEnumerable<(int, int)> Runs(List<int> values)
            {
                values.Sort();
                int start = values[0], prev = values[0];
                for (int i = 1; i < values.Count; i++)
                {
                    if (values[i] == prev) continue;
                    if (values[i] != prev + 1) { yield return (start, prev); start = values[i]; }
                    prev = values[i];
                }
                yield return (start, prev);
            }
        }

        /// <summary>Axis-aligned bounds (min, max) of a door's cells.</summary>
        public (Vec2 Min, Vec2 Max) DoorBounds(Door door)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (c, r) in door.Cells)
            {
                var p = CellCenter(c, r);
                minX = Math.Min(minX, p.X - Cell / 2); maxX = Math.Max(maxX, p.X + Cell / 2);
                minY = Math.Min(minY, p.Y - Cell / 2); maxY = Math.Max(maxY, p.Y + Cell / 2);
            }
            return (new Vec2(minX, minY), new Vec2(maxX, maxY));
        }
    }
}
