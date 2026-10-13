using System.Linq;
using DetailGrid.Hydrology;
using Xunit;

namespace DetailGrid.Hydrology.Tests
{
    public class BoundaryTracerTests
    {
        private static bool[] Mask(params string[] rows) =>
            rows.SelectMany(r => r.Select(ch => ch == '#')).ToArray();

        [Fact]
        public void Rectangle_HasFourCorners()
        {
            var rings = BoundaryTracer.Trace(Mask(
                "....",
                ".##.",
                ".##.",
                "...."), 4, 4);
            var ring = Assert.Single(rings);
            Assert.Equal(5, ring.Count); // 4 corners + closing vertex
            Assert.Equal(ring[0], ring[^1]);
            Assert.Equal(4.0, BoundaryTracer.SignedArea(ring));
        }

        [Fact]
        public void DiagonalPinch_IsOneRing()
        {
            var rings = BoundaryTracer.Trace(Mask(
                "#.",
                ".#"), 2, 2);
            var ring = Assert.Single(rings);
            Assert.Equal(2.0, BoundaryTracer.SignedArea(ring));
        }

        [Fact]
        public void Hole_IsSeparateRingWithOppositeWinding()
        {
            var rings = BoundaryTracer.Trace(Mask(
                "###",
                "#.#",
                "###"), 3, 3);
            Assert.Equal(2, rings.Count);
            var areas = rings.Select(BoundaryTracer.SignedArea).OrderBy(a => a).ToArray();
            Assert.Equal(-1.0, areas[0]);
            Assert.Equal(9.0, areas[1]);
        }

        [Fact]
        public void LShape_AreaMatchesCellCount()
        {
            var mask = Mask(
                "#...",
                "#...",
                "####");
            var ring = Assert.Single(BoundaryTracer.Trace(mask, 4, 3));
            Assert.Equal(mask.Count(b => b), BoundaryTracer.SignedArea(ring));
            Assert.Equal(7, ring.Count); // 6 corners + closing vertex
        }
    }
}
