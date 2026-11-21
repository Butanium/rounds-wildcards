# Renders docs/cards.png: all cards on their theme colors (run after updating art/png).
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cards = @(
  @("PORTAL ROUNDS","portal_rounds","#b13f8c","Bullets open linked portals"),
  @("GRAPPLE","grapple","#8c8c9c","Jump in mid-air fires a hook"),
  @("REWIND","rewind","#3567a8","Blocking rewinds you 2 seconds"),
  @("HOT POTATO","hot_potato","#a83434","Hits pass a ticking bomb"),
  @("WRAPAROUND","wraparound","#61378f","Screen edges wrap around"),
  @("SWITCHEROO","switcheroo","#8c8c9c","Blocking swaps you with your attacker"),
  @("METEOR","meteor","#a83434","Hold down to dive and slam"),
  @("ROCKET JUMP","rocket_jump","#b88f18","Shots blast you backwards"),
  @("BALLOON ROUNDS","balloon_rounds","#3567a8","Hits make enemies float"),
  @("BOOMERANG","boomerang","#b13f8c","Bullets come back. Catch them for ammo")
)
$W=260; $H=360; $GAP=20; $cols=5; $rows=2
$bmp = New-Object Drawing.Bitmap ($cols*$W + ($cols+1)*$GAP), ($rows*$H + ($rows+1)*$GAP)
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode='AntiAlias'; $g.TextRenderingHint='AntiAliasGridFit'; $g.InterpolationMode='HighQualityBicubic'
$g.Clear([Drawing.Color]::FromArgb(22,20,30))
$titleFont = New-Object Drawing.Font("Arial Black", 17, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$descFont = New-Object Drawing.Font("Segoe UI", 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$sf = New-Object Drawing.StringFormat; $sf.Alignment='Center'; $sf.LineAlignment='Center'
function RoundRect($x,$y,$w,$h,$r){ $p=New-Object Drawing.Drawing2D.GraphicsPath; $d=$r*2
  $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90); $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90); $p.CloseFigure(); return $p }
for ($i=0; $i -lt $cards.Count; $i++) {
  $c=$cards[$i]; $x=$GAP+($i%$cols)*($W+$GAP); $y=$GAP+[Math]::Floor($i/$cols)*($H+$GAP)
  $col=[Drawing.ColorTranslator]::FromHtml($c[2])
  $card=RoundRect $x $y $W $H 18
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(245,245,245))), $card)
  $art=RoundRect ($x+12) ($y+12) ($W-24) ($W-24) 12
  $g.FillPath((New-Object Drawing.SolidBrush $col), $art)
  $png=Join-Path $root "art\png\$($c[1]).png"
  if (Test-Path $png) { $im=[Drawing.Image]::FromFile($png); $g.DrawImage($im, $x+22, $y+22, $W-44, $W-44); $im.Dispose() }
  $g.DrawString($c[0], $titleFont, (New-Object Drawing.SolidBrush $col), (New-Object Drawing.RectangleF $x, ($y+$W-6), $W, 34), $sf)
  $g.DrawString($c[3], $descFont, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(60,60,70))), (New-Object Drawing.RectangleF ($x+10), ($y+$W+26), ($W-20), 64), $sf)
}
$out=Join-Path $root "docs\cards.png"; $bmp.Save($out,[Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose(); "saved $out"
