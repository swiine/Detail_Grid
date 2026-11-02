# CoWork – Teams link: request for IT review

CoWork is an in-house Civil 3D add-in (chat and live multiplayer drafting between colleagues). It has an
optional **Teams link** that lets a signed-in user, from inside Civil 3D:

- see the Teams status (Available / Busy / In a meeting …) of the colleagues on their CoWork friends list, and
- read and send their own **1:1 Teams chats** with those colleagues (the messages are normal Teams messages and appear in Teams too).

The Teams link is **switched off** in the shipped build. It only switches on once IT creates an app registration and
puts its two ids in the add-in's settings file (see *Switching it on*). Nothing below happens until then.

## What we're asking for

1. An **app registration** in our Microsoft Entra ID tenant.
2. **Admin consent** for four delegated Microsoft Graph permissions (below).
3. The **Directory (tenant) ID** and **Application (client) ID** of that registration.

### App registration settings

| Setting | Value |
|---|---|
| Name | CoWork (Civil 3D) |
| Supported account types | Accounts in this organizational directory only (single tenant) |
| Platform | **Mobile and desktop applications** |
| Redirect URI | `http://localhost` |
| Allow public client flows | **Yes** (Authentication → Advanced settings) |
| Client secret / certificate | **None** – it's a public desktop client; no secret is stored anywhere |
| Application (app-only) permissions | **None** |

### Delegated Microsoft Graph permissions

All are **delegated**: the add-in can only do what the signed-in user could already do in Teams themselves, and only
while that user is signed in.

| Permission | Admin consent needed | Why |
|---|---|---|
| `User.Read` | No | Read the signed-in user's own name, email and id. |
| `User.ReadBasic.All` | No | Find a colleague's Teams account from their work email (name, email, id only). |
| `Presence.Read.All` | Yes | Show the Teams status of the user's friends. |
| `Chat.ReadWrite` | Yes | Read and send the user's own 1:1 chats with a friend, and start one if it doesn't exist yet. |

Granting tenant-wide admin consent avoids every user seeing a consent prompt.

### Graph calls the add-in makes (the complete list)

| Call | When |
|---|---|
| `GET /me` | After sign-in. |
| `GET /users/{email}` | Once per friend, to find their Teams id. |
| `POST /communications/getPresencesByUserId` | Every 30 s while signed in, for the user's friends only. |
| `POST /chats` (`chatType: oneOnOne`) | When the user clicks *Teams chat* on a friend (returns the existing chat). |
| `GET /chats/{id}/messages?$top=30` | Every 6 s, only for Teams chats the user has opened in the palette this session. |
| `POST /chats/{id}/messages` | When the user sends a message. |

If Graph throttles (HTTP 429), the add-in waits for the `Retry-After` time before calling again.

## Data handling

- **Sign-in** uses Microsoft's own sign-in page in the user's default browser (MSAL, authorization code + PKCE).
  Conditional Access, MFA and sign-in logs all apply as normal. The add-in never sees the password.
- **Tokens** are cached per Windows user in `%LOCALAPPDATA%\CoWork\teams.cache`, encrypted with Windows
  DPAPI (only that Windows account can read it). *Sign out* (or `CWTEAMS` → Out) removes them.
- **No server.** The add-in talks directly to Microsoft Graph (`graph.microsoft.com`) and Entra sign-in
  (`login.microsoftonline.com`). Nothing goes to any third party.
- **Nothing from Teams is written to disk.** Teams messages and statuses are kept in memory while Civil 3D is open.
  (CoWork's own chat lives in a shared folder on our network; Teams messages are never copied there.)
- The only Teams-related thing written to the shared folder is the user's **Teams user id** (a GUID) in their
  "online" note, so friends' add-ins can find them without a directory lookup.
- Libraries: Microsoft.Identity.Client and Microsoft.Identity.Client.Extensions.Msal 4.92.0 (Microsoft, from NuGet).

## Switching it on

Edit `CoWork.bundle\Contents\cowork.json` in the install package (or on each PC under
`%APPDATA%\Autodesk\ApplicationPlugins\`) and fill in the two ids:

```json
"teams": {
  "tenantId": "<Directory (tenant) ID>",
  "clientId": "<Application (client) ID>"
}
```

Restart Civil 3D. Users then click **Friends → Sign in to Teams** (or type `CWTEAMS`).

## Switching it off

Any of these work on their own:

- Blank the two ids again in `cowork.json` (the Teams link disappears from the add-in), or
- Disable the app registration's sign-in (Enterprise applications → CoWork → Properties → *Enabled for users to sign in?* = No), or
- Revoke the admin consent / delete the app registration.

The rest of CoWork (its own chat and drafting) keeps working without Teams.
