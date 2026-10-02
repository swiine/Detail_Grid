using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(TTWLinemarking.PluginApp))]
[assembly: CommandClass(typeof(TTWLinemarking.Commands))]

namespace TTWLinemarking
{
    public class PluginApp : IExtensionApplication
    {
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"V{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public void Initialize()
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\nTTW Linemarking {Version} loaded. Type LINEMARKING (or 8), or ROAD for the code list.\n");
        }

        public void Terminate()
        {
        }
    }
}
