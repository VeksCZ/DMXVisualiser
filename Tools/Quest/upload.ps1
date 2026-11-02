# Nahraje Build\Quest\DMXVisualiser-release.apk do release channelu aplikace DMX Visualiser v Meta Developer Dashboardu.
#   powershell -ExecutionPolicy Bypass -File Tools\Quest\upload.ps1 [-Channel ALPHA] [-Notes "text"]
# Potřebuje (mimo repo, jako u XR House): %USERPROFILE%\.xrhouse\meta.json s polem "token" (platform utility token účtu)
# a ovr-platform-util.exe ve stejné složce nebo na PATH. App ID této aplikace je níže (není tajné).
param([string]$Channel = 'ALPHA', [string]$Notes = '', [string]$AppId = '1672509524239308')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$cfgDir = Join-Path $env:USERPROFILE '.xrhouse'
$apk = Join-Path $root 'Build\Quest\DMXVisualiser-release.apk'
if (-not (Test-Path $apk)) { throw "APK nenalezeno - nejdřív Tools\Quest\build-release.ps1" }
$util = Join-Path $cfgDir 'ovr-platform-util.exe'
if (-not (Test-Path $util)) { $c = Get-Command ovr-platform-util.exe -ErrorAction SilentlyContinue; if ($c) { $util = $c.Source } }
if (-not (Test-Path $util)) { throw "ovr-platform-util.exe nenalezeno ($cfgDir)" }
$metaFile = Join-Path $cfgDir 'meta.json'
if (-not (Test-Path $metaFile)) { throw "$metaFile chybí" }
$token = (Get-Content $metaFile -Raw | ConvertFrom-Json).token
if (-not $token) { throw "$metaFile neobsahuje token" }
if (-not $Notes) { Push-Location $root; $Notes = (git log -1 --format=%s 2>$null) -join ' '; Pop-Location }
Write-Host "Nahrávám $apk do kanálu $Channel (app $AppId)..."
& $util upload-quest-build --app-id $AppId --token $token --apk $apk --channel $Channel --age-group TEENS_AND_ADULTS --notes $Notes --disable-progress-bar
if ($LASTEXITCODE -ne 0) { throw "Upload selhal (exit $LASTEXITCODE)" }
Write-Host "Nahráno."
