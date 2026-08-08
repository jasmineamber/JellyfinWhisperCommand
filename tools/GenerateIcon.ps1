param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\app-icon.ico')
)

$width = 256
$height = 256
$pixels = [byte[]]::new($width * $height * 4)

function Set-Pixel([int]$x, [int]$y, [byte]$r, [byte]$g, [byte]$b, [byte]$a = 255) {
    if ($x -lt 0 -or $x -ge $width -or $y -lt 0 -or $y -ge $height) { return }
    $offset = (($height - 1 - $y) * $width + $x) * 4
    $pixels[$offset] = $b
    $pixels[$offset + 1] = $g
    $pixels[$offset + 2] = $r
    $pixels[$offset + 3] = $a
}

function Fill-Circle([int]$cx, [int]$cy, [int]$radius, [byte]$r, [byte]$g, [byte]$b) {
    for ($y = $cy - $radius; $y -le $cy + $radius; $y++) {
        for ($x = $cx - $radius; $x -le $cx + $radius; $x++) {
            if ((($x - $cx) * ($x - $cx)) + (($y - $cy) * ($y - $cy)) -le ($radius * $radius)) { Set-Pixel $x $y $r $g $b }
        }
    }
}

function Fill-RoundedRect([int]$left, [int]$top, [int]$rectWidth, [int]$rectHeight, [int]$radius, [byte]$r, [byte]$g, [byte]$b) {
    for ($y = $top; $y -lt $top + $rectHeight; $y++) {
        for ($x = $left; $x -lt $left + $rectWidth; $x++) {
            $inside = $true
            $cornerX = if ($x -lt $left + $radius) { $left + $radius } elseif ($x -ge $left + $rectWidth - $radius) { $left + $rectWidth - $radius - 1 } else { $x }
            $cornerY = if ($y -lt $top + $radius) { $top + $radius } elseif ($y -ge $top + $rectHeight - $radius) { $top + $rectHeight - $radius - 1 } else { $y }
            if ((($x - $cornerX) * ($x - $cornerX)) + (($y - $cornerY) * ($y - $cornerY)) -gt ($radius * $radius)) { $inside = $false }
            if ($inside) { Set-Pixel $x $y $r $g $b }
        }
    }
}

Fill-Circle 128 128 122 25 29 42
Fill-Circle 128 128 108 110 70 190
Fill-RoundedRect 43 65 170 126 18 18 23 35
Fill-RoundedRect 50 72 156 112 12 34 42 59

# Play triangle.
for ($y = 94; $y -le 146; $y++) {
    $half = [Math]::Floor(($y - 94) / 2)
    for ($x = 103; $x -le 103 + $half; $x++) { Set-Pixel $x $y 245 247 250 }
}

# Subtitle tracks.
Fill-RoundedRect 139 103 47 9 4 78 222 222
Fill-RoundedRect 139 121 32 9 4 78 222 222
Fill-RoundedRect 72 158 112 10 5 78 222 222

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32](40 + $pixels.Length + 8192))
    $writer.Write([UInt32]22)
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$width)
    $writer.Write([Int32]($height * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]$pixels.Length)
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)
    $writer.Write($pixels)
    $writer.Write([byte[]]::new(8192))
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}
