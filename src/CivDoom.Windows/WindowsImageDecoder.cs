using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CivDoom.Engine;

namespace CivDoom.Windows;

/// <summary>Reads PNG, JPEG, BMP and GIF pictures for monster and weapon designs using Windows' own decoders.</summary>
public static class WindowsImageDecoder
{
    public static void Install() => ImageSprites.Decoder = Decode;

    public static (int Width, int Height, int[] Argb)? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var bmp = new Bitmap(ms);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            using Bitmap argb = bmp.Clone(rect, PixelFormat.Format32bppArgb);
            BitmapData data = argb.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new int[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width, bmp.Width);
                return (bmp.Width, bmp.Height, px);
            }
            finally
            {
                argb.UnlockBits(data);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            // GDI+ reports unreadable files in odd ways; fall back to the engine's own PNG reader.
            return PngDecoder.Decode(bytes);
        }
    }
}
