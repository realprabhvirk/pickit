# Publishes PicKit and packages it into a Windows installer with Inno Setup.
# Needs Inno Setup, either on this path or in the location below.

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'PicKit-build\tools\inno\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles} 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
)

$compiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup was not found. Install it from https://jrsoftware.org/isinfo.php' }

& (Join-Path $PSScriptRoot 'publish.ps1')

& $compiler (Join-Path $root 'installer\PicKit.iss')
if ($LASTEXITCODE -ne 0) { throw 'The installer failed to build.' }
