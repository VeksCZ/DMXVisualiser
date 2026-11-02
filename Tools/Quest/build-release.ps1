# Podepsaný Quest APK (Build\Quest\DMXVisualiser-release.apk) pro upload do Meta release channelu.
# Klíč čte BuildQuest.BuildRelease z %USERPROFILE%\.xrhouse\signing.json (mimo repo).
#   powershell -ExecutionPolicy Bypass -File Tools\Quest\build-release.ps1
param([string]$UnityVersion = '6000.3.9f1')
$ErrorActionPreference = 'Stop'
$unity = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity nenalezeno: $unity" }
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
$log = Join-Path $env:TEMP 'unity_quest_release.log'
$p = Start-Process $unity -Wait -PassThru -ArgumentList '-batchmode','-nographics','-quit','-projectPath',"`"$root`"",'-buildTarget','Android','-executeMethod','BuildQuest.BuildRelease','-logFile',"`"$log`""
Select-String $log -Pattern 'Quest release:|error CS' | ForEach-Object { $_.Line.Trim() }
if ($p.ExitCode -ne 0) { throw "Unity skončilo s kódem $($p.ExitCode), log: $log" }
Write-Host "Hotovo: $root\Build\Quest\DMXVisualiser-release.apk"
