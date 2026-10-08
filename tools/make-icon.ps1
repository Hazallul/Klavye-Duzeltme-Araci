# Uygulama simgesini (src/Duzeltici/app.ico) üretir: mavi tuş kapağı içinde beyaz onay işareti.
# Tepsi simgesi ayrıca kod içinde, görev çubuğu temasına göre tek renkli çiziliyor (TrayApp.DrawIcon).
# Kullanım: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) "src\Duzeltici\app.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function Draw([int]$size) {
    $u = $size / 16.0
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $r = New-Object Drawing.RectangleF (0.6 * $u), (0.6 * $u), (14.8 * $u), (14.8 * $u)
    $d = 7.0 * $u
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $fill = New-Object Drawing.Drawing2D.LinearGradientBrush $r, ([Drawing.Color]::FromArgb(0x2B, 0x88, 0xD8)), ([Drawing.Color]::FromArgb(0x00, 0x5F, 0xB8)), 90.0
    $g.FillPath($fill, $path)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([Math]::Max(1.5, 1.9 * $u))
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF (4.4 * $u), (8.3 * $u)),
        (New-Object Drawing.PointF (6.9 * $u), (10.8 * $u)),
        (New-Object Drawing.PointF (11.6 * $u), (5.4 * $u)))
    $g.DrawLines($pen, $points)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png) # 256 px için PNG standart
    } else {
        # Küçük boyutlar klasik DIB: BITMAPINFOHEADER + alttan üste BGRA + 1 bit AND maskesi.
        # Her araç (eski System.Drawing dahil) bunu okuyabilir.
        $bw = New-Object IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
        $bw.Write([uint32]($size * $size * 4 + $maskStride * $size))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $size))) # alfa kanalı kullanıldığı için maske boş
        $bw.Flush()
    }
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO: başlık + her boyut için 16 baytlık kayıt + PNG verileri.
$images = $sizes | ForEach-Object { , (Draw $_) }
$fs = [IO.File]::Create($out)
$w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "Yazıldı: $out"
