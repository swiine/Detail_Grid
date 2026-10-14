using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PavementBuildup.Export;

internal sealed class PdfWriter
{
    private const double Pt = 72 / 25.4;
    private readonly List<(double W, double H, byte[] Content)> _pages = new();
    private StringBuilder? _page;
    private double _pageW, _pageH;

    public string Title { get; set; } = "";
    public string Author { get; set; } = "TTW Pavement";

    public void BeginPage(double widthMm, double heightMm)
    {
        EndPage();
        _page = new StringBuilder();
        _pageW = widthMm;
        _pageH = heightMm;
        _page.Append("1 J 1 j\n");
    }

    private StringBuilder P => _page ?? throw new InvalidOperationException("Call BeginPage first.");

    private static string N(double v) => (Math.Abs(v) < 1e-7 ? 0 : v).ToString("0.###", CultureInfo.InvariantCulture);
    private static string M(double mm) => N(mm * Pt);

    public void Save() => P.Append("q\n");
    public void Restore() => P.Append("Q\n");
    public void LineWidth(double mm) => P.Append(M(mm)).Append(" w\n");
    public void Stroke(double r, double g, double b) => P.Append($"{N(r)} {N(g)} {N(b)} RG\n");
    public void Fill(double r, double g, double b) => P.Append($"{N(r)} {N(g)} {N(b)} rg\n");
    public void Stroke(string hex) { var (r, g, b) = Rgb(hex); Stroke(r, g, b); }
    public void Fill(string hex) { var (r, g, b) = Rgb(hex); Fill(r, g, b); }
    public void Dash(double onMm, double offMm) => P.Append(onMm <= 0 ? "[] 0 d\n" : $"[{M(onMm)} {M(offMm)}] 0 d\n");

    private static (double, double, double) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        int v = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
    }

    public void Line(double x1, double y1, double x2, double y2) =>
        P.Append($"{M(x1)} {M(y1)} m {M(x2)} {M(y2)} l S\n");

    public void Segment(double x1, double y1, double x2, double y2) => P.Append($"{M(x1)} {M(y1)} m {M(x2)} {M(y2)} l\n");
    public void StrokePath() => P.Append("S\n");

    public void Polyline(IReadOnlyList<(double X, double Y)> pts, bool closed = false, bool fill = false, bool stroke = true)
    {
        if (pts.Count < 2) return;
        Path(pts);
        if (closed) P.Append("h ");
        P.Append(fill && stroke ? "B\n" : fill ? "f\n" : "S\n");
    }

    public void Clip(IReadOnlyList<(double X, double Y)> pts)
    {
        Path(pts);
        P.Append("h W n\n");
    }

    public void Rect(double x, double y, double w, double h, bool fill = false, bool stroke = true) =>
        P.Append($"{M(x)} {M(y)} {M(w)} {M(h)} re {(fill && stroke ? "B" : fill ? "f" : "S")}\n");

    public void Circle(double cx, double cy, double r, bool fill = true)
    {
        const double k = 0.5523;
        P.Append($"{M(cx + r)} {M(cy)} m ");
        P.Append($"{M(cx + r)} {M(cy + k * r)} {M(cx + k * r)} {M(cy + r)} {M(cx)} {M(cy + r)} c ");
        P.Append($"{M(cx - k * r)} {M(cy + r)} {M(cx - r)} {M(cy + k * r)} {M(cx - r)} {M(cy)} c ");
        P.Append($"{M(cx - r)} {M(cy - k * r)} {M(cx - k * r)} {M(cy - r)} {M(cx)} {M(cy - r)} c ");
        P.Append($"{M(cx + k * r)} {M(cy - r)} {M(cx + r)} {M(cy - k * r)} {M(cx + r)} {M(cy)} c ");
        P.Append(fill ? "f\n" : "S\n");
    }

    private void Path(IReadOnlyList<(double X, double Y)> pts)
    {
        P.Append($"{M(pts[0].X)} {M(pts[0].Y)} m");
        for (int i = 1; i < pts.Count; i++) P.Append($" {M(pts[i].X)} {M(pts[i].Y)} l");
        P.Append(' ');
    }

    public enum Align { Left, Center, Right }

    public void Text(string text, double x, double y, double heightMm, bool bold = false, Align align = Align.Left, double angleDeg = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        double size = heightMm / 0.72;
        double width = Width(text, size, bold);
        double shift = align switch { Align.Center => -width / 2, Align.Right => -width, _ => 0 };
        double a = angleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        double ox = x + shift * c, oy = y + shift * s;
        P.Append($"BT /{(bold ? "F2" : "F1")} {N(size * Pt)} Tf {N(c)} {N(s)} {N(-s)} {N(c)} {M(ox)} {M(oy)} Tm (");
        P.Append(Escape(text)).Append(") Tj ET\n");
    }

    public static double Width(string text, double sizeMm, bool bold = false) =>
        text.Sum(ch => (ch is >= ' ' and <= '~' ? Widths[ch - ' '] : 556) * (bold ? 1.06 : 1)) / 1000 * sizeMm;

    public static double TextWidth(string text, double heightMm, bool bold = false) => Width(text, heightMm / 0.72, bold);

    private static readonly int[] Widths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            int code = ch switch
            {
                '€' => 0x80, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94, '•' => 0x95, '–' => 0x96, '—' => 0x97,
                '−' => '-', '≥' => -1, '≤' => -2, '→' => -3, '✔' => -4, '✖' => -5,
                _ => ch <= 0xFF ? ch : '?',
            };
            switch (code)
            {
                case -1: sb.Append(">="); continue;
                case -2: sb.Append("<="); continue;
                case -3: sb.Append("->"); continue;
                case -4: sb.Append("OK"); continue;
                case -5: sb.Append("X"); continue;
            }
            char c = (char)code;
            if (c is '(' or ')' or '\\') sb.Append('\\');
            if (c < ' ') { sb.Append(' '); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void EndPage()
    {
        if (_page is null) return;
        _pages.Add((_pageW, _pageH, Latin1.GetBytes(_page.ToString())));
        _page = null;
    }

    private static readonly Encoding Latin1 = Encoding.Latin1;

    public void Save(Stream stream)
    {
        EndPage();
        if (_pages.Count == 0) throw new InvalidOperationException("The PDF has no pages.");
        var objects = new List<byte[]>();
        int Add(string body) { objects.Add(Latin1.GetBytes(body)); return objects.Count; }

        int pageCount = _pages.Count;
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{6 + i * 2} 0 R"));
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Add($"<< /Title ({Escape(Title)}) /Author ({Escape(Author)}) /Creator (TTW Pavement) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");
        foreach (var (w, h, content) in _pages)
        {
            int contentObj = objects.Count + 2;
            Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {M(w)} {M(h)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObj} 0 R >>");
            using var packed = new MemoryStream();
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(content);
            var data = packed.ToArray();
            var head = Latin1.GetBytes($"<< /Length {data.Length} /Filter /FlateDecode >>\nstream\n");
            var tail = Latin1.GetBytes("\nendstream");
            objects.Add(head.Concat(data).Concat(tail).ToArray());
        }

        var offsets = new List<long>();
        void Write(byte[] b) => stream.Write(b, 0, b.Length);
        long pos = 0;
        void W(string s) { var b = Latin1.GetBytes(s); Write(b); pos += b.Length; }
        W("%PDF-1.4\n%âãÏÓ\n");
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(pos);
            W($"{i + 1} 0 obj\n");
            Write(objects[i]);
            pos += objects[i].Length;
            W("\nendobj\n");
        }
        long xref = pos;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
