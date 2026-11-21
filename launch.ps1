# Launch ROUNDS modded with an r2modman profile (same command r2modman uses).
#   powershell -ExecutionPolicy Bypass -File launch.ps1 [-Profile "WildCards 1v1"] [-Restart]
param([string]$Profile = "WildCards 1v1", [switch]$Restart)
$p = Get-Process Rounds -ErrorAction SilentlyContinue
if ($p) {
  if (-not $Restart) { "ROUNDS is already running (use -Restart to close and relaunch)"; exit 0 }
  $p.CloseMainWindow() | Out-Null
  if (-not $p.WaitForExit(10000)) { Stop-Process -Id $p.Id -Force }
  Start-Sleep -Seconds 2   # let Windows release the DLL lock
}
$target = "$env:APPDATA\r2modmanPlus-local\ROUNDS\profiles\$Profile\BepInEx\core\BepInEx.Preloader.dll"
Start-Process "C:\Program Files (x86)\Steam\steam.exe" -ArgumentList @("-applaunch","1557740","--doorstop-enable","true","--doorstop-target","`"$target`"")
"launched ROUNDS with profile '$Profile'"
