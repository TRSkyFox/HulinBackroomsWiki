param(
  [string]$Font = 'D:\Project\HulinBackroomsWiki\客户端\fonts\NotoSansCJKtc-Regular.otf',
  [string]$OutDir = 'D:\Project\HulinBackroomsWiki\客户端\fonts',
  [int]$Size = 32
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$pfc = New-Object System.Drawing.Text.PrivateFontCollection
$pfc.AddFontFile($Font)
$fam = $pfc.Families[0]
$chars = ('的一是不了在人有我他這中大來上國個到說們為子和你地出道也時年得就那要下以生會自著去之過家學對可她里後小麼心多天而能好都然沒日於起還發成事只作當想看文無開手十用主行方又如前所本見經頭面公同三已老從動兩長知民樣現分將外但身些與高意進把法此實回二理美點月明其種聲全工己話兒者向情部正名定女問力機給等幾很業最間新什打便位因重被走電四第門相次東政海口使教西再平真聽世氣信北少關並內加化由卻代軍產入先山五太水萬市眼體別處總才場師書比住員九笑性通目華報立馬命張活難神數件安表原車白應路期叫死常提感金何更反合放做系計或司利受光王果親界及今京務制解各任至清物台象記邊共風戰干接它許八特覺望直服毛林題建南度統色字請交愛讓認算論百吃義科怎元社術結六功指思非流每青管夫連遠資隊跟帶花快條院變聯言權往展該領傳近留紅治決周保達辦運武半候七必城父強步完革深區即求品士轉量空甚眾技輕程告江語英基派滿式李息寫呢識極令黃德收臉錢黨倒未持取設始版雙歷越史商千片容研像找友孩站廣改議形委早房音火際則首單據導影失拿網香似斯專石若兵弟誰校讀志飛觀爭究包組造落視濟喜離雖坐集編寶談府拉黑且隨格盡劍講布殺微怕母調局根曾準贏元超負換昨夜雨雪風').ToCharArray() | Select-Object -Unique;
# 去掉重複
$chars = $chars | Select-Object -Unique
$cell = $Size + 8
$cols = 32
$rows = [int][Math]::Ceiling($chars.Count / [double]$cols)
$bmp = New-Object System.Drawing.Bitmap ($cols * $cell), ($rows * $cell)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$fontObj = New-Object System.Drawing.Font($fam, $Size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.Brushes]::White
$items = @()
for ($i = 0; $i -lt $chars.Count; $i++) {
  $c = $chars[$i]
  $x = ($i % $cols) * $cell
  $y = [int][Math]::Floor($i / $cols) * $cell
  $g.DrawString($c, $fontObj, $brush, $x, $y)
  $items += [pscustomobject]@{ c = $c; x = $x; y = $y; w = $cell; h = $cell }
}
$g.Dispose(); $fontObj.Dispose()
New-Item -ItemType Directory -Force $OutDir | Out-Null
$png = Join-Path $OutDir ('atlas-' + $Size + '.png')
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$meta = [pscustomobject]@{ font = $Font; family = $fam.Name; size = $Size; cell = $cell; cols = $cols; rows = $rows; png = $png; chars = $items }
[System.IO.File]::WriteAllText((Join-Path $OutDir ('atlas-' + $Size + '.json')), ($meta | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))
$meta | Select-Object font,family,size,rows,png
