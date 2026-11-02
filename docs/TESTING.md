# Testing

## Automated

| Suite | Tests | Covers |
|---|---|---|
| `tests/PicKit.Tests` | 331 | Resize, crop, compression, conversion, undo, rename, batch, metadata, clipboard formats, Explorer registration |
| `tests/PicKit.UiTests` | 139 | The real window end to end: every tool, save flows, prompts, batch, rename, display scaling, accessibility names |

The UI tests run off-screen, never take focus, and use an in-memory clipboard, so they can run while the PC is being used. Registry tests write only under a throwaway key.

For a longer run of the damaged-file tests:

```powershell
$env:PICKIT_FUZZ_ROUNDS = 5000
dotnet test tests/PicKit.Tests --filter HostileInputTests
```

## Measured on a Ryzen 5 7530U laptop with 16 GB of RAM

These were taken on 2026-11-02 with Chrome and OneDrive active.

| | |
|---|---|
| First paint, warm | about 1.4 s, of which most is WPF's own load time |
| First paint, first launch after a build | 3 to 4 s |
| Memory after startup | about 115 MB |
| Automatic background removal | about 2.5 s |
| 100-megapixel image: rotate | 433 ms |
| 100-megapixel image: blur a 3-megapixel area | 58 ms |
| 100-megapixel image: undo | 24 ms |
| 100-megapixel image: resize to 1920 wide | 212 ms |

Startup is above the 1 second aimed for. Going lower would mean replacing the Windows theme with a hand-built one.

## Windows shell check

Run against the real shell through `IContextMenu`, the interface File Explorer uses, on Windows 11 with a logging stub in place of PicKit:

| Files selected | Result |
|---|---|
| 1, 15, 16, 50, 100 | Each file launched once, with correct arguments, including paths with spaces |
| 101 | PicKit's menu is hidden, which is Windows' documented limit for this kind of entry |

Selections over 100 can be dragged onto the window instead. `--unregister` was confirmed to remove every key.

## Installer check

Run on 2026-11-02 on Windows 11, without administrator rights, using Inno Setup 6.7.3 and a silent install with the default options.

| | |
|---|---|
| Installer size | 234 MB |
| Installed files | All 257 match the published build, and the model's SHA-256 is correct |
| Explorer menu | Registered for all 10 image types, pointing at the installed `PicKit.exe` |
| Open with | Left unregistered, as it is off by default |
| First launch from the install folder | Opened an image, on an invisible desktop, with first paint after 4 s |
| Uninstall | Removed the app folder, Start menu entry, uninstall entry and every menu key |
| File associations | Default apps were the same after install and after uninstall |

## Hands-on checks

These need a person, or a real install, and have **not** been done yet.

**Input**

- [ ] Mouse wheel zooms around the cursor
- [ ] Press and drag anywhere along a slider
- [ ] While typing text, drag Font size and pick a font: the box updates and typing carries on
- [ ] Click text, then press Delete: it goes in one press
- [ ] Space + drag and middle-button drag pan
- [ ] Drawing with a pen tablet feels smooth
- [ ] Drag images and a folder onto the window from File Explorer

**Clipboard, using the real Windows clipboard**

- [ ] Copy, then paste into Word, Teams, Discord and Paint
- [ ] Transparent PNG keeps transparency in an app that supports it, and shows white in Paint
- [ ] Win + Shift + S screenshot opens with Ctrl+V
- [ ] Copy an image file in File Explorer, then Ctrl+V in PicKit

**Install**

- [x] The installer builds and runs without administrator rights
- [ ] The setup wizard's pages look right (the install above was silent)
- [ ] Right-click one image, then Compress, on Windows 11 (under Show more options)
- [ ] Select 50 images, right-click, Compress: one batch page opens
- [ ] Open with shows PicKit, only if chosen during install
- [x] Uninstall removes the menu and app folder
- [ ] Uninstall removes the Open with entry, if it was chosen during install
- [ ] Installing over a running copy closes it cleanly

**Windows 10**

- [ ] Icons fall back correctly to Segoe MDL2 Assets
- [ ] The Fluent theme looks right
