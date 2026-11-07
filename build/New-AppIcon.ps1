$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$path = Join-Path $PSScriptRoot '..\src\SystemCockpit\Assets\aether.ico'
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 64.0, $size / 64.0)
        $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#100924'))
        $cyan = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#24FFE3'), 5)
        $purple = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#F56AFF'), 4)
        try {
            $graphics.FillRectangle($background, 0, 0, 64, 64)
            $triangle = [Drawing.PointF[]]@([Drawing.PointF]::new(32, 6), [Drawing.PointF]::new(58, 56), [Drawing.PointF]::new(6, 56))
            $graphics.DrawPolygon($cyan, $triangle)
            $graphics.DrawLine($purple, 32, 21, 18, 48)
            $graphics.DrawLine($purple, 32, 21, 46, 48)
            $graphics.DrawLine($purple, 24, 37, 40, 37)
        }
        finally { $background.Dispose(); $cyan.Dispose(); $purple.Dispose() }
        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally { $stream.Dispose() }
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$output = [IO.File]::Create([IO.Path]::GetFullPath($path))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }
Write-Output "Created high-contrast AETHER icon at $path"
