$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets'
$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(8, 8, 112, 112, 180, 90)
    $path.AddArc(136, 8, 112, 112, 270, 90)
    $path.AddArc(136, 136, 112, 112, 0, 90)
    $path.AddArc(8, 136, 112, 112, 90, 90)
    $path.CloseFigure()
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush([System.Drawing.Point]::new(8,8), [System.Drawing.Point]::new(248,248), [System.Drawing.ColorTranslator]::FromHtml('#1747A6'), [System.Drawing.ColorTranslator]::FromHtml('#101E45'))
    $graphics.FillPath($gradient, $path)
    $handle = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#FFCB56'), 26)
    $handle.StartCap = $handle.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($handle, 153, 153, 202, 202)
    $lens = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#F4FCFF'), 22)
    $graphics.DrawEllipse($lens, 50, 50, 116, 116)
    $cross = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#55D9F0'), 12)
    $cross.StartCap = $cross.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($cross, 82, 108, 134, 108)
    $graphics.DrawLine($cross, 108, 82, 108, 134)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [PSCustomObject]@{ Size = $size; Data = $stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetPath 'Everything2.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $cross.Dispose(); $lens.Dispose(); $handle.Dispose(); $gradient.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetPath 'Everything2.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $file.Dispose() }
