param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$scenes=Get-ChildItem -LiteralPath $Directory -Filter '*.json' | Where-Object { $_.Name -match '^\d\d-' } | Sort-Object Name
$sheet=New-Object System.Drawing.Bitmap 1920,([int][Math]::Ceiling($scenes.Count/2.0)*540)
$sheetGraphics=[System.Drawing.Graphics]::FromImage($sheet)
$sheetGraphics.Clear([System.Drawing.Color]::FromArgb(8,13,26))
$index=0
foreach($scene in $scenes) {
    $bitmap=New-Object System.Drawing.Bitmap 1920,1080
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.TextRenderingHint=[System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    foreach($primitive in (Get-Content -LiteralPath $scene.FullName -Raw | ConvertFrom-Json)) {
        if($primitive.Kind -eq 'image') {
            $asset=[System.Drawing.Image]::FromFile($primitive.Path)
            $x=[single]$primitive.X;$y=[single]$primitive.Y;$w=[single]$primitive.W;$h=[single]$primitive.H
            if($primitive.Fit){$factor=[Math]::Min($w/$asset.Width,$h/$asset.Height);$pw=$asset.Width*$factor;$ph=$asset.Height*$factor;$x+=($w-$pw)/2;$y+=($h-$ph)/2;$w=$pw;$h=$ph}
            $graphics.DrawImage($asset,$x,$y,$w,$h);$asset.Dispose();continue
        }
        $color=[System.Drawing.ColorTranslator]::FromHtml($primitive.Color)
        if($null -ne $primitive.Alpha){$color=[System.Drawing.Color]::FromArgb([int]$primitive.Alpha,$color)}
        $brush=New-Object System.Drawing.SolidBrush $color
        $rect=New-Object System.Drawing.RectangleF ([single]$primitive.X),([single]$primitive.Y),([single]$primitive.W),([single]$primitive.H)
        if($primitive.Kind -eq 'panel') {$graphics.FillRectangle($brush,$rect)} else {
            $font=New-Object System.Drawing.Font 'Segoe UI',([single]$primitive.Size),([System.Drawing.FontStyle]::Regular),([System.Drawing.GraphicsUnit]::Pixel)
            $format=New-Object System.Drawing.StringFormat
            if($primitive.Centered){$format.Alignment=[System.Drawing.StringAlignment]::Center}
            $format.Trimming=[System.Drawing.StringTrimming]::EllipsisCharacter
            $measured=$graphics.MeasureString($primitive.Text,$font,[int]$primitive.W,$format)
            if($measured.Height -gt $primitive.H+3) {throw "Text exceeds host preview box in $($scene.Name): $($primitive.Text)"}
            $graphics.DrawString($primitive.Text,$font,$brush,$rect,$format)
            $format.Dispose();$font.Dispose()
        }
        $brush.Dispose()
    }
    $png=Join-Path $Directory ($scene.BaseName+'.png')
    $bitmap.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
    $sheetGraphics.DrawImage($bitmap,([int]($index%2)*960),([int][Math]::Floor($index/2)*540),960,540)
    $graphics.Dispose();$bitmap.Dispose();$index++
}
$sheet.Save((Join-Path $Directory 'contact-sheet.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$sheetGraphics.Dispose();$sheet.Dispose()
Write-Output "PASS: $index host layout previews; canvas and text bounds checked. TV font/layout verification remains pending."
