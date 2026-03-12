param(
    [string]$PublishDir = 'bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish',
    [string]$OutDir = 'release',
    [string]$Version = 'dev'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishFull = Join-Path $repoRoot $PublishDir
$outFull = Join-Path $repoRoot $OutDir
$packageName = "MusicBox-win-x64-$Version"
$stageDir = Join-Path $outFull $packageName
$zipPath = Join-Path $outFull ($packageName + '.zip')
$hashPath = $zipPath + '.sha256'

if (-not (Test-Path $publishFull)) {
    throw "Publish output not found: $publishFull"
}

New-Item -ItemType Directory -Force -Path $outFull | Out-Null
if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item -Path (Join-Path $publishFull '*') -Destination $stageDir -Recurse -Force

@"
Music Box
=========

Quick start:
1. Extract this zip.
2. Run 音乐魔盒.exe.
3. If Windows shows a SmartScreen warning, click More info, then Run anyway.

Notes:
- This package is the unpackaged Windows x64 build.
- Keep all extracted files in the same folder.
"@ | Set-Content -LiteralPath (Join-Path $stageDir 'README.txt') -Encoding utf8

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 -ErrorAction SilentlyContinue)
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Package: $zipPath"
Write-Host "SHA256 : $hashPath"
