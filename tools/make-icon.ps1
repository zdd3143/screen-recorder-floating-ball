# make-icon.ps1 - builds Assets\app.ico (multi-size, classic 32bpp DIB entries) and a preview PNG.
# Pure .NET base library only: no third-party modules.

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# 输出到仓库里主程序的 Assets 目录（相对本脚本定位，换机器也能用）
$Root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
        else { (Get-Location).Path }
$OutDir = Join-Path $Root 'src\ScreenRecorder\Assets'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$SIZES = @(16, 24, 32, 48, 64, 128, 256)

function New-RgbaBuffer([int]$size) {
    $buf = New-Object 'byte[]' ($size * $size * 4)
    $cx = ($size - 1) / 2.0
    $cy = ($size - 1) / 2.0
    $rOuter = $size / 2.0 - $size * 0.02
    $rDot = $size * 0.14
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $dx = $x - $cx
            $dy = $y - $cy
            $dist = [Math]::Sqrt($dx * $dx + $dy * $dy)
            $alpha = $rOuter - $dist
            if ($alpha -gt 1.0) { $alpha = 1.0 }
            if ($alpha -lt 0.0) { $alpha = 0.0 }
            # colour: vertical gradient red -> deep red
            $t = $y / [double]($size - 1)
            $red = 236 - 76 * $t
            $green = 68 - 46 * $t
            $blue = 68 - 34 * $t
            # white record dot in the middle
            if ($dist -lt $rDot + 1.0) {
                $w = $rDot - $dist
                if ($w -gt 1.0) { $w = 1.0 }
                if ($w -lt 0.0) { $w = 0.0 }
                $red = $red + (255 - $red) * $w
                $green = $green + (255 - $green) * $w
                $blue = $blue + (255 - $blue) * $w
            }
            $i = ($y * $size + $x) * 4
            $buf[$i] = [byte][Math]::Round($blue)
            $buf[$i + 1] = [byte][Math]::Round($green)
            $buf[$i + 2] = [byte][Math]::Round($red)
            $buf[$i + 3] = [byte][Math]::Round($alpha * 255)
        }
    }
    return $buf
}

function ConvertTo-IconDib([int]$size, [byte[]]$rgba) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40)
    $bw.Write([int32]$size)
    $bw.Write([int32]($size * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($size * $size * 4))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $i = ($y * $size + $x) * 4
            $bw.Write([byte]$rgba[$i])
            $bw.Write([byte]$rgba[$i + 1])
            $bw.Write([byte]$rgba[$i + 2])
            $bw.Write([byte]$rgba[$i + 3])
        }
    }
    $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $mask = New-Object 'byte[]' ($maskStride * $size)
    for ($y = 0; $y -lt $size; $y++) {
        $srcRow = $size - 1 - $y
        for ($x = 0; $x -lt $size; $x++) {
            $a = $rgba[($srcRow * $size + $x) * 4 + 3]
            if ($a -lt 128) {
                $byteIndex = $y * $maskStride + [int]([Math]::Floor($x / 8))
                $mask[$byteIndex] = $mask[$byteIndex] -bor ([byte](0x80 -shr ($x % 8)))
            }
        }
    }
    $bw.Write($mask, 0, $mask.Length)
    $bw.Flush()
    $out = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return $out
}

# ---- preview PNG (truecolour + alpha, stored, zlib) ----
$script:POLY = [uint64]3988292384   # 0xEDB88320
$script:U32 = [uint64]4294967295    # 0xFFFFFFFF

function Get-CrcTable {
    $t = New-Object 'uint32[]' 256
    for ($n = 0; $n -lt 256; $n++) {
        $c = [uint64]$n
        for ($k = 0; $k -lt 8; $k++) {
            $lsb = $c -band 1
            $c = $c -shr 1
            if ($lsb -ne 0) { $c = $c -bxor $script:POLY }
        }
        $t[$n] = [uint32]$c
    }
    return $t
}

$script:CRC = Get-CrcTable

function Get-Crc32([byte[]]$bytes) {
    $c = $script:U32
    foreach ($b in $bytes) {
        $idx = [int](($c -bxor [uint64]$b) -band 255)
        $c = ([uint64]$script:CRC[$idx]) -bxor ($c -shr 8)
    }
    return [uint32]($c -bxor $script:U32)
}

