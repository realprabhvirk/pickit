# Third-party notices

PicKit uses the following software.

| Component | Used for | Licence |
|---|---|---|
| [.NET and WPF](https://github.com/dotnet) | Runtime and UI framework | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Image decoding, encoding and drawing | MIT |
| [Skia](https://skia.org) | The graphics library SkiaSharp wraps | BSD 3-Clause |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) | Running the background removal model | MIT |
| [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) | Reading image metadata | Apache 2.0 |
| [XmpCore](https://github.com/drewnoakes/xmp-core-dotnet) | Reading XMP, used by MetadataExtractor | BSD 3-Clause |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM helpers | MIT |
| [xUnit](https://xunit.net) | Tests only, not distributed | Apache 2.0 |

## Background removal model

IS-Net general use, from [xuebinqin/DIS](https://github.com/xuebinqin/DIS). The repository's code is Apache 2.0. The licence of the trained weights is not explicitly stated, and the training dataset has its own terms. See [docs/BACKGROUND-REMOVAL.md](docs/BACKGROUND-REMOVAL.md) before distributing this commercially.

## Installer

Installers are built with [Inno Setup](https://jrsoftware.org/isinfo.php), which permits commercial use under its own licence. Its authors ask commercial users above USD 5,000 in annual revenue to buy a licence.

## Fonts

Icons come from Segoe Fluent Icons, which is part of Windows. The font isn't redistributed.
