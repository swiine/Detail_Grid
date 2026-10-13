using System;
using System.Collections.Generic;

namespace DetailGrid.Hydrology
{
    /// <summary>
    /// Hydrologically conditioned D8 flow model for an <see cref="ElevationGrid"/>.
    /// Build once per surface (O(n log n)); each catchment query afterwards only touches
    /// the cells inside that catchment, so picking pour points is effectively instant.
    /// </summary>
    public sealed class FlowModel
    {
        // D8 neighbour offsets, clockwise from east (row grows southward).
        internal static readonly int[] DRow = { 0, 1, 1, 1, 0, -1, -1, -1 };
        internal static readonly int[] DCol = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly double[] Dist = { 1, Math.Sqrt(2), 1, Math.Sqrt(2), 1, Math.Sqrt(2), 1, Math.Sqrt(2) };

        /// <summary>Direction code for a cell that drains off the grid or into a nodata hole.</summary>
        public const sbyte Outlet = -1;

        /// <summary>Direction code for a nodata cell.</summary>
        public const sbyte NoData = -2;

        public ElevationGrid Grid { get; }

        /// <summary>Depression-filled elevations (NaN for nodata).</summary>
        public double[] Filled { get; }

        /// <summary>D8 direction 0..7 (see <see cref="DRow"/>), <see cref="Outlet"/> or <see cref="NoData"/>.</summary>
        public sbyte[] Direction { get; }

        /// <summary>Number of cells, including itself, that drain through each cell.</summary>
        public int[] Accumulation { get; }

        private FlowModel(ElevationGrid grid, double[] filled, sbyte[] direction, int[] accumulation)
        {
            Grid = grid;
            Filled = filled;
            Direction = direction;
            Accumulation = accumulation;
        }

        public static FlowModel Build(ElevationGrid grid)
        {
            var filled = FillDepressions(grid);
            var dir = FlowDirections(grid, filled);
            var acc = FlowAccumulation(grid, dir);
            return new FlowModel(grid, filled, dir, acc);
        }

