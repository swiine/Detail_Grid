using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(BlockLayerChanger.Commands))]

namespace BlockLayerChanger
{
    public class Commands
    {
        /// <summary>Opens the Block Layer Changer dialog.</summary>
        [CommandMethod("BLOCKLAYERCHANGE", CommandFlags.Modal)]
        public void BlockLayerChange()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (var form = new LayerChangerForm(doc))
            {
                AcApp.ShowModalDialog(form);
            }
        }

        /// <summary>Short alias for BLOCKLAYERCHANGE.</summary>
        [CommandMethod("BLC", CommandFlags.Modal)]
        public void Blc() => BlockLayerChange();
    }
}
