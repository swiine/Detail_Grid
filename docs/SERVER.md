# CoWork with or without a server

CoWork has two modes. Both use the same add-in, and one setting picks which.

| | Shared folder only (default) | With the CoWork server |
|---|---|---|
| Setup | Nothing extra | Run `CoWork-Server.exe` on one PC or server that stays on |
| Chat and who's online | Read from the shared folder every 0.5 s | Arrive the moment they're sent; the folder is checked every 3 s as a backstop |
| CoWork sessions (changes) | Folder checked every 0.2 s | Others are told as soon as a change is saved; folder checked every 1 s as a backstop |
| Crosshairs and locks | About 5–10 updates a second | About 30 a second, passed straight through the server |
| Licences | Checked on each PC | Also checked by the server when someone connects; revoked people are dropped within a minute |
| Updates | From the release folder | From the server, falling back to the release folder (still signed by you, so the server can't change them) |
| If the server is off | n/a | CoWork carries on through the shared folder, as if there were no server |

The shared folder is still where everything is kept in both modes: chats, sessions, drawings' changes, licences
and logs. The server stores nothing. It only passes messages along, so restarting it loses nothing.

## You may not need one

When a CoWork session starts and there's no office server, the host's CoWork starts a small server for that session
inside Civil 3D (port 5392) and guests connect to it automatically - sessions are instant without running anything.
Windows may ask the host's PC to allow Civil 3D (acad.exe) through the firewall the first time: choose
"Private/Domain networks" (IT may need to approve it, or add a rule for acad.exe or TCP port 5392). If the connection is
blocked, the session still works through the shared folder, just slower. The office server is still worth running for
instant chat, "is typing", licence checks and updates.

## Run the server

1. Copy the `CoWork-Server` folder to the PC or server that will run it. It needs the .NET 8 runtime, which any
   PC with Civil 3D 2025 already has. On a bare server, install the ".NET 8 Runtime" from Microsoft.
2. Edit `cowork-server.json` next to the exe:
   ```json
   {
     "port": 5391,
     "data": "\\\\server\\share\\...\\CoWork-data",
     "release": "\\\\server\\share\\...\\CoWork-release"
   }
   ```
   * `data` is the CoWork-data folder. With it, the server knows about licences you revoke.
   * `release` is the folder CoWork-Admin publishes to. With it, the server hands out updates.
   * Both are optional.
3. Run `CoWork-Server.exe`. It prints the line to put in cowork.json, e.g. `"server": "OFFICE-PC1:5391"`.
4. Allow incoming TCP on port 5391 in that PC's firewall. Windows asks the first time it runs; choose
   "Private/Domain networks".
5. To keep it running after logging off, run it as a scheduled task "At startup", or ask IT to wrap it as a service.

## Switch CoWork to the server

* **Try it on your PC first:** type `CWSERVER` in Civil 3D and enter the server's name, e.g. `OFFICE-PC1`.
  `CWSERVER` again (or Settings → Server) shows whether it connected. Enter `.` to go back to the office default.
* **Everyone:** put the address in `cowork.json` in CoWork-Admin's `app` folder (`"server": "OFFICE-PC1:5391"`)
  and publish (option 4). Everyone picks it up the next time they open Civil 3D.
* **Back to folder-only:** set `"server": ""` and publish again.

Updates *through the server* need the new loader (CoWork 1.3.0's installer). PCs installed from an older zip still
update from the release folder, which works as before.

## Office or cloud

The server is the same program either way.

* **Office (recommended):** any always-on PC on the office network. Nothing leaves the network.
* **Cloud:** it runs on a small Windows or Linux VM (e.g. Azure). The messages aren't encrypted, so connect over
  your VPN instead of opening the port to the internet. Everyone still needs the shared folder, because that's
  where the chats and drawings live.

## What the server checks

* Every connection must show a genuine, unexpired, unrevoked licence for the Windows login it gives. Without one it
  is refused, and refused PCs simply carry on through the shared folder.
* It only hands out files listed in a release's signed manifest, and the loader re-checks every file's signature
  before running anything.
