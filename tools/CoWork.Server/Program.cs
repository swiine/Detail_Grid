using CoWork.Core;

// CoWork-Server.exe [--port 5391] [--data "\\server\share\CoWork-data"] [--release "\\server\share\CoWork-release"]
//   --data     the CoWork-data folder: lets the server see licences you revoke (and drop those people straight away)
//   --release  the folder CoWork-Admin publishes updates to: the server hands updates out (still signed by you)
// Settings can also go in cowork-server.json next to the exe: { "port": 5391, "data": "...", "release": "..." }.
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
