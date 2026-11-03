$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Soneca\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$images = @()
foreach ($size in @(16,32,48,64,128,256)) {
 $bmp = [Drawing.Bitmap]::new($size,$size)
 $g = [Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode = 'AntiAlias'
 $g.ScaleTransform(($size/256.0),($size/256.0))
 $bg = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#211C34'))
 $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#C7B8FF'))
 $g.FillRectangle($bg,0,0,256,256)
 $pen = [Drawing.Pen]::new($ink,18)
 $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
 $g.DrawEllipse($pen,48,48,160,160)
 $g.DrawLine($pen,128,78,128,128)
 $g.DrawLine($pen,128,128,159,147)
 $stream = [IO.MemoryStream]::new()
 $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
 if ($size -eq 256) { $bmp.Save((Join-Path $assets 'soneca.png'),[Drawing.Imaging.ImageFormat]::Png) }
 $stream.Dispose(); $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $bg.Dispose(); $ink.Dispose()
}
$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets 'soneca.ico')))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16*$images.Count
foreach ($im in $images) {
 $dimension = if ($im.Size -eq 256) {0} else {$im.Size}
 $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
 $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$im.Bytes.Length); $writer.Write([uint32]$offset)
 $offset += $im.Bytes.Length
}
foreach ($im in $images) { $writer.Write([byte[]]$im.Bytes) }
$writer.Dispose()
