# Regenerates dev/api_game.txt and dev/api_unbound.txt: every type/field/property/method (incl. private)
# of Assembly-CSharp and UnboundLib, one per line, greppable. Run after a game or UnboundLib update.
#   powershell -ExecutionPolicy Bypass -File tools\dump_api.ps1 [-Profile "WildCards 1v1"]
param([string]$Profile = "WildCards 1v1")
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$m = "C:\Program Files (x86)\Steam\steamapps\common\ROUNDS\ROUNDS_Data\Managed"
$u = "$env:APPDATA\r2modmanPlus-local\ROUNDS\profiles\$Profile\BepInEx"
New-Item -ItemType Directory -Force "$root\dev" | Out-Null
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({param($s,$e) $n=(New-Object Reflection.AssemblyName($e.Name)).Name; foreach($dir in @($m,"$u\core","$u\plugins\willis81808-UnboundLib","$u\plugins\willis81808-MMHook")){ $f="$dir\$n.dll"; if(Test-Path $f){ return [Reflection.Assembly]::ReflectionOnlyLoadFrom($f)} }; return $null})
$bf = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
foreach ($pair in @(@("$m\Assembly-CSharp.dll","api_game.txt"), @("$u\plugins\willis81808-UnboundLib\UnboundLib.dll","api_unbound.txt"))) {
  $a = [Reflection.Assembly]::ReflectionOnlyLoadFrom($pair[0]); $sb = New-Object Text.StringBuilder
  try { $types = $a.GetTypes() } catch [Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ } }
  foreach ($t in $types) {
    $tn = [string]$t.FullName; if ($tn -like '*<*') { continue }
    $base = "?"; try { $base = [string]$t.BaseType.FullName } catch {}
    [void]$sb.AppendLine("TYPE " + $tn + " : " + $base)
    try { foreach ($f in $t.GetFields($bf)) { if ($f.Name -like '<*') { continue }; [void]$sb.AppendLine("  F " + $(if ($f.IsStatic) {'static '} else {''}) + $(if ($f.IsPrivate) {'private '} else {''}) + $f.FieldType.Name + " " + $t.Name + "." + $f.Name) } } catch {}
    try { foreach ($p in $t.GetProperties($bf)) { [void]$sb.AppendLine("  P " + $p.PropertyType.Name + " " + $t.Name + "." + $p.Name) } } catch {}
    try { foreach ($mm in $t.GetMethods($bf)) { if ($mm.Name -like '<*' -or $mm.Name -like 'get_*' -or $mm.Name -like 'set_*') { continue }; $ps = ($mm.GetParameters() | ForEach-Object { [string]$_.ParameterType.Name + " " + $_.Name }) -join ', '; [void]$sb.AppendLine("  M " + $(if ($mm.IsStatic) {'static '} else {''}) + $(if ($mm.IsPrivate) {'private '} else {''}) + $mm.ReturnType.Name + " " + $t.Name + "." + $mm.Name + "(" + $ps + ")") } } catch {}
  }
  [IO.File]::WriteAllText("$root\dev\$($pair[1])", $sb.ToString())
  "wrote dev\$($pair[1])"
}
