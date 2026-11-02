# Downloads the background removal model and checks it against a pinned hash.
# It's a 178 MB binary, so it isn't kept in the repository. Build output picks it up from here.

$ErrorActionPreference = 'Stop'

$url = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx'
$expected = '60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a'

$folder = Join-Path $env:LOCALAPPDATA 'PicKit-build\models'
$target = Join-Path $folder 'isnet-general-use.onnx'
New-Item -ItemType Directory -Force $folder | Out-Null

if (-not (Test-Path $target)) {
    Write-Host "Downloading model to $target"
    [Net.ServicePointManager]::SecurityProtocol = 'Tls12'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest $url -OutFile "$target.part" -UseBasicParsing
    Move-Item "$target.part" $target -Force
}

$actual = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item $target -Force
    throw "The model's hash doesn't match. Expected $expected, got $actual. The file was deleted."
}

Write-Host "Model verified: $target"