        /// <summary>
        /// Priority-Flood + epsilon (Barnes, Lehman &amp; Mulla, 2014). Every valid cell ends up with a
        /// strictly descending path to the grid edge or to a nodata hole, so D8 is defined everywhere,
        /// including across flats and through pits.
        /// </summary>
        public static double[] FillDepressions(ElevationGrid grid)
        {
            int w = grid.Width, h = grid.Height, n = grid.Count;
            var z = new double[n];
            var closed = new bool[n];
            var open = new PriorityQueue<int, double>(2 * (w + h));
            var pit = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                float v = grid.Z[i];
                if (float.IsFinite(v)) z[i] = v;
                else { z[i] = double.NaN; closed[i] = true; }
            }

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (closed[i]) continue;
                    bool seed = r == 0 || c == 0 || r == h - 1 || c == w - 1;
                    for (int d = 0; !seed && d < 8; d++)
                        seed = double.IsNaN(z[(r + DRow[d]) * w + c + DCol[d]]);
                    if (seed) { closed[i] = true; open.Enqueue(i, z[i]); }
                }
            }

            while (pit.Count > 0 || open.Count > 0)
            {
                int i = pit.Count > 0 ? pit.Dequeue() : open.Dequeue();
                int r = i / w, c = i - r * w;
                double zi = z[i];
                for (int d = 0; d < 8; d++)
                {
                    int rr = r + DRow[d], cc = c + DCol[d];
                    if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                    int j = rr * w + cc;
                    if (closed[j]) continue;
                    closed[j] = true;
                    if (z[j] <= zi)
                    {
                        // Raise into the depression by the smallest representable step.
                        z[j] = Math.BitIncrement(zi);
                        pit.Enqueue(j);
                    }
                    else
                    {
                        open.Enqueue(j, z[j]);
                    }
                }
            }
            return z;
        }

        /// <summary>D8 steepest-descent direction for every cell of a conditioned surface.</summary>
        public static sbyte[] FlowDirections(ElevationGrid grid, double[] filled)
        {
            int w = grid.Width, h = grid.Height;
            var dir = new sbyte[grid.Count];
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    double zi = filled[i];
                    if (double.IsNaN(zi)) { dir[i] = NoData; continue; }
                    sbyte best = Outlet;
                    double bestSlope = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        int rr = r + DRow[d], cc = c + DCol[d];
                        if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                        double zj = filled[rr * w + cc];
                        if (double.IsNaN(zj)) continue;
                        double s = (zi - zj) / Dist[d];
                        if (s > bestSlope) { bestSlope = s; best = (sbyte)d; }
                    }
                    // No lower neighbour only happens on the edge or next to a hole: drain off-grid.
                    dir[i] = best;
                }
            }
            return dir;
        }

        public static int[] FlowAccumulation(ElevationGrid grid, sbyte[] dir)
        {
            int n = grid.Count, w = grid.Width;
            var acc = new int[n];
            var indeg = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int j = Downstream(dir, w, i);
                if (j >= 0) indeg[j]++;
            }
            var queue = new int[n];
            int head = 0, tail = 0;
            for (int i = 0; i < n; i++)
            {
                if (dir[i] == NoData) continue;
                acc[i] = 1;
                if (indeg[i] == 0) queue[tail++] = i;
            }
            while (head < tail)
            {
                int i = queue[head++];
                int j = Downstream(dir, w, i);
                if (j < 0) continue;
                acc[j] += acc[i];
                if (--indeg[j] == 0) queue[tail++] = j;
            }
            return acc;
        }

        private static int Downstream(sbyte[] dir, int width, int i)
        {
            int d = dir[i];
            if (d < 0) return -1;
            int r = i / width, c = i - r * width;
            return (r + DRow[d]) * width + c + DCol[d];
        }

        /// <summary>Index of the cell that <paramref name="index"/> drains into, or -1.</summary>
        public int Downstream(int index) => Downstream(Direction, Grid.Width, index);

        /// <summary>
        /// Moves a picked cell onto the highest-accumulation cell within <paramref name="radius"/>
        /// cells, so a click slightly off a channel still captures the channel's catchment.
        /// </summary>
        public int SnapPourPoint(int index, int radius)
        {
            int w = Grid.Width, h = Grid.Height;
            int r0 = index / w, c0 = index - r0 * w;
            int best = index, bestAcc = Accumulation[index];
            for (int r = Math.Max(0, r0 - radius); r <= Math.Min(h - 1, r0 + radius); r++)
            {
                for (int c = Math.Max(0, c0 - radius); c <= Math.Min(w - 1, c0 + radius); c++)
                {
                    if ((r - r0) * (r - r0) + (c - c0) * (c - c0) > radius * radius) continue;
                    int i = r * w + c;
                    if (Accumulation[i] > bestAcc) { bestAcc = Accumulation[i]; best = i; }
                }
            }
            return best;
        }

        /// <summary>Every cell that drains to <paramref name="outlet"/> (the catchment), including the outlet.</summary>
        public Catchment Delineate(int outlet)
        {
            int w = Grid.Width, h = Grid.Height;
            var mask = new bool[Grid.Count];
            if (Direction[outlet] == NoData) return new Catchment(this, outlet, mask, 0);

            var stack = new Stack<int>();
            stack.Push(outlet);
            mask[outlet] = true;
            int count = 0;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                count++;
                int r = i / w, c = i - r * w;
                for (int d = 0; d < 8; d++)
                {
                    int rr = r + DRow[d], cc = c + DCol[d];
                    if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                    int j = rr * w + cc;
                    // Neighbour j drains into i when its direction points back along d.
                    if (!mask[j] && Direction[j] == ((d + 4) & 7)) { mask[j] = true; stack.Push(j); }
                }
            }
            return new Catchment(this, outlet, mask, count);
        }

        /// <summary>Cells visited following D8 downstream from <paramref name="start"/> until leaving the grid.</summary>
        public List<int> FlowPath(int start)
        {
            var path = new List<int>();
            for (int i = start; i >= 0 && path.Count <= Grid.Count; i = Downstream(i)) path.Add(i);
            return path;
        }
    }
}
