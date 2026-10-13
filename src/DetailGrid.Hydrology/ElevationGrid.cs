using System;

namespace DetailGrid.Hydrology
{
    /// <summary>
    /// Regular elevation raster sampled from a surface. Row 0 is the NORTH (top) row and
    /// cells are stored row-major: index = row * Width + col. Missing data is NaN.
    /// </summary>
    public sealed class ElevationGrid
    {
        public int Width { get; }
        public int Height { get; }
        public double CellSize { get; }

        /// <summary>World X of the grid's left (west) edge.</summary>
        public double OriginX { get; }

        /// <summary>World Y of the grid's top (north) edge.</summary>
        public double OriginY { get; }

        public float[] Z { get; }

        public ElevationGrid(int width, int height, double cellSize, double originX, double originY, float[]? z = null)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Grid must have at least one cell.");
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Width = width;
            Height = height;
            CellSize = cellSize;
            OriginX = originX;
            OriginY = originY;
            if (z != null && z.Length != width * height) throw new ArgumentException("Elevation array does not match grid size.", nameof(z));
            Z = z ?? CreateEmpty(width * height);
        }

        private static float[] CreateEmpty(int n)
        {
            var z = new float[n];
            Array.Fill(z, float.NaN);
            return z;
        }

        public int Count => Width * Height;

        public int Index(int row, int col) => row * Width + col;

        public bool InBounds(int row, int col) => row >= 0 && col >= 0 && row < Height && col < Width;

        public (double X, double Y) CellCenter(int index)
        {
            int row = index / Width, col = index - row * Width;
            return (OriginX + (col + 0.5) * CellSize, OriginY - (row + 0.5) * CellSize);
        }

        /// <summary>World coordinates of grid vertex (col, row); vertex (0,0) is the top-left corner.</summary>
        public (double X, double Y) Vertex(int col, int row) => (OriginX + col * CellSize, OriginY - row * CellSize);

        public bool TryWorldToCell(double x, double y, out int row, out int col)
        {
            col = (int)Math.Floor((x - OriginX) / CellSize);
            row = (int)Math.Floor((OriginY - y) / CellSize);
            return InBounds(row, col);
        }
    }
}