function Add-PngChunk([System.IO.MemoryStream]$ms, [string]$type, [byte[]]$data) {
    $ms.Write([byte[]][BitConverter]::GetBytes([uint32]$data.Length), 0, 4)
    $tb = [System.Text.Encoding]::ASCII.GetBytes($type)
    $ms.Write($tb, 0, 4)
    $ms.Write($data, 0, $data.Length)
    $crcInput = New-Object 'byte[]' ($tb.Length + $data.Length)
    [Array]::Copy($tb, 0, $crcInput, 0, $tb.Length)
    [Array]::Copy($data, 0, $crcInput, $tb.Length, $data.Length)
    $ms.Write([byte[]][BitConverter]::GetBytes([uint32](Get-Crc32 $crcInput)), 0, 4)
}

function ConvertTo-Png([int]$size, [byte[]]$rgba) {
    $raw = New-Object System.IO.MemoryStream
    for ($y = 0; $y -lt $size; $y++) {
        $raw.WriteByte(0)
        $raw.Write($rgba, $y * $size * 4, $size * 4)
    }
    $rawBytes = $raw.ToArray()
    $raw.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $ms.Write([byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), 0, 8)
    $ihdr = New-Object System.IO.MemoryStream
    $ihdr.Write([byte[]][BitConverter]::GetBytes([uint32]$size), 0, 4)
    $ihdr.Write([byte[]][BitConverter]::GetBytes([uint32]$size), 0, 4)
    $ihdr.WriteByte(8)
    $ihdr.WriteByte(6)
    $ihdr.WriteByte(0)
    $ihdr.WriteByte(0)
    $ihdr.WriteByte(0)
    Add-PngChunk $ms 'IHDR' $ihdr.ToArray()
    $ihdr.Dispose()

    $zms = New-Object System.IO.MemoryStream
    $zms.WriteByte(0x78)
    $zms.WriteByte(0x01)
    $pos = 0
    while ($pos -lt $rawBytes.Length) {
        $chunkLen = [Math]::Min(65535, $rawBytes.Length - $pos)
        $finalByte = [byte]0
        if (($pos + $chunkLen) -ge $rawBytes.Length) { $finalByte = [byte]1 }
        $zms.WriteByte($finalByte)
        $zms.Write([byte[]][BitConverter]::GetBytes([uint16]$chunkLen), 0, 2)
        $nlen = [uint16]([uint16]$chunkLen -bxor [uint16]0xFFFF)
        $zms.Write([byte[]][BitConverter]::GetBytes($nlen), 0, 2)
        $zms.Write($rawBytes, $pos, $chunkLen)
        $pos += $chunkLen
    }
    $a = [uint64]1
    $b = [uint64]0
    foreach ($byte in $rawBytes) {
        $a = ($a + [uint64]$byte) % [uint64]65521
        $b = ($b + $a) % [uint64]65521
    }
    $adler = [uint32](($b -shl 16) -bor $a)
    $zms.Write([byte[]][BitConverter]::GetBytes([uint32]$adler), 0, 4)
    Add-PngChunk $ms 'IDAT' $zms.ToArray()
    $zms.Dispose()
    Add-PngChunk $ms 'IEND' (New-Object 'byte[]' 0)
    $out = $ms.ToArray()
    $ms.Dispose()
    return $out
}

$entries = @{}
foreach ($s in $SIZES) {
    $rgba = New-RgbaBuffer $s
    $entries[$s] = ConvertTo-IconDib $s $rgba
    if ($s -eq 256) {
        [System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app-preview-256.png'), (ConvertTo-Png $s $rgba))
    }
    Write-Host ("size {0} -> {1} bytes" -f $s, $entries[$s].Length)
}

$ico = New-Object System.IO.MemoryStream
$ico.Write([byte[]](0, 0, 1, 0, [byte]$SIZES.Count, 0), 0, 6)
$offset = 6 + 16 * $SIZES.Count
foreach ($s in $SIZES) {
    $data = $entries[$s]
    $dim = $s
    if ($s -ge 256) { $dim = 0 }
    $ico.WriteByte([byte]$dim)
    $ico.WriteByte([byte]$dim)
    $ico.WriteByte([byte]0)
    $ico.WriteByte([byte]0)
    $ico.Write([byte[]][BitConverter]::GetBytes([uint16]1), 0, 2)
    $ico.Write([byte[]][BitConverter]::GetBytes([uint16]32), 0, 2)
    $ico.Write([byte[]][BitConverter]::GetBytes([uint32]$data.Length), 0, 4)
    $ico.Write([byte[]][BitConverter]::GetBytes([uint32]$offset), 0, 4)
    $offset += $data.Length
}
foreach ($s in $SIZES) { $ico.Write($entries[$s], 0, $entries[$s].Length) }

$icoPath = Join-Path $OutDir 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$ico.Dispose()
Write-Host ("wrote {0} ({1} bytes)" -f $icoPath, (Get-Item $icoPath).Length)
