using System;
using System.Linq;
using DetailGrid.Hydrology;
using Xunit;

namespace DetailGrid.Hydrology.Tests
{
    public class FlowModelTests
    {
        private static ElevationGrid Grid(int w, int h, Func<int, int, double> f)
        {
            var z = new float[w * h];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    z[r * w + c] = (float)f(r, c);
            return new ElevationGrid(w, h, 1.0, 0, h, z);
        }

        [Fact]
        public void InclinedPlane_AllFlowLeavesSouthEdge()
        {
            // Elevation falls toward the south (increasing row).
            var model = FlowModel.Build(Grid(5, 5, (r, c) => 10 - r));
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 5; c++)
                    Assert.Equal(2, model.Direction[r * 5 + c]); // D8 code 2 = south
            Assert.All(Enumerable.Range(20, 5), i => Assert.Equal(5, model.Accumulation[i]));
        }

        [Fact]
        public void Pit_IsFilledAndDrainsOut()
        {
            // Bowl with a single low spill point on the east edge.
            var grid = Grid(7, 7, (r, c) => Math.Max(Math.Abs(r - 3), Math.Abs(c - 3)) + 1.0);
            grid.Z[3 * 7 + 6] = 0.5f; // spill
            var model = FlowModel.Build(grid);

            // Every cell must reach the edge by following D8.
            for (int i = 0; i < grid.Count; i++)
            {
                var path = model.FlowPath(i);
                Assert.True(path.Count <= grid.Count, "flow path loops");
                Assert.Equal(FlowModel.Outlet, model.Direction[path[^1]]);
            }

            // The whole bowl interior drains through the spill cell.
            var catchment = model.Delineate(3 * 7 + 6);
            for (int r = 1; r < 6; r++)
                for (int c = 1; c < 6; c++)
                    Assert.True(catchment.Mask[r * 7 + c]);
        }

        [Fact]
        public void Flat_GetsAGradientWithoutCycles()
        {
            var model = FlowModel.Build(Grid(20, 20, (r, c) => 100));
            for (int i = 0; i < model.Grid.Count; i++)
                Assert.Equal(FlowModel.Outlet, model.Direction[model.FlowPath(i)[^1]]);
            Assert.Equal(400, Enumerable.Range(0, 400).Where(i => model.Direction[i] == FlowModel.Outlet).Sum(i => model.Accumulation[i]));
        }

        [Fact]
        public void Ridge_SplitsCatchments()
        {
            // V-shaped valley along col 5 draining south, ridge at the edges.
            var model = FlowModel.Build(Grid(11, 30, (r, c) => Math.Abs(c - 5) * 2 + (30 - r) * 0.1));
            int outlet = 29 * 11 + 5;
            var whole = model.Delineate(outlet);
            Assert.True(whole.CellCount > 250);

            int midValley = 15 * 11 + 5;
            var upper = model.Delineate(midValley);
            Assert.True(upper.CellCount < whole.CellCount);
            Assert.True(upper.Mask[0 * 11 + 5]);
            Assert.False(upper.Mask[20 * 11 + 5]);
            Assert.Equal(upper.CellCount, model.Accumulation[midValley]);
        }

        [Fact]
        public void CatchmentSize_MatchesAccumulationEverywhere()
        {
            var rnd = new Random(7);
            var model = FlowModel.Build(Grid(25, 25, (r, c) => rnd.NextDouble() * 5 + r * 0.3));
            for (int i = 0; i < model.Grid.Count; i += 13)
                Assert.Equal(model.Accumulation[i], model.Delineate(i).CellCount);
        }

        [Fact]
        public void NoData_IsExcludedAndActsAsSink()
        {
            var grid = Grid(9, 9, (r, c) => Math.Abs(r - 4) + Math.Abs(c - 4));
            grid.Z[4 * 9 + 4] = float.NaN;
            var model = FlowModel.Build(grid);
            Assert.Equal(FlowModel.NoData, model.Direction[4 * 9 + 4]);
            Assert.Equal(0, model.Delineate(4 * 9 + 4).CellCount);
            Assert.Equal(FlowModel.Outlet, model.Direction[3 * 9 + 4]);
        }

        [Fact]
        public void Snap_MovesToChannel()
        {
            var model = FlowModel.Build(Grid(11, 30, (r, c) => Math.Abs(c - 5) * 2 + (30 - r) * 0.1));
            int clicked = 20 * 11 + 7;
            int snapped = model.SnapPourPoint(clicked, 3);
            Assert.Equal(5, snapped % 11); // onto the channel
            Assert.True(model.Accumulation[snapped] > model.Accumulation[clicked]);
        }

        [Fact]
        public void WorldCellRoundTrip()
        {
            var grid = new ElevationGrid(10, 8, 2.5, 1000, 5000);
            Assert.True(grid.TryWorldToCell(1001, 4999, out int r, out int c));
            Assert.Equal((0, 0), (r, c));
            var (x, y) = grid.CellCenter(grid.Index(3, 4));
            Assert.True(grid.TryWorldToCell(x, y, out r, out c));
            Assert.Equal((3, 4), (r, c));
            Assert.False(grid.TryWorldToCell(999, 4999, out _, out _));
        }
    }
}
