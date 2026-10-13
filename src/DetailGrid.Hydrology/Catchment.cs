using System.Collections.Generic;

namespace DetailGrid.Hydrology
{
    /// <summary>The set of cells draining to one pour point.</summary>
    public sealed class Catchment
    {
        public FlowModel Model { get; }
        public int Outlet { get; }
        public bool[] Mask { get; }
        public int CellCount { get; }

        internal Catchment(FlowModel model, int outlet, bool[] mask, int cellCount)
        {
            Model = model;
            Outlet = outlet;
            Mask = mask;
            CellCount = cellCount;
        }

        public double Area => CellCount * Model.Grid.CellSize * Model.Grid.CellSize;

        /// <summary>Highest and lowest surface elevation inside the catchment.</summary>
        public (double Min, double Max) ElevationRange()
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            var z = Model.Grid.Z;
            for (int i = 0; i < Mask.Length; i++)
            {
                if (!Mask[i]) continue;
                if (z[i] < min) min = z[i];
                if (z[i] > max) max = z[i];
            }
            return (min, max);
        }

        /// <summary>Boundary rings in grid-vertex coordinates; see <see cref="BoundaryTracer"/>.</summary>
        public List<List<(int Col, int Row)>> BoundaryRings() => BoundaryTracer.Trace(Mask, Model.Grid.Width, Model.Grid.Height);

        /// <summary>Boundary rings in world coordinates, each closed (first point repeated last).</summary>
        public List<List<(double X, double Y)>> BoundaryWorld()
        {
            var result = new List<List<(double X, double Y)>>();
            foreach (var ring in BoundaryRings())
            {
                var pts = new List<(double X, double Y)>(ring.Count);
                foreach (var (col, row) in ring) pts.Add(Model.Grid.Vertex(col, row));
                result.Add(pts);
            }
            return result;
        }
    }
}
