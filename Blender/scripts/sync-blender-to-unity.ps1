# Sync Blender FBX exports into the Unity project.
# Run from repo root: powershell -ExecutionPolicy Bypass -File Blender/scripts/sync-blender-to-unity.ps1
$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ExportDir = Join-Path $RepoRoot "Blender\exports"
$Dest = Join-Path $RepoRoot "ReactorSim.Unity\Assets\Blender"
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$Copied = 0
foreach ($fbx in @("ControlRoom.fbx", "PanelKits.fbx", "PlantEquipment.fbx", "ReactorCore.fbx", "VesselInternals.fbx", "RotatingEquipment.fbx")) {
  $src = Join-Path $ExportDir $fbx
  if (Test-Path $src) {
    Copy-Item -LiteralPath $src -Destination (Join-Path $Dest $fbx) -Force
    Write-Host "Copied $fbx"
    $Copied++
  } else {
    throw "Missing $src (run Blender export first)"
  }
}
Copy-Item -LiteralPath (Join-Path $RepoRoot "assets\manifest.csv") -Destination (Join-Path $Dest "ReactorManifest.csv") -Force
$TexSrc = Join-Path $ExportDir "Textures"
$TexDest = Join-Path $Dest "Textures"
if (Test-Path $TexSrc) {
  New-Item -ItemType Directory -Force -Path $TexDest | Out-Null
  $pngs = Get-ChildItem -LiteralPath $TexSrc -Filter *.png
  foreach ($png in $pngs) { Copy-Item -LiteralPath $png.FullName -Destination (Join-Path $TexDest $png.Name) -Force }
  Write-Host "Copied $($pngs.Count) trim-sheet textures"
} else {
  Write-Host "No baked textures (run Blender/scripts/bake_trim_sheets.py to add them)"
}
if ($Copied -eq 0) { exit 1 }
