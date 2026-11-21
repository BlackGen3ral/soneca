$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\Soneca\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$images = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
 $bmp = [Drawing.Bitmap]::new($size,$size)
 $g = [Drawing.Graphics]::FromImage($bmp)
 $g.SmoothingMode = 'AntiAlias'
 $g.ScaleTransform(($size/256.0),($size/256.0))
 $bg = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#20382B'))
 $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#D7F8E1'))
 $tile = [Drawing.Drawing2D.GraphicsPath]::new()
 $tile.AddArc(12,12,94,94,180,90)
 $tile.AddArc(150,12,94,94,270,90)
 $tile.AddArc(150,150,94,94,0,90)
 $tile.AddArc(12,150,94,94,90,90)
 $tile.CloseFigure()
 $g.FillPath($bg,$tile)
 $mark = [Drawing.PointF[]]@(
  [Drawing.PointF]::new(49,190), [Drawing.PointF]::new(112,62),
  [Drawing.PointF]::new(144,62), [Drawing.PointF]::new(207,190),
  [Drawing.PointF]::new(173,190), [Drawing.PointF]::new(158,158),
  [Drawing.PointF]::new(98,158), [Drawing.PointF]::new(83,190)
 )
 $g.FillPolygon($ink,$mark)
 $counter = [Drawing.PointF[]]@(
  [Drawing.PointF]::new(111,130), [Drawing.PointF]::new(128,92), [Drawing.PointF]::new(145,130)
 )
 $g.FillPolygon($bg,$counter)
 $stream = [IO.MemoryStream]::new()
 $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
 if ($size -eq 256) { $bmp.Save((Join-Path $assets 'ArthoAI.png'),[Drawing.Imaging.ImageFormat]::Png) }
 $stream.Dispose(); $g.Dispose(); $bmp.Dispose(); $tile.Dispose(); $bg.Dispose(); $ink.Dispose()
}
$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets 'ArthoAI.ico')))
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
