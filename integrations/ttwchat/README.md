# TTW Chat changes for CivDOOM multiplayer

`civdoom-multiplayer.patch` is the whole change to TTW Chat (apply with `git apply` or `patch -p1` in the TTWChat
folder):

- **server/Program.cs** – a `relay` message: passed straight on to everyone else in the channel and never stored.
  CivDOOM games use it in `doom-<game>` channels when TTW Chat is set to use the server.
- **plugin/src/ChatService.cs, ChatPanel.cs** – a **Join game** button on `[Doom] ... (game <id>)` invites, which
  runs `CIVDOOMJOIN <id>`.

Shared-folder mode needs no change to TTW Chat at all: CivDOOM writes its games to `TTWChat\doom\` and posts the
invite as an ordinary chat message (without the patched palette it shows as text: type `CIVDOOMJOIN <id>`).
