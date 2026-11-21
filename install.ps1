# Installs the built WildCards mod into an r2modman ROUNDS profile (default: "WildCards 1v1").
# Usage: powershell -File install.ps1 [-Profile "more players"]
param([string]$Profile = "WildCards 1v1")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$prof = Join-Path "$env:APPDATA\r2modmanPlus-local\ROUNDS\profiles" $Profile
if (-not (Test-Path $prof)) { throw "Profile not found: $prof" }

$manifest = Get-Content "$root\package\manifest.json" -Raw | ConvertFrom-Json
$ver = $manifest.version_number.Split('.')
$dest = "$prof\BepInEx\plugins\WildCards-WildCards"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$root\bin\pkg\*" $dest -Force

# Register in mods.yml so r2modman lists it (replace any previous entry).
$yml = "$prof\mods.yml"
$txt = Get-Content $yml -Raw
$parts = [regex]::Split($txt, "(?m)^(?=- manifestVersion)") | Where-Object { $_ -notmatch "name: WildCards-WildCards" }
$entry = @"
- manifestVersion: 1
  name: WildCards-WildCards
  authorName: WildCards
  websiteUrl: ""
  displayName: WildCards
  description: $($manifest.description)
  gameVersion: "0"
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: $([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
  loaders: []
  dependencies:
    - willis81808-UnboundLib-3.2.14
    - willis81808-MMHook-1.0.0
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: $($ver[0])
    minor: $($ver[1])
    patch: $($ver[2])
  enabled: true
  icon: $dest\icon.png

"@
$out = (($parts -join "").TrimEnd() + "`n" + $entry)
[IO.File]::WriteAllText($yml, $out, (New-Object Text.UTF8Encoding $false))
Write-Output "Installed WildCards $($manifest.version_number) into profile '$Profile'"
