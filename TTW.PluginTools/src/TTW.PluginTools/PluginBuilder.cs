using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace TTW.PluginTools
{
    public sealed class BuildResult
    {
        public bool Success { get; init; }
        /// <summary>The compiler's output (errors and warnings).</summary>
        public string Output { get; init; }
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>
    /// Compiles a plugin's .csproj with the .NET SDK (dotnet build), straight into the plugin's
    /// folder. Needs the .NET 8 SDK on the PC - Visual Studio installs it.
    /// </summary>
    public static class PluginBuilder
    {
        public static async Task<BuildResult> BuildAsync(string project, string outputDir)
        {
            var sw = Stopwatch.StartNew();
            var output = new StringBuilder();
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // -nodeReuse:false stops build processes hanging around holding files
            foreach (var arg in new[] { "build", project, "-c", "Release", "-o", outputDir, "-nologo", "-v:minimal", "-nodeReuse:false" })
                psi.ArgumentList.Add(arg);

            try
            {
                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                return new BuildResult { Success = process.ExitCode == 0, Output = output.ToString(), Elapsed = sw.Elapsed };
            }
            catch (Win32Exception)
            {
                return new BuildResult
                {
                    Success = false,
                    Output = "Couldn't start the compiler (dotnet). Install the .NET 8 SDK, or Visual Studio 2022 with \".NET desktop development\".",
                    Elapsed = sw.Elapsed,
                };
            }
        }
    }
}
