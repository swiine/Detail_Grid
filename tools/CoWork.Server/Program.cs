using CoWork.Core;

// CoWork-Server.exe [--port 5391] [--data "\\server\share\CoWork-data"] [--release "\\server\share\CoWork-release"]
//   --data     the CoWork-data folder: lets the server see licences you revoke (and drop those people straight away)
//   --release  the folder CoWork-Admin publishes updates to: the server hands updates out (still signed by you)
// Settings can also go in cowork-server.json next to the exe: { "port": 5391, "data": "...", "release": "..." }.
// --install: start with Windows (when this user logs in) and come back within 5 minutes if it ever stops;
// --uninstall removes that. Run them once, ideally from an admin command prompt (for the firewall rule).
if (args.Length > 0 && args[0].ToLowerInvariant() is "--install" or "--uninstall")
    return Setup(args[0].ToLowerInvariant() == "--install");

// Only one copy runs at a time (the scheduled task tries every 5 minutes; extra copies just leave).
using var single = new Mutex(true, "CoWork-Server-" + Environment.UserName, out bool first);
if (!first) { Console.WriteLine("The CoWork server is already running on this PC."); return 0; }

int port = CoWorkServer.DefaultPort;
string? data = null, release = null;
string config = Path.Combine(AppContext.BaseDirectory, "cowork-server.json");
if (File.Exists(config))
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(config));
        foreach (var p in doc.RootElement.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "port" when p.Value.TryGetInt32(out int n): port = n; break;
                case "data": data = p.Value.GetString(); break;
                case "release": release = p.Value.GetString(); break;
            }
    }
    catch (Exception ex) { Console.WriteLine($"Couldn't read {config}: {ex.Message}"); }
}
for (int i = 0; i < args.Length - 1; i++)
    switch (args[i].ToLowerInvariant())
    {
        case "--port" or "-p" when int.TryParse(args[i + 1], out int n): port = n; break;
        case "--data": data = args[i + 1]; break;
        case "--release": release = args[i + 1]; break;
    }

using var server = new CoWorkServer(port, data, release, msg => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {msg}"));
try { server.Start(); }
catch (System.Net.Sockets.SocketException ex)
{
    Console.WriteLine($"Couldn't listen on port {port}: {ex.Message}");
    return 1;
}
Console.WriteLine($"CoWork server running on {Environment.MachineName}:{port}");
Console.WriteLine($"  Licences revoked: {(data == null ? "not checked (no --data folder)" : "read from " + data)}");
Console.WriteLine($"  Updates:          {(release == null ? "not served (no --release folder)" : "served from " + release)}");
Console.WriteLine($"In cowork.json put:  \"server\": \"{Environment.MachineName}:{port}\"");
Console.WriteLine("Press Ctrl+C to stop.");
var done = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
done.Wait();
return 0;

static int Setup(bool install)
{
    string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CoWork-Server.exe");
    const string task = "CoWork Server";
    static (int Code, string Output) Run(string file, string arguments)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments)
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    if (!install)
    {
        Run("schtasks", $"/Delete /TN \"{task}\" /F");
        Run("netsh", "advfirewall firewall delete rule name=\"CoWork server\"");
        Console.WriteLine("Removed: the CoWork server won't start by itself any more.");
        return 0;
    }

    // Every 5 minutes while this user is logged in: starts it after logon, and again if it ever stops.
    // It runs minimised; closing the window stops it until the next 5-minute check.
    var (code, output) = Run("schtasks",
        $"/Create /TN \"{task}\" /SC MINUTE /MO 5 /TR \"cmd /c start \\\"CoWork server\\\" /min \\\"{exe}\\\"\" /F");
    Console.WriteLine(code == 0 ? "Installed: the CoWork server starts by itself (checked every 5 minutes)." : "Couldn't set it to start by itself: " + output);

    var (fw, fwOut) = Run("netsh", "advfirewall firewall delete rule name=\"CoWork server\"");
    (fw, fwOut) = Run("netsh", $"advfirewall firewall add rule name=\"CoWork server\" dir=in action=allow protocol=TCP localport={CoWorkServer.DefaultPort},5392 profile=domain,private");
    Console.WriteLine(fw == 0 ? "Firewall: other PCs can now reach it." : "Firewall: not changed (run this from an admin command prompt, or ask IT to allow inbound TCP 5391).");
    Console.WriteLine("Starting it now...");
    Run("schtasks", $"/Run /TN \"{task}\"");
    return code == 0 ? 0 : 1;
}
