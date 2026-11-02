# PicKit

**The simple image toolbox for Windows.**

PicKit handles the image jobs that are too small for Photoshop and too annoying to do manually: resize this, crop this, redact this, compress this, remove this background, copy it, done.

No accounts, no cloud, no projects, no library. Everything happens on your PC, and your images are never uploaded. The only network request PicKit ever makes is to download an image you drag in from a web page, when the browser passes along its address and not the image itself.

## What it does

| | |
|---|---|
| **Edit** | Crop, resize in pixels, mm or cm, rotate, flip, pen, arrow, line, rectangle, circle, text with preset fonts, highlight, eraser |
| **Tabs** | Each image opens in its own tab, with its own edits and undo. Rename it from the bar at the top |
| **Privacy** | Blur and pixelate by box, rounded box, circle or brush, and redaction that truly replaces pixels. View and remove camera, location and author details |
| **Remove background** | Keep and Remove brushes. There's no AI in the release, and builds from source can add automatic removal |
| **Compress and convert** | PNG, JPEG and WebP, by quality or to a target file size |
| **Resize for** | Instagram, YouTube, LinkedIn, Facebook, wallpapers and more, always showing what would be cut |
| **Many images** | Batch compress, resize, convert and rename, with a preview, cancel and undo |
| **Clipboard** | Copy without saving, and paste screenshots straight in |
| **File Explorer** | Right-click images, or use Open with |
| **Snipping** | Ctrl+Alt+S freezes the screen, like Snip & Sketch. Drag over any part or take it all, with no sound. It lands on the clipboard and in the notification centre, ready to save, edit or trash |
| **Ribbon** | A thin strip on the edge of the screen that opens when pointed at. Drop an image on it from a web page or File Explorer, or start a snip |

Originals are safe by design. Files are written to a temporary name and swapped in only when complete, replacing a file always asks first, and batch jobs never overwrite anything.

## Requirements

Windows 10 version 1809 or later, or Windows 11, on 64-bit. The installer includes everything else, so .NET doesn't need to be installed.

## Building

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build PicKit.slnx
dotnet test tests/PicKit.Tests
dotnet test tests/PicKit.UiTests
```

Releases contain no AI. Automatic background removal is an optional extra for development builds, and it needs a model that's too large for this repository and has an unclear licence ([details](docs/BACKGROUND-REMOVAL.md)). This downloads it and checks its hash:

```powershell
.\tools\get-model.ps1
```

Without it, PicKit still runs, and the tool says automatic removal isn't installed while the brushes keep working. Tests that need the model are skipped.

To build the installer, install [Inno Setup](https://jrsoftware.org/isinfo.php), then:

```powershell
.\tools\build-installer.ps1
```

Build output goes to `%LOCALAPPDATA%\PicKit-build`, which keeps binaries out of OneDrive.

## How it's put together

| Project | Purpose |
|---|---|
| `src/PicKit.Core` | Image processing, documents and undo, export, metadata, rename, batch, Explorer registration. No UI |
| `src/PicKit` | The WPF app: canvas, tools, panels and Windows services |
| `tests/PicKit.Tests` | Unit tests, including deliberately damaged files and 100-megapixel images |
| `tests/PicKit.UiTests` | The real window, driven off-screen so it never touches your mouse, focus or clipboard |

Some decisions are worth knowing:

- **The canvas draws with Skia into a viewport-sized bitmap**, so a huge photo costs no second full copy in memory, and the screen uses the same drawing code as exported files.
- **Undo stores only what changed**, and older steps move to a temporary file rather than being lost when memory runs short.
- **Metadata removal rewrites the file's container**, so photos aren't recompressed. Anything that doesn't validate falls back to re-encoding pixels.
- **Explorer starts one process per selected file**, so later launches pass their file to the running PicKit before any UI starts.

More detail: [background removal](docs/BACKGROUND-REMOVAL.md), [testing](docs/TESTING.md), [third-party notices](THIRD-PARTY-NOTICES.md).

## Licence

PicKit is open source under the [MIT License](LICENSE).
