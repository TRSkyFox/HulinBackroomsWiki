param([Parameter(Mandatory=$true)][string]$SessionDir)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$shotDir = Join-Path $SessionDir 'floor-shots'
if (-not (Test-Path -LiteralPath $shotDir)) { Write-Output '{"ok":true,"count":0,"reason":"no floor-shots"}'; exit 0 }

function Q([System.Drawing.Color]$c) {
  $r = [int]([Math]::Floor($c.R / 4) * 4); $g = [int]([Math]::Floor($c.G / 4) * 4); $b = [int]([Math]::Floor($c.B / 4) * 4)
  return ('#{0:X2}{1:X2}{2:X2}' -f $r, $g, $b)
}
function Lum([System.Drawing.Color]$c) { return (0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B) }
# 底色從四角取樣：整張圖的眾數常常是內容面板，不是樓層底色。
function EdgeMode($bmp) {
  $h = @{}
  $w = $bmp.Width; $ht = $bmp.Height
  $rects = @(
    @(0, 0, [int]($w * 0.10), [int]($ht * 0.10)),
    @(($w - [int]($w * 0.10)), 0, $w, [int]($ht * 0.10)),
    @(0, ($ht - [int]($ht * 0.10)), [int]($w * 0.10), $ht),
    @(($w - [int]($w * 0.10)), ($ht - [int]($ht * 0.10)), $w, $ht)
  )
  foreach ($r in $rects) {
    for ($y = [Math]::Max(0, $r[1]); $y -lt [Math]::Min($ht, $r[3]); $y += 6) {
      for ($x = [Math]::Max(0, $r[0]); $x -lt [Math]::Min($w, $r[2]); $x += 6) {
        $k = Q $bmp.GetPixel($x, $y)
        if ($h.ContainsKey($k)) { $h[$k]++ } else { $h[$k] = 1 }
      }
    }
  }
  if ($h.Count -eq 0) { return $null }
  $best = $null; $n = -1
  foreach ($k in $h.Keys) { if ($h[$k] -gt $n) { $best = $k; $n = $h[$k] } }
  return $best
}
function ModeColor($bmp, [int]$x0, [int]$x1, [int]$y0, [int]$y1, [switch]$BrightOnly, [double]$BgLum = -1, [int]$MinLum = -1, [int]$MaxLum = 999, [string]$NotColor = '', [switch]$SaturatedOnly) {
  $h = @{}
  for ($y = [Math]::Max(0,$y0); $y -lt [Math]::Min($bmp.Height,$y1); $y += 6) {
    for ($x = [Math]::Max(0,$x0); $x -lt [Math]::Min($bmp.Width,$x1); $x += 6) {
      $c = $bmp.GetPixel($x, $y)
      $lum = Lum $c
      if ($BrightOnly -and $lum -lt ($BgLum + 70)) { continue }
      if ($lum -lt $MinLum -or $lum -gt $MaxLum) { continue }
      if ($SaturatedOnly) {
        $mx = [Math]::Max($c.R, [Math]::Max($c.G, $c.B)); $mn = [Math]::Min($c.R, [Math]::Min($c.G, $c.B))
        if (($mx - $mn) -lt 24) { continue }
      }
      $k = Q $c
      if ($NotColor -and $k -eq $NotColor) { continue }
      if ($h.ContainsKey($k)) { $h[$k]++ } else { $h[$k] = 1 }
    }
  }
  if ($h.Count -eq 0) { return $null }
  $best = $null; $n = -1
  foreach ($k in $h.Keys) { if ($h[$k] -gt $n) { $best = $k; $n = $h[$k] } }
  return $best
}


# 找出 card 色的範圍，用來沿面板邊界取線色（不要拿文字反鋸齒的色邊）。
function CardBounds($bmp, [string]$card, [int]$step = 3) {
  $minx = $bmp.Width; $miny = $bmp.Height; $maxx = -1; $maxy = -1
  $x0 = [int]($bmp.Width * 0.02); $x1 = [int]($bmp.Width * 0.75)
  $y0 = [int]($bmp.Height * 0.12); $y1 = [int]($bmp.Height * 0.92)
  for ($y = $y0; $y -lt $y1; $y += $step) {
    for ($x = $x0; $x -lt $x1; $x += $step) {
      if ((Q $bmp.GetPixel($x, $y)) -ne $card) { continue }
      if ($x -lt $minx) { $minx = $x }; if ($y -lt $miny) { $miny = $y }
      if ($x -gt $maxx) { $maxx = $x }; if ($y -gt $maxy) { $maxy = $y }
    }
  }
  if ($maxx -lt 0) { return $null }
  return @($minx, $miny, $maxx, $maxy)
}

