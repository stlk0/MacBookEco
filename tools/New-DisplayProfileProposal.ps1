[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EdidPath,

    [Parameter(Mandatory = $true)]
    [string]$SystemModel,

    [Parameter(Mandatory = $true)]
    [string]$GpuDeviceIdPrefix,

    [string]$GpuName = '',
    [string]$DriverVersion = '',
    [string]$DisplayName = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$blockLength = 128
$descriptorLength = 18
$firstDescriptorOffset = 54
$targetRefresh = 48

function Test-Checksum {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    $sum = 0
    for ($index = 0; $index -lt $script:blockLength; $index++) {
        $sum = ($sum + $Bytes[$Offset + $index]) -band 0xFF
    }
    return $sum -eq 0
}

function Read-DetailedTiming {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    $pixelClock = $Bytes[$Offset] -bor ($Bytes[$Offset + 1] -shl 8)
    if ($pixelClock -eq 0) {
        throw 'The preferred descriptor is not a detailed timing.'
    }

    return [pscustomobject]@{
        PixelClock10Khz = $pixelClock
        HorizontalActive = $Bytes[$Offset + 2] -bor `
            (($Bytes[$Offset + 4] -band 0xF0) -shl 4)
        HorizontalBlanking = $Bytes[$Offset + 3] -bor `
            (($Bytes[$Offset + 4] -band 0x0F) -shl 8)
        VerticalActive = $Bytes[$Offset + 5] -bor `
            (($Bytes[$Offset + 7] -band 0xF0) -shl 4)
        VerticalBlanking = $Bytes[$Offset + 6] -bor `
            (($Bytes[$Offset + 7] -band 0x0F) -shl 8)
        HorizontalSyncOffset = $Bytes[$Offset + 8] -bor `
            (($Bytes[$Offset + 11] -band 0xC0) -shl 2)
        HorizontalSyncWidth = $Bytes[$Offset + 9] -bor `
            (($Bytes[$Offset + 11] -band 0x30) -shl 4)
        VerticalSyncOffset = (($Bytes[$Offset + 10] -band 0xF0) -shr 4) -bor `
            (($Bytes[$Offset + 11] -band 0x0C) -shl 2)
        VerticalSyncWidth = ($Bytes[$Offset + 10] -band 0x0F) -bor `
            (($Bytes[$Offset + 11] -band 0x03) -shl 4)
        Flags = $Bytes[$Offset + 17]
    }
}

function Test-FreeDescriptor {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    $allZero = $true
    for ($index = 0; $index -lt $script:descriptorLength; $index++) {
        if ($Bytes[$Offset + $index] -ne 0) {
            $allZero = $false
            break
        }
    }
    if ($allZero) {
        return $true
    }

    if ($Bytes[$Offset] -ne 0 -or
        $Bytes[$Offset + 1] -ne 0 -or
        $Bytes[$Offset + 2] -ne 0 -or
        $Bytes[$Offset + 3] -ne 0x10 -or
        $Bytes[$Offset + 4] -ne 0) {
        return $false
    }
    for ($index = 5; $index -lt $script:descriptorLength; $index++) {
        if ($Bytes[$Offset + $index] -ne 0) {
            return $false
        }
    }
    return $true
}

function Format-Hex {
    param([byte[]]$Bytes)

    return (($Bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function Format-Sha256 {
    param([byte[]]$Bytes)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $algorithm.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
    }
}

function Decode-Manufacturer {
    param([byte[]]$Bytes)

    $encoded = ($Bytes[8] -shl 8) -bor $Bytes[9]
    $values = @(
        (($encoded -shr 10) -band 0x1F),
        (($encoded -shr 5) -band 0x1F),
        ($encoded -band 0x1F))
    if (@($values | Where-Object { $_ -lt 1 -or $_ -gt 26 }).Count -ne 0) {
        throw 'The EDID has an invalid manufacturer code.'
    }

    return -join @($values | ForEach-Object { [char](64 + $_) })
}

if (-not (Test-Path -LiteralPath $EdidPath -PathType Leaf)) {
    throw "EDID input was not found: $EdidPath"
}
$edid = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($EdidPath))
if ($edid.Length -lt $blockLength -or $edid.Length % $blockLength -ne 0) {
    throw 'The input must be a complete binary EDID document.'
}
$header = @(0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
for ($index = 0; $index -lt $header.Count; $index++) {
    if ($edid[$index] -ne $header[$index]) {
        throw 'The EDID base-block header is invalid.'
    }
}
$expectedLength = ($edid[126] + 1) * $blockLength
if ($edid.Length -ne $expectedLength) {
    throw 'The EDID extension count does not match the document length.'
}
for ($offset = 0; $offset -lt $edid.Length; $offset += $blockLength) {
    if (-not (Test-Checksum $edid $offset)) {
        throw "EDID block $($offset / $blockLength) has an invalid checksum."
    }
}
if (($edid[24] -band 0x02) -eq 0) {
    throw 'The EDID does not declare the first descriptor as preferred timing.'
}

$nativeOffset = $firstDescriptorOffset
$native = Read-DetailedTiming $edid $nativeOffset
if (($native.Flags -band 0x80) -ne 0) {
    throw 'Interlaced native timings are not supported.'
}
if ($native.HorizontalSyncOffset + $native.HorizontalSyncWidth -gt
        $native.HorizontalBlanking -or
    $native.VerticalSyncOffset + $native.VerticalSyncWidth -gt
        $native.VerticalBlanking) {
    throw 'The native timing has sync outside blanking.'
}

$freeDescriptor = $false
for ($index = 1; $index -lt 4; $index++) {
    $offset = $firstDescriptorOffset + ($index * $descriptorLength)
    if (Test-FreeDescriptor $edid $offset) {
        $freeDescriptor = $true
        break
    }
}
if (-not $freeDescriptor) {
    throw 'The base EDID has no free non-preferred descriptor.'
}

$horizontalTotal = $native.HorizontalActive + $native.HorizontalBlanking
$nativeVerticalTotal = $native.VerticalActive + $native.VerticalBlanking
$nativeClockHz = [int64]$native.PixelClock10Khz * 10000
$nativeRefresh = $nativeClockHz / `
    ([double]$horizontalTotal * $nativeVerticalTotal)
if ($nativeRefresh -lt 59.0 -or $nativeRefresh -gt 61.0) {
    throw 'The preferred native refresh is outside 59 through 61 Hz.'
}

$targetVerticalTotal = [int64][Math]::Floor(
    $nativeClockHz / ([double]$horizontalTotal * $targetRefresh))
$targetVerticalBlanking = $targetVerticalTotal - $native.VerticalActive
if ($targetVerticalBlanking -le $native.VerticalBlanking -or
    $targetVerticalBlanking -gt 4095) {
    throw 'The 48 Hz vertical blanking cannot be encoded safely.'
}
$clockNumerator = [int64]$horizontalTotal *
    $targetVerticalTotal * $targetRefresh
$targetClock10Khz = [int64][Math]::Floor(
    ($clockNumerator + 5000) / 10000.0)
if ($targetClock10Khz -lt 1 -or
    $targetClock10Khz -gt 65535 -or
    $targetClock10Khz -gt $native.PixelClock10Khz) {
    throw 'The 48 Hz pixel clock cannot be encoded safely.'
}
$targetActualRefresh = ($targetClock10Khz * 10000.0) / `
    ([double]$horizontalTotal * $targetVerticalTotal)
if ([Math]::Abs($targetActualRefresh - $targetRefresh) -gt 0.01) {
    throw 'The encoded target is not within 0.01 Hz of 48 Hz.'
}

$nativeBytes = [byte[]]::new($descriptorLength)
[Array]::Copy($edid, $nativeOffset, $nativeBytes, 0, $descriptorLength)
$targetBytes = [byte[]]$nativeBytes.Clone()
$targetBytes[0] = [byte]($targetClock10Khz -band 0xFF)
$targetBytes[1] = [byte](($targetClock10Khz -shr 8) -band 0xFF)
$targetBytes[6] = [byte]($targetVerticalBlanking -band 0xFF)
$targetBytes[7] = [byte](
    ($targetBytes[7] -band 0xF0) -bor
    (($targetVerticalBlanking -shr 8) -band 0x0F))

for ($index = 1; $index -lt 4; $index++) {
    $offset = $firstDescriptorOffset + ($index * $descriptorLength)
    $same = $true
    for ($byteIndex = 0; $byteIndex -lt $descriptorLength; $byteIndex++) {
        if ($edid[$offset + $byteIndex] -ne $targetBytes[$byteIndex]) {
            $same = $false
            break
        }
    }
    if ($same) {
        throw 'The calculated 48 Hz timing already exists in the base EDID.'
    }
}

$normalized = [byte[]]::new($blockLength)
[Array]::Copy($edid, 0, $normalized, 0, $blockLength)
for ($index = 12; $index -le 17; $index++) {
    $normalized[$index] = 0
}
for ($index = $firstDescriptorOffset + $descriptorLength;
    $index -lt 126;
    $index++) {
    $normalized[$index] = 0
}
$normalized[127] = 0

$manufacturer = Decode-Manufacturer $edid
$productCode = $edid[10] -bor ($edid[11] -shl 8)
$panelHardwareId = $manufacturer + $productCode.ToString('X4')
$SystemModel = $SystemModel.Trim()
$GpuDeviceIdPrefix = $GpuDeviceIdPrefix.Trim().ToUpperInvariant()
if ($SystemModel -notmatch '^MacBookPro[0-9]+,[0-9]+$') {
    throw 'SystemModel must be an exact MacBookPro SMBIOS identifier.'
}
if ($GpuDeviceIdPrefix -cnotmatch '^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}$') {
    throw 'GpuDeviceIdPrefix must be canonical PCI\VEN_xxxx&DEV_xxxx text.'
}
if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    $DisplayName = "$SystemModel / $panelHardwareId"
}
$profileId = (($SystemModel + '-' + $panelHardwareId + '-48hz').ToLowerInvariant() `
    -replace '[^a-z0-9]+', '-').Trim('-')

$proposal = [ordered]@{
    id = $profileId
    displayName = $DisplayName.Trim()
    systemModels = @($SystemModel)
    panelHardwareId = $panelHardwareId
    normalizedEdidSignature = Format-Sha256 $normalized
    nativeTiming = Format-Hex $nativeBytes
    targetTiming = Format-Hex $targetBytes
    verifiedGpuName = $GpuName.Trim()
    verifiedGpuDeviceIdPrefix = $GpuDeviceIdPrefix
    verifiedDriverVersion = $DriverVersion.Trim()
}
$json = ($proposal | ConvertTo-Json -Depth 3) + "`r`n"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $json
    return
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $fullOutputPath) {
    throw "Refusing to overwrite existing proposal: $fullOutputPath"
}
[IO.File]::WriteAllText(
    $fullOutputPath,
    $json,
    [Text.UTF8Encoding]::new($false))
Write-Host "Wrote profile proposal: $fullOutputPath"
Write-Host 'The proposal is not runtime data; review it before copying it to profiles.'
