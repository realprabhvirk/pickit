# Builds the self-contained app that the installer packages. Users don't need .NET installed.

param(
    [string]$Output = (Join-Path $env:LOCALAPPDATA 'PicKit-build\publish')
)

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\PicKit\PicKit.csproj'

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Debug symbols are only useful during development, and Skia's alone are 92 MB.
Get-ChildItem $Output -Filter *.pdb | Remove-Item

# Release builds carry no background removal model. See docs/BACKGROUND-REMOVAL.md.
if (Test-Path (Join-Path $Output 'models\isnet-general-use.onnx')) {
    throw 'A release must not include the background removal model.'
}

$size = (Get-ChildItem $Output -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published to {0} ({1:N0} MB)" -f $Output, $size)