function LineMode($bmp, $b, [string]$bg, [string]$card) {
  if (-not $b) { return $null }
  $minx = $b[0]; $miny = $b[1]; $maxx = $b[2]; $maxy = $b[3]
  $pad = 5
  $h = @{}
  for ($x = [Math]::Max(0, $minx - $pad); $x -le [Math]::Min($bmp.Width - 1, $maxx + $pad); $x++) {
    for ($d = 0; $d -le $pad; $d++) {
      $y1 = [Math]::Max(0, $miny - $d); $y2 = [Math]::Min($bmp.Height - 1, $maxy + $d)
      foreach ($y in @($y1, $y2)) {
        $k = Q $bmp.GetPixel($x, $y)
        if ($k -ne $bg -and $k -ne $card) { if ($h.ContainsKey($k)) { $h[$k]++ } else { $h[$k] = 1 } }
      }
    }
  }
  for ($y = [Math]::Max(0, $miny - $pad); $y -le [Math]::Min($bmp.Height - 1, $maxy + $pad); $y++) {
    for ($d = 0; $d -le $pad; $d++) {
      $x1 = [Math]::Max(0, $minx - $d); $x2 = [Math]::Min($bmp.Width - 1, $maxx + $d)
      foreach ($x in @($x1, $x2)) {
        $k = Q $bmp.GetPixel($x, $y)
        if ($k -ne $bg -and $k -ne $card) { if ($h.ContainsKey($k)) { $h[$k]++ } else { $h[$k] = 1 } }
      }
    }
  }
  if ($h.Count -eq 0) { return $null }
  $best = $null; $n = -1
  foreach ($k in $h.Keys) { if ($h[$k] -gt $n) { $best = $k; $n = $h[$k] } }
  return $best
}

$items = @()
$used = New-Object System.Collections.Generic.List[string]
foreach ($f in Get-ChildItem -LiteralPath $shotDir -File -Filter '*.png' | Sort-Object Name) {
  $name = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
  if ($name -notmatch '^(.*?)-f\d+$') { continue }
  $key = $Matches[1]
  $id = (($key -split '-', 2)[0]) -replace '^Level\s*', ''
  $nm = if ($key.Contains('-')) { ($key -split '-', 2)[1] } else { '' }
  $bmp = [System.Drawing.Bitmap]::FromFile($f.FullName)
  try {
    $bg = EdgeMode $bmp
    if (-not $bg) { continue }
    $bgc = [System.Drawing.ColorTranslator]::FromHtml($bg)
    $tx = ModeColor $bmp ([int]($bmp.Width*0.08)) ([int]($bmp.Width*0.66)) ([int]($bmp.Height*0.16)) ([int]($bmp.Height*0.86)) -BrightOnly -BgLum (Lum $bgc)
    $acc = ModeColor $bmp 0 ([int]($bmp.Width*0.45)) 0 ([int]($bmp.Height*0.12)) -BrightOnly -BgLum (Lum $bgc) -SaturatedOnly
    # card：主面板內、和背景不同但亮度接近的背景色；line：主面板邊界／分隔線的中間色
    $card = ModeColor $bmp ([int]($bmp.Width*0.06)) ([int]($bmp.Width*0.66)) ([int]($bmp.Height*0.20)) ([int]($bmp.Height*0.86)) -NotColor $bg -MinLum ([int]((Lum $bgc) - 18)) -MaxLum ([int]((Lum $bgc) + 28))
    $line = $null
    if ($card -ne $bg) {
      $bnd = CardBounds $bmp $card
      if ($bnd) { $line = LineMode $bmp $bnd $bg $card }
    }
    if (-not $tx) { $tx = $bg }
    if (-not $acc) { $acc = $tx }
    if (-not $card) { $card = $bg }
    if (-not $line) { $line = $card }
    # 取樣常把亮邊框和正文顏色抓反（例如上白下灰）；正文比邊框暗很多時對調。
    try {
      $txL = Lum ([System.Drawing.ColorTranslator]::FromHtml($tx))
      $lnL = Lum ([System.Drawing.ColorTranslator]::FromHtml($line))
      if ($lnL -gt ($txL + 38)) {
        $tmp = $tx; $tx = $line; $line = $tmp
      }
    } catch { }
    $items += [pscustomobject]@{
      id = $id; name = $nm; source = 'auto-floor-shot'; shot = $f.Name
      palette = [pscustomobject]@{ bg = $bg; background = $bg; card = $card; line = $line; acc = $acc; tx = $tx }
    }
    $used.Add($f.FullName)
  } finally { $bmp.Dispose() }
}

$out = Join-Path $SessionDir 'floor-palettes.json'
if ($items.Count -eq 0 -and (Test-Path -LiteralPath $out) -and (Get-Item -LiteralPath $out).Length -gt 2) {
  # 已經有取樣結果、這次只是沒有新截圖 → 不要把它蓋成 []
  Write-Output (ConvertTo-Json ([pscustomobject]@{ ok = $true; count = 0; kept = $true; file = $out }) -Compress)
  exit 0
}
if ($items.Count -eq 0) {
  # 空陣列也要是合法 JSON；以前 ConvertTo-Json 空陣列會寫出 0 byte。
  [System.IO.File]::WriteAllText($out, '[]', [System.Text.UTF8Encoding]::new($false))
} else {
  [System.IO.File]::WriteAllText($out, (ConvertTo-Json -InputObject @($items) -Depth 8), [System.Text.UTF8Encoding]::new($false))
}
# 取樣完成，截圖只是暫存：只刪「這次真的處理過」的檔案
foreach ($p in $used) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
Write-Output (ConvertTo-Json ([pscustomobject]@{ ok = $true; count = $items.Count; file = $out }) -Compress)
