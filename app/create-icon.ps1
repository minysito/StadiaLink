param([string]$OutputPath)
Add-Type -AssemblyName System.Drawing
$frames=@()
foreach($size in @(16,32,48,256)) {
    $bitmap=New-Object Drawing.Bitmap $size,$size
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode='AntiAlias'
    $graphics.Clear([Drawing.Color]::FromArgb(16,21,23))
    $graphics.ScaleTransform($size/64.0,$size/64.0)
    $brush=New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 6,6),(New-Object Drawing.Point 56,56),([Drawing.Color]::FromArgb(202,245,122)),([Drawing.Color]::FromArgb(102,217,203))
    $pen=New-Object Drawing.Pen $brush,6
    $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddLine(31,12,18,12);$path.AddLine(18,12,8,24);$path.AddLine(8,24,18,36);$path.AddLine(18,36,30,36)
    $path.StartFigure();$path.AddLine(33,52,46,52);$path.AddLine(46,52,56,40);$path.AddLine(56,40,46,28);$path.AddLine(46,28,34,28)
    $path.StartFigure();$path.AddLine(25,42,39,22)
    $graphics.DrawPath($pen,$path)
    $stream=New-Object IO.MemoryStream
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames+=,@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose();$path.Dispose();$pen.Dispose();$brush.Dispose();$graphics.Dispose();$bitmap.Dispose()
}
$file=[IO.File]::Create($OutputPath)
$writer=New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames) {
        $dimension=if($frame.Size -eq 256){0}else{$frame.Size}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
        $offset+=$frame.Bytes.Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose();$file.Dispose()}
