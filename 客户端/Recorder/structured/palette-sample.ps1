param(
  [Parameter(Mandatory=$true)][string]$Image,
  [string]$Out = ''
)
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
function AvgRect($x0,$y0,$x1,$y1) {
  $r=0L;$g=0L;$b=0L;$n=0
  for($y=$y0;$y -le $y1;$y+=3){ for($x=$x0;$x -le $x1;$x+=3){
    $c=$bmp.GetPixel($x,$y); $r+=$c.R;$g+=$c.G;$b+=$c.B;$n++
  }}
  if($n -eq 0){return $null}
  $r=[int][Math]::Round($r/$n);$g=[int][Math]::Round($g/$n);$b=[int][Math]::Round($b/$n)
  [pscustomobject]@{Hex=('#{0:X2}{1:X2}{2:X2}' -f $r,$g,$b);R=$r;G=$g;B=$b}
}
function BrightRect($x0,$y0,$x1,$y1) {
  $best=$null;$bestScore=-1
  for($y=$y0;$y -le $y1;$y+=2){ for($x=$x0;$x -le $x1;$x+=2){
    $c=$bmp.GetPixel($x,$y); $score=[Math]::Max($c.R,[Math]::Max($c.G,$c.B))
    if($score -gt $bestScore){$bestScore=$score;$best=$c}
  }}
  if(-not $best){return $null}
  [pscustomobject]@{Hex=('#{0:X2}{1:X2}{2:X2}' -f $best.R,$best.G,$best.B);R=$best.R;G=$best.G;B=$best.B}
}
$bg = AvgRect 1180 650 1520 820
$card = AvgRect 90 160 900 250
$card2 = AvgRect 80 630 1000 700
$line = AvgRect 15 100 25 500
$acc = BrightRect 20 15 320 55
$tx = BrightRect 50 290 900 400
$res = [pscustomobject]@{
  image = (Resolve-Path $Image).Path
  size = "$($bmp.Width)x$($bmp.Height)"
  sampled = [pscustomobject]@{
    bg = $bg.Hex; card = $card.Hex; card2 = $card2.Hex; line = $line.Hex; acc = $acc.Hex; tx = $tx.Hex
  }
  regions = [pscustomobject]@{
    bg = '1180,650-1520,820'; card = '90,160-900,250'; card2 = '80,630-1000,700'; line = '15,100-25,500'; acc = 'title 20,15-320,55'; tx = 'story 50,290-900,400'
  }
}
$json = $res | ConvertTo-Json -Depth 5
if($Out){ [System.IO.File]::WriteAllText($Out,$json,[System.Text.UTF8Encoding]::new($false)) }
$json
$bmp.Dispose()
