using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(DerZombies.Civil3D.Commands))]

namespace DerZombies.Civil3D
{
    public sealed class Commands
    {
        /// <summary>Builds the castle (plus the Civil 3D surface and COGO points) and starts a game.</summary>
        [CommandMethod("DERZOMBIES")]
        public void Play()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            if (System.Convert.ToInt16(AcApp.GetSystemVariable("TILEMODE")) == 0)
                AcApp.SetSystemVariable("TILEMODE", 1);

            ed.WriteMessage(
                "\n=== DER EISENDRACHE - Civil 3D Zombies ===" +
                "\nThe castle is drawn on DE-* layers at 0,0 (run this in an empty drawing)." +
                "\nWASD move | mouse aim | L-click/Space fire | R reload | Q swap | E buy/use | V knife | G grenade | P pause | Esc quit" +
                "\nTurn on the power in Mission Control, activate the 3 landing pads for Pack-a-Punch," +
                "\nand feed the 3 dragons to earn the Wrath of the Ancients.\n");

            GameSession.Start(doc, new Vector3d(0, 0, 0), buildCivil: true);
        }

        /// <summary>First-person mode: the castle as 3D solids and the camera at your eyes.</summary>
        [CommandMethod("DERZ3D")]
        public void PlayFirstPerson()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (System.Convert.ToInt16(AcApp.GetSystemVariable("TILEMODE")) == 0)
                AcApp.SetSystemVariable("TILEMODE", 1);

            doc.Editor.WriteMessage(
                "\n=== DER EISENDRACHE - first person ===" +
                "\nThe castle is built as 3D solids on DE-* layers at 0,0 (run this in an empty drawing)." +
                "\nMouse look | WASD move | L-click/Space fire | R reload | Q swap | E buy/use | V knife | G grenade" +
                "\nTab frees the mouse | P pause | Esc quit (your view and visual style are restored).\n");

            GameSession.Start(doc, new Vector3d(0, 0, 0), buildCivil: true, firstPerson: true);
        }

        [CommandMethod("DERZQUIT")]
        public void Quit() => GameSession.Stop();

        /// <summary>Draws the map only (no game), e.g. to plot the "site plan".</summary>
        [CommandMethod("DERZMAP")]
        public void MapOnly()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var map = DerZombies.Core.GameMap.CreateDefault();
            MapBuilder.Clear(doc.Database);
            MapBuilder.Build(doc.Database, map, new Vector3d(0, 0, 0));
            try { doc.Editor.WriteMessage("\n" + CivilSite.Build(doc.Database, map, new Vector3d(0, 0, 0))); }
            catch (System.Exception ex) { doc.Editor.WriteMessage($"\nCivil 3D surface/points skipped: {ex.Message}"); }
        }

        /// <summary>Removes everything the game drew (DE-* layers).</summary>
        [CommandMethod("DERZCLEAN")]
        public void Clean()
        {
            GameSession.Stop();
            MapBuilder.Clear(AcApp.DocumentManager.MdiActiveDocument.Database);
        }
    }
}
