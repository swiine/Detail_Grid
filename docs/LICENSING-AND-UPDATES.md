# CoWork: licences and automatic updates (for the person who runs CoWork)

Only people you approve can use CoWork, and new versions reach everyone the next time they restart Civil 3D.
Both rely on one **private key** (`cowork-private.key`) that only you hold. CoWork has the matching public key
built in, so it can check licences and updates but can never make them.

> Keep `cowork-private.key` safe and backed up (e.g. a password manager or an encrypted USB stick).
> Anyone who has it can let people in and publish code that every CoWork PC will run.
> If you lose it, a new key pair and a new build are needed, and everyone reinstalls once.

## The CoWork Admin tool

Unzip `CoWork-Admin.zip` somewhere only you can reach and double-click `CoWork-Admin.exe`
(needs the .NET 8 runtime, which Civil 3D 2025 already installs). It reads `cowork.json` next to it for the shared
folder and the release folder.

| Menu | What it does |
|---|---|
| 1 Approve access requests | Lists people who clicked **Request access**. Approve with an optional end date and optional "this PC only". Their licence goes into the shared folder and their CoWork unlocks within ~15 s. |
| P Pre-approve people | Type logins (e.g. `danielV, coreyy`). Their licences go straight into the shared folder, so they're let in the first time they open CoWork - no request, no licence window. |
| 2 Issue a licence | Make a key by hand: named logins, or `*` for everyone on a Windows domain, optional PCs and end date. Prints the key to send them. |
| 3 Revoke a licence | Cancels licences. CoWork locks for those people within 10 minutes (or at their next start). |
| 4 Publish an update | Copies a new build into the release folder, signs it, and makes it the version everyone gets on their next Civil 3D start. |
| 5 Check a licence key | Is a key genuine, and who is it for. |
| 6 List licences issued | Everything issued from this PC (kept in `issued-licences.csv`), with revoked ones marked. |

## How it fits together

```
CoWork-data\licences\<login>.key        licences you approved (signed - can't be faked or edited)
CoWork-data\licences\requests\*.json     people asking for access
CoWork-data\licences\revoked.key         signed list of cancelled licences
CoWork-release\latest.txt                version everyone should run
CoWork-release\<version>\...             that version's files + manifest.json/.sig (SHA-256 of every file, signed)
```

On each PC, Civil 3D loads only the small `CoWork.Loader.dll` from the bundle. At every start it checks the release
folder; if there's a newer version it copies it to `%LOCALAPPDATA%\CoWork\app\<version>`, checks the signature and
every file, and only then runs it. Anything unsigned or altered is ignored and the last good copy runs instead.

## Stopping people from tampering with licences

What CoWork already guarantees, whatever anyone does to the files:

- **Nobody can make or edit a licence.** Every licence is signed with your private key; a typed, edited or
  copied-and-altered file fails the signature check and is ignored.
- **A licence only works for the login it names.** Copying Daniel's file and renaming it to your own login does nothing.
- **Licences are tied to the office's Windows domain** (the admin tool fills it in for you), so a home PC with a
  local account called `danielV` doesn't count.
- **Revokes can't be undone.** Each revoked list is numbered and signed, every PC keeps the newest one it has seen,
  and the admin tool keeps its own copy (`revoked.key` next to it). Deleting the shared list or putting an older
  one back changes nothing.

What only Windows permissions can stop: someone **deleting** other people's licence files (an annoyance, not a way
in - you just pre-approve them again), or filling the requests folder with junk. Ask IT to set the folder so only you
can change it:

| Folder | Everyone else | You |
|---|---|---|
| `CoWork-data\licences` | Read | Full control |
| `CoWork-data\licences\requests` | Read + write (so they can ask) | Full control |
| `CoWork-release` | Read | Full control |

For IT (adjust the group and admin account):

```
icacls "\\server\...\CoWork-data\licences" /inheritance:r /grant:r "OFFICE\coreyy":(OI)(CI)F "OFFICE\Domain Users":(OI)(CI)RX
icacls "\\server\...\CoWork-data\licences\requests" /grant "OFFICE\Domain Users":(OI)(CI)M
icacls "\\server\...\CoWork-release" /inheritance:r /grant:r "OFFICE\coreyy":(OI)(CI)F "OFFICE\Domain Users":(OI)(CI)RX
```

## Being honest about the limits

- This stops people you haven't approved from running CoWork. It is not unbreakable: CoWork is .NET, so a
  determined programmer could decompile and patch their own copy. It's a lock on the door, not a vault.
- Chat messages are plain files in `CoWork-data`. To keep unapproved people from **reading** them by browsing the
  folder, ask IT to limit the folder's Windows permissions to the approved group.
- Ask IT to make `CoWork-release` **read-only for everyone except you**. (Signed releases already stop anyone else's
  files from running, but this keeps the folder tidy.)
