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
 $bounds = [Drawing.RectangleF]::new(12,12,232,232)
 $bg = [Drawing.Drawing2D.LinearGradientBrush]::new($bounds,
  [Drawing.ColorTranslator]::FromHtml('#263D56'), [Drawing.ColorTranslator]::FromHtml('#090F1A'), 58)
 $ink = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#A6E6D7'))
 $edge = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#526F86'), 7)
 $tile = [Drawing.Drawing2D.GraphicsPath]::new()
 $tile.AddArc(12,12,94,94,180,90)
 $tile.AddArc(150,12,94,94,270,90)
 $tile.AddArc(150,150,94,94,0,90)
 $tile.AddArc(12,150,94,94,90,90)
 $tile.CloseFigure()
 $g.FillPath($bg,$tile)
 $g.DrawPath($edge,$tile)
 $mark = [Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Alternate)
 $mark.AddPolygon([Drawing.PointF[]]@(
  [Drawing.PointF]::new(48,193), [Drawing.PointF]::new(111,62),
  [Drawing.PointF]::new(144,62), [Drawing.PointF]::new(208,193),
  [Drawing.PointF]::new(174,193), [Drawing.PointF]::new(159,161),
  [Drawing.PointF]::new(97,161), [Drawing.PointF]::new(82,193)
 ))
 $counter = [Drawing.PointF[]]@(
  [Drawing.PointF]::new(107,135), [Drawing.PointF]::new(127.5,92), [Drawing.PointF]::new(149,135)
 )
 $mark.AddPolygon($counter)
 $g.FillPath($ink,$mark)
 $stream = [IO.MemoryStream]::new()
 $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
 $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
 if ($size -eq 256) { $bmp.Save((Join-Path $assets 'ArthoAI.png'),[Drawing.Imaging.ImageFormat]::Png) }
 $stream.Dispose(); $g.Dispose(); $bmp.Dispose(); $tile.Dispose(); $mark.Dispose(); $edge.Dispose(); $bg.Dispose(); $ink.Dispose()
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
