# Detail_Grid

## Custom ribbon installer

`install_custom_ribbon.lsp` adds your custom ribbon and applies your
customization settings to the AutoCAD profile you're currently using.

### Setup

Open `install_custom_ribbon.lsp` and edit the **CONFIGURATION** section:

| Setting | What it does |
| --- | --- |
| `*ICR-cuix-path*` | Full path to the partial `.cuix` file that holds your ribbon |
| `*ICR-support-paths*` | Folders added to the Support File Search Path |
| `*ICR-trusted-paths*` | Folders added to Trusted Locations (end with `\...` to include subfolders) |
| `*ICR-template-path*`, `*ICR-qnew-template*`, `*ICR-plotter-path*`, `*ICR-plotstyle-path*` | Optional file locations from OPTIONS > Files (`nil` leaves them alone) |
| `*ICR-workspace*` | Optional workspace to switch to after loading |
| `*ICR-sysvars*` | System variables to set, as `("NAME" value)` pairs |

### Run it

1. In AutoCAD, switch to the profile you want (OPTIONS > Profiles > Set Current).
2. Run `APPLOAD` and load `install_custom_ribbon.lsp`, or drag the file into the drawing.
3. Type `INSTALLRIBBON`.

You can run it again at any time. Folders are only added if they're missing,
and the ribbon CUIx is reloaded so you get the latest version.

AutoCAD saves profile settings to the registry when it closes, so close it
normally (don't end the process) after running the installer.

### Notes

- If the ribbon tabs don't appear after loading, open `CUI`, find your tabs
  under the partial customization file, and drag them into your workspace's
  **Ribbon Tabs** list. Or set `*ICR-workspace*` to a workspace that already
  includes them.
- System variables that AutoCAD stores in the drawing (rather than the
  registry) only apply to the current drawing.
