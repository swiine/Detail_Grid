using System;
using System.Collections.Generic;

namespace DetailGrid.Hydrology
{
    /// <summary>
    /// Converts a raster cell mask into closed boundary rings that follow cell edges.
    /// Coordinates are grid vertices (col, row) where (0,0) is the grid's top-left corner.
    /// </summary>
    public static class BoundaryTracer
    {
        /// <summary>
        /// Traces every boundary of the mask. Rings are closed (first vertex repeated last) and
        /// collinear vertices are removed. Edges run with the inside of the mask on their right
        /// (in row-down grid space); at a vertex where two cells touch only diagonally the tracer
        /// turns left, so a D8 catchment (always 8-connected) yields a single outer ring.
        /// </summary>
        public static List<List<(int Col, int Row)>> Trace(bool[] mask, int width, int height)
        {
            if (mask.Length != width * height) throw new ArgumentException("Mask does not match grid size.", nameof(mask));

            int vw = width + 1;
            var from = new List<int>();
            var to = new List<int>();
            var outgoing = new Dictionary<int, List<int>>();

            void AddEdge(int a, int b)
            {
                int e = from.Count;
                from.Add(a);
                to.Add(b);
                if (!outgoing.TryGetValue(a, out var list)) outgoing[a] = list = new List<int>(1);
                list.Add(e);
            }

            bool Inside(int r, int c) => r >= 0 && c >= 0 && r < height && c < width && mask[r * width + c];

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    if (!mask[r * width + c]) continue;
                    int tl = r * vw + c, tr = tl + 1, bl = tl + vw, br = bl + 1;
                    if (!Inside(r - 1, c)) AddEdge(tl, tr);
                    if (!Inside(r, c + 1)) AddEdge(tr, br);
                    if (!Inside(r + 1, c)) AddEdge(br, bl);
                    if (!Inside(r, c - 1)) AddEdge(bl, tl);
                }
            }

            var used = new bool[from.Count];
            var rings = new List<List<(int Col, int Row)>>();
            for (int start = 0; start < from.Count; start++)
            {
                if (used[start]) continue;
                var ring = new List<(int Col, int Row)>();
                int e = start;
                while (true)
                {
                    used[e] = true;
                    ring.Add((from[e] % vw, from[e] / vw));
                    int v = to[e];
                    int next = PickNext(e, v, outgoing[v], used, from, to, vw);
                    if (next < 0) break;
                    e = next;
                }
                ring.Add(ring[0]);
                rings.Add(RemoveCollinear(ring));
            }
            return rings;
        }

        // Among unused edges leaving v, prefer a left turn, then straight, then right.
        private static int PickNext(int incoming, int v, List<int> outs, bool[] used, List<int> from, List<int> to, int vw)
        {
            var (dx, dy) = Heading(from[incoming], to[incoming], vw);
            int best = -1, bestRank = int.MaxValue;
            foreach (int e in outs)
            {
                if (used[e]) continue;
                var (ex, ey) = Heading(from[e], to[e], vw);
                int cross = dx * ey - dy * ex; // < 0 is a left turn in row-down space
                int rank = cross < 0 ? 0 : cross == 0 ? 1 : 2;
                if (rank < bestRank) { bestRank = rank; best = e; }
            }
            return best;
        }

        private static (int Dx, int Dy) Heading(int a, int b, int vw) => (b % vw - a % vw, b / vw - a / vw);

        private static List<(int Col, int Row)> RemoveCollinear(List<(int Col, int Row)> ring)
        {
            // ring is closed: ring[0] == ring[^1]. Treat it cyclically so the start vertex can go too.
            int n = ring.Count - 1;
            var keep = new List<(int Col, int Row)>(n);
            for (int k = 0; k < n; k++)
            {
                var a = ring[(k - 1 + n) % n];
                var b = ring[k];
                var c = ring[(k + 1) % n];
                long cross = (long)(b.Col - a.Col) * (c.Row - b.Row) - (long)(b.Row - a.Row) * (c.Col - b.Col);
                if (cross != 0 || b == a || b == c) keep.Add(b);
            }
            if (keep.Count == 0) keep.Add(ring[0]);
            keep.Add(keep[0]);
            return keep;
        }

        /// <summary>Shoelace area of a closed ring, positive when the ring runs clockwise in row-down grid space.</summary>
        public static double SignedArea(IReadOnlyList<(int Col, int Row)> ring)
        {
            long twice = 0;
            for (int k = 0; k + 1 < ring.Count; k++)
                twice += (long)ring[k].Col * ring[k + 1].Row - (long)ring[k + 1].Col * ring[k].Row;
            return twice / 2.0;
        }
    }
}
