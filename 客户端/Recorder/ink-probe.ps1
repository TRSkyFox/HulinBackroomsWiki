param([string]$Png)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$script:AsTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]
function Await($op, $type) {
  $m = $script:AsTaskGeneric.MakeGenericMethod($type)
  $t = $m.Invoke($null, @($op))
  $t.Wait(-1) | Out-Null
  $t.Result
}
[Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.StorageFile, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
[Windows.Globalization.Language, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null

$lang = New-Object Windows.Globalization.Language 'zh-Hans'
$eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
if (-not $eng) { $eng = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }

$bmp = [System.Drawing.Bitmap]::new($Png)
Write-Host ("image {0}x{1}" -f $bmp.Width, $bmp.Height)

$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Png)) ([Windows.Storage.StorageFile])
$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$sb = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$res = Await ($eng.RecognizeAsync($sb)) ([Windows.Media.Ocr.OcrResult])

foreach ($line in $res.Lines) {
  $x1=[double]::MaxValue; $y1=[double]::MaxValue; $x2=0; $y2=0
  foreach ($wd in $line.Words) {
    $br=$wd.BoundingRect
    if ($br.X -lt $x1) { $x1=$br.X }
    if ($br.Y -lt $y1) { $y1=$br.Y }
    if (($br.X+$br.Width) -gt $x2) { $x2=$br.X+$br.Width }
    if (($br.Y+$br.Height) -gt $y2) { $y2=$br.Y+$br.Height }
  }
  # 取色：框内 4 条扫描线，步进 3px
  $lums = New-Object 'System.Collections.Generic.List[double]'
  $cols = New-Object 'System.Collections.Generic.List[object]'
  $stepY = [Math]::Max(1,[int](($y2-$y1)/3))
  for ($y=[int]$y1; $y -le [int]$y2; $y += $stepY) {
    for ($x=[int]$x1; $x -le [int]$x2; $x += 3) {
      if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { continue }
      $p = $bmp.GetPixel($x,$y)
      $lums.Add(0.299*$p.R + 0.587*$p.G + 0.114*$p.B)
      $cols.Add($p)
    }
  }
  if ($lums.Count -lt 6) { continue }
  $sorted = @($lums | Sort-Object)
  $med = $sorted[[int]($sorted.Count/2)]
  $pairs = @()
  for ($i=0; $i -lt $lums.Count; $i++) { $pairs += [pscustomobject]@{ d=[Math]::Abs($lums[$i]-$med); R=$cols[$i].R; G=$cols[$i].G; B=$cols[$i].B } }
  $top = @($pairs | Sort-Object d -Descending | Select-Object -First ([Math]::Max(3,[int]($pairs.Count*0.2))))
  $r=0;$g=0;$b=0; foreach ($t in $top) { $r+=$t.R; $g+=$t.G; $b+=$t.B }
  $k=[Math]::Max(1,$top.Count)
  $hex = '#{0:X2}{1:X2}{2:X2}' -f [int]($r/$k),[int]($g/$k),[int]($b/$k)
  $hex + [char]9 + $line.Text
}
$bmp.Dispose()