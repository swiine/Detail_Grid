using System;
using System.Drawing;
using System.IO;
using TTWLinemarking.Core;

namespace TTWLinemarking.UI
{
    internal static class Ui
    {
        // Overall dialog size. Every size and font below goes through S()/F(), so this is the one
        // number to change if the dialogs feel too big or too small.
        public const float Scale = 1.25f;

        public static int S(int v) => (int)Math.Round(v * Scale);
        public static float F(float pt) => pt * Scale;
        public static Size S(int w, int h) => new Size(S(w), S(h));

        public static Font Regular(float pt = 9f) => new Font("Segoe UI", F(pt));
        public static Font Bold(float pt = 9f) => new Font("Segoe UI", F(pt), FontStyle.Bold);
        public static Font Mono(float pt = 9f, FontStyle style = FontStyle.Regular) => new Font("Consolas", F(pt), style);

        public static readonly Color Red = Color.FromArgb(184, 29, 19);
        public static readonly Color Yellow = Color.FromArgb(255, 191, 0);
        public static readonly Color Asphalt = Color.FromArgb(58, 58, 58);
        public static readonly Color Muted = Color.FromArgb(105, 105, 105);

        // On-screen colour for the paint. White paint is drawn white, on an asphalt background.
        public static Color PaintColor(Paint paint) =>
            paint == Paint.Red ? Red : paint == Paint.Yellow ? Yellow : Color.White;

        // ttw_logo.png next to the DLL, or null. Read into memory so the file isn't held locked.
        public static Image TryLoadLogo()
        {
            try
            {
                string path = Paths.LogoPath;
                if (path == null || !File.Exists(path)) return null;
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch
            {
                // A bad logo file should never stop the tool from opening.
                return null;
            }
        }
    }
}
