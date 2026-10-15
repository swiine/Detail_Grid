using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PavementBuildup.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace PavementBuildup;

internal static class Training
{
    public const string FolderName = "TTW Pavement Training";
    public const string SampleBuildup = "[TYPE A - CARPARK]\n40mm AC10 wearing course\nprime\n150mm DGB20 base\n200mm DGS20 subbase\nsubgrade CBR 5%";
    public const string SampleNote = "PAVEMENT TYPE B - FOOTPATH\n100mm 32MPa CONCRETE WITH SL72 MESH\n75mm DGB20 BASE\nSUBGRADE COMPACTED TO 98% SMDD";

    private static TrainingForm? _form;

    public static bool IsTrainingDrawing(Document? doc) =>
        doc?.Name is { } name && name.Contains(FolderName, StringComparison.OrdinalIgnoreCase);

    public static void Start()
    {
        var folder = Path.Combine(Path.GetTempPath(), FolderName);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"TTW_Pvmt_Training_{DateTime.Now:HHmmss}.dwg");

        var store = PresetStore.Default();
        var settings = store.Load().Settings;
        CadStandard standard;
        try { standard = StandardStore.Load(StandardStore.PathFor(settings)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { standard = CadStandard.CreateDefault(); }
        var template = standard.ResolveStandardDrawing();

        Document doc;
        if (template is not null && File.Exists(template))
        {
            File.Copy(template, path, overwrite: true);
            File.SetAttributes(path, FileAttributes.Normal);
            doc = AcApp.DocumentManager.Open(path, false);
        }
        else
        {
            doc = AcApp.DocumentManager.Add("acadiso.dwt");
        }
        AcApp.DocumentManager.MdiActiveDocument = doc;

        using (doc.LockDocument())
        {
            var db = doc.Database;
            double m = DetailDrawer.ResolveUnitsPerMm(db, settings, standard) * 1000;
            using var tr = db.TransactionManager.StartTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            void Add(Entity e) { ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            void Label(string text, double x, double y) => Add(new MText { Location = new Point3d(x * m, y * m, 0), Contents = text, TextHeight = 0.6 * m });
            Polyline Poly(params (double X, double Y, double Bulge)[] pts)
            {
                var pl = new Polyline { Closed = true };
                for (int i = 0; i < pts.Length; i++) pl.AddVertexAt(i, new Point2d(pts[i].X * m, pts[i].Y * m), pts[i].Bulge, 0, 0);
                return pl;
            }

            Label("1  SAMPLE NOTE  -  TTWPVMT > Detail from a note, click this text", 0, 8);
            Add(new MText { Location = new Point3d(0, 6.5 * m, 0), Contents = SampleNote.Replace("\n", "\\P"), TextHeight = 0.25 * m });

            if (Features.Jointing)
            {
                Label("SAMPLE SLAB with a pit  -  TTWPVMT > Joint layout", 0, -2);
                Add(Poly((0, -32, 0), (30, -32, 0), (30, -22, 0), (17, -22, 0), (17, -10, 0), (0, -10, 0)));
                Add(Poly((6, -27, 0), (7, -27, 0), (7, -26, 0), (6, -26, 0)));
                Label("SAMPLE CURVED PATH  -  TTWPVMT > Joint layout, Across the width", 40, -2);
                double b = Math.Tan(Math.PI / 8);
                Add(Poly((40, -30, 0), (52, -30, b), (64, -18, 0), (64, -4, 0), (61, -4, 0), (61, -18, -b), (52, -27, 0), (40, -27, 0)));
            }

            Label("2  SAMPLE SHEET (A3 at 1:10)  -  TTWPVMT > Arrange details: pick this frame, then the details you drew", 75, -2);
            var sheet = Poly((75, -10.42, 0), (75 + 4.2, -10.42, 0), (75 + 4.2, -10.42 + 2.97, 0), (75, -10.42 + 2.97, 0));
            Add(sheet);
            tr.Commit();
        }
        doc.SendStringToExecute("_.ZOOM _E ", true, false, false);

        _form?.Close();
        _form = new TrainingForm();
        _form.FormClosed += (_, _) => _form = null;
        Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessDialog(_form);
    }
}

internal sealed class TrainingForm : Form
{
    private static readonly (string Title, string Detail, string? Copy)[] Steps =
    {
        ("New pavement detail", "Type TTWPVMT > 1 New pavement detail. Paste the sample build-up (button below), check the picture, Place in drawing, click a spot.", Training.SampleBuildup),
        ("Detail from a note", "TTWPVMT > 3 Detail from a note. Click the SAMPLE NOTE text (area 1). Check it read the layers, then place it.", null),
        ("Edit a detail", "TTWPVMT > 2 Edit a detail. Click one of your details, change a thickness, Update detail. All copies change.", null),
        ("Arrange details on a sheet", "TTWPVMT > Arrange details on a sheet. Click the SAMPLE SHEET frame (area 2), then select the details you drew. They're packed in without overlaps.", null),
        ("Report a problem", "TTWPVMT > Report a problem (link at the bottom of the menu). See what it collects - then Cancel.", null),
    };

    private static readonly (string Title, string Detail, string? Copy)[] JointSteps =
    {
        ("Joints on an L-shaped slab", "TTWPVMT > 5 Joint layout. Click the slab (area 2), then the small pit, Enter. Try the set-out options and click a corner in the picture. Draw joints.", null),
        ("Joints on a curved path", "TTWPVMT > 5 Joint layout on the path (area 3). Across the width only: joints come out radial on the curve. Draw joints.", null),
    };

    public TrainingForm()
    {
        Text = "TTW Pavement training";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Brand.Px(60), Brand.Px(120));
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        TopMost = false;
        Size = new Size(Brand.Px(460), Brand.Px(640));

        var list = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
        list.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(Brand.Px(410), 0), ForeColor = Brand.Muted, Margin = new Padding(3, 0, 3, 8),
            Text = "This is a practice drawing in your temp folder: close it without saving when you're done. Training use isn't counted in the time saved report.",
        });
        int n = 1;
        foreach (var (title, detail, copy) in Features.Jointing ? Steps.Concat(JointSteps) : Steps)
        {
            var check = new CheckBox { Text = $"{n++}  {title}", AutoSize = true, Font = Brand.Sized(10.5f, FontStyle.Bold), ForeColor = Brand.Heading, Margin = new Padding(3, 8, 3, 0) };
            var text = new Label { Text = detail, AutoSize = true, MaximumSize = new Size(Brand.Px(400), 0), Margin = new Padding(22, 2, 3, 2) };
            check.CheckedChanged += (_, _) => text.ForeColor = check.Checked ? Brand.Muted : Brand.Text;
            list.Controls.Add(check);
            list.Controls.Add(text);
            if (copy is not null)
            {
                var button = new Button { Text = "Copy the sample build-up", AutoSize = true, Margin = new Padding(22, 2, 3, 2) };
                button.Click += (_, _) => Clipboard.SetText(copy);
                list.Controls.Add(button);
            }
        }
        var close = new Button { Text = "Finish training", AutoSize = true, Margin = new Padding(3, 14, 3, 3) };
        close.Click += (_, _) => Close();
        list.Controls.Add(close);
        Controls.Add(list);
        Brand.Decorate(this, "Training", "Practice drawing  ·  work down the list");
        list.BringToFront();
    }
}
