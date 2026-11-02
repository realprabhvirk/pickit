# Background removal model

PicKit runs background removal entirely on the user's PC. Nothing is uploaded, and no internet connection is needed once PicKit is installed.

## The model

| | |
|---|---|
| Model | IS-Net general use (`isnet-general-use.onnx`) |
| Size | 178 MB |
| SHA-256 | `60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a` |
| Input | 1024 x 1024, RGB, mean 0.5, deviation 1 |
| Runtime | ONNX Runtime, CPU |
| Source of this file | [rembg release assets](https://github.com/danielgatis/rembg/releases/tag/v0.0.0), converted from [xuebinqin/DIS](https://github.com/xuebinqin/DIS) |

The model is a 178 MB binary, over GitHub's per-file limit, so it isn't in this repository. `tools/get-model.ps1` downloads it and checks the hash. Builds pick it up from `%LOCALAPPDATA%\PicKit-build\models`.

If the file is missing, the tool says so and the manual Keep and Remove brushes still work.

## How it was chosen

Measured on 2026-11-02 on a Ryzen 5 7530U laptop with 16 GB of RAM and no dedicated GPU, using ONNX Runtime 1.31.1 at below-normal priority.

| Model | Size | Time per image | Peak memory | Result |
|---|---|---|---|---|
| u2netp | 4.6 MB | 0.5 s | low | Fast, but lost an arm and the lower body on a portrait, and dropped a second person entirely |
| IS-Net general use | 178 MB | 2.5 s | about 2 GB for the process | Kept whole subjects, including fine detail. **Chosen.** |
| BiRefNet-lite | 224 MB | 14 to 35 s, plus 20 s to load | 7.5 GB | Rejected. Would cripple or crash a normal 8 GB laptop on CPU |
| RMBG-2.0 | about 1 GB | not tested | not tested | Rejected. Non-commercial licence |

The model is loaded only while it runs and is released as soon as the mask exists. Reloading costs about a second, which isn't worth holding hundreds of megabytes for, and startup never touches it.

## Licensing: decide before selling PicKit

This has not been resolved, and it matters if PicKit is ever sold or distributed commercially.

- The [DIS repository](https://github.com/xuebinqin/DIS/blob/main/LICENSE.md) is Apache-2.0, which covers its code. The README doesn't state a licence for the `isnet-general-use` weights.
- The DIS5K dataset has separate terms of use. The README says commercial users need to sign another terms-of-use agreement by contacting the authors.
- BiRefNet is the cleanest alternative legally, because its weights are explicitly [MIT](https://huggingface.co/ZhengPeng7/BiRefNet), but it's too heavy for CPU use at 1024 x 1024.

Options before commercial release:

1. Ask the DIS authors to confirm the terms for the weights.
2. Export a lighter BiRefNet (for example at 512 x 512) to ONNX and benchmark it. Its MIT weights would settle the question.
3. Ship PicKit without the model. The brushes still work, and users could add a model themselves.

Swapping models needs only a new `SegmentationModel` entry, with its file name, input size, normalisation and hash.
