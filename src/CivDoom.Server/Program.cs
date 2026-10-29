using CivDoom.Engine;

// CivDoom.Server.exe [--port 5390]
// Runs until closed (Ctrl+C). Players set CIVDOOMNET to this PC's name (and the port, if not 5390).
int port = GameServer.DefaultPort;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out int p)) port = p;

using var server = new GameServer(port, msg => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {msg}"));
try
{
    server.Start();
}
catch (System.Net.Sockets.SocketException ex)
{
    Console.WriteLine($"Couldn't listen on port {port}: {ex.Message}");
    return 1;
}
Console.WriteLine($"Players: type CIVDOOMNET in Civil 3D and enter {Environment.MachineName}{(port == GameServer.DefaultPort ? "" : ":" + port)}");
Console.WriteLine("Press Ctrl+C to stop.");
var done = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
done.Wait();
return 0;
