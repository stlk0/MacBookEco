[CmdletBinding(DefaultParameterSetName = 'Check')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Propose')]
    [switch]$Propose,
    [Parameter(Mandatory = $true, ParameterSetName = 'Generate')]
    [switch]$Generate,
    [Parameter(Mandatory = $true, ParameterSetName = 'Check')]
    [switch]$Check,
    [Parameter(Mandatory = $true, ParameterSetName = 'Propose')]
    [string]$EdidPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Propose')]
    [string]$SystemModel,
    [Parameter(Mandatory = $true, ParameterSetName = 'Propose')]
    [string]$GpuDeviceIdPrefix,
    [Parameter(ParameterSetName = 'Propose')]
    [string]$GpuName = '',
    [Parameter(ParameterSetName = 'Propose')]
    [string]$DriverVersion = '',
    [Parameter(ParameterSetName = 'Propose')]
    [string]$DisplayName = '',
    [Parameter(ParameterSetName = 'Propose')]
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$profilesPath = Join-Path $repoRoot 'profiles'
$catalogPath = Join-Path `
    $repoRoot `
    'src\DisplayProfiles\ProfileCatalog.Generated.cs'
$blockLength = 128
$dtdLength = 18
$dtdOffset = 54

function ConvertFrom-DtdHex {
    param([string]$Value, [string]$Source)

    $value = $Value.Trim()
    if ($value -cnotmatch '^[0-9A-F]{2}( [0-9A-F]{2}){17}$') {
        throw "$Source must contain one canonical 18-byte DTD."
    }
    return [byte[]]@($value.Split(' ') | ForEach-Object {
        [Convert]::ToByte($_, 16)
    })
}

function Read-Dtd {
    param([byte[]]$Bytes, [int]$Offset = 0)

    if ($null -eq $Bytes -or $Offset -lt 0 -or
        $Offset + $script:dtdLength -gt $Bytes.Length) {
        throw 'A complete detailed timing descriptor is required.'
    }
    $clock = [int]$Bytes[$Offset] -bor
        ([int]$Bytes[$Offset + 1] -shl 8)
    if ($clock -eq 0) {
        throw 'The descriptor is not a detailed timing.'
    }
    $encoded = [byte[]]::new($script:dtdLength)
    [Array]::Copy($Bytes, $Offset, $encoded, 0, $script:dtdLength)
    return [pscustomobject]@{
        Bytes = $encoded
        Clock = $clock
        HActive = [int]$Bytes[$Offset + 2] -bor
            (([int]$Bytes[$Offset + 4] -band 0xF0) -shl 4)
        HBlank = [int]$Bytes[$Offset + 3] -bor
            (([int]$Bytes[$Offset + 4] -band 0x0F) -shl 8)
        VActive = [int]$Bytes[$Offset + 5] -bor
            (([int]$Bytes[$Offset + 7] -band 0xF0) -shl 4)
        VBlank = [int]$Bytes[$Offset + 6] -bor
            (([int]$Bytes[$Offset + 7] -band 0x0F) -shl 8)
        HSyncOffset = [int]$Bytes[$Offset + 8] -bor
            (([int]$Bytes[$Offset + 11] -band 0xC0) -shl 2)
        HSyncWidth = [int]$Bytes[$Offset + 9] -bor
            (([int]$Bytes[$Offset + 11] -band 0x30) -shl 4)
        VSyncOffset =
            (([int]$Bytes[$Offset + 10] -band 0xF0) -shr 4) -bor
            (([int]$Bytes[$Offset + 11] -band 0x0C) -shl 2)
        VSyncWidth =
            ([int]$Bytes[$Offset + 10] -band 0x0F) -bor
            (([int]$Bytes[$Offset + 11] -band 0x03) -shl 4)
        Flags = $Bytes[$Offset + 17]
    }
}

function New-TargetDtd {
    param([object]$Native, [string]$Source)

    if (($Native.Flags -band 0x80) -ne 0) {
        throw "$Source has an interlaced native timing."
    }
    if ($Native.HSyncOffset + $Native.HSyncWidth -gt $Native.HBlank -or
        $Native.VSyncOffset + $Native.VSyncWidth -gt $Native.VBlank) {
        throw "$Source has native sync outside blanking."
    }
    $hTotal = $Native.HActive + $Native.HBlank
    $nativeClockHz = [int64]$Native.Clock * 10000
    $nativeRefresh = $nativeClockHz /
        ([double]$hTotal * ($Native.VActive + $Native.VBlank))
    if ($nativeRefresh -lt 59.0 -or $nativeRefresh -gt 61.0) {
        throw "$Source native refresh is outside 59 through 61 Hz."
    }
    $vTotal = [int64][Math]::Floor(
        $nativeClockHz / ([double]$hTotal * 48))
    $vBlank = $vTotal - $Native.VActive
    if ($vBlank -le $Native.VBlank -or $vBlank -gt 4095) {
        throw "$Source cannot encode the 48 Hz vertical blanking safely."
    }
    $clock = [int64][Math]::Floor(
        (([int64]$hTotal * $vTotal * 48) + 5000) / 10000.0)
    if ($clock -lt 1 -or $clock -gt 65535 -or $clock -gt $Native.Clock) {
        throw "$Source cannot encode the 48 Hz pixel clock safely."
    }
    if ([Math]::Abs(($clock * 10000.0) / ($hTotal * $vTotal) - 48) -gt 0.01) {
        throw "$Source target is not within 0.01 Hz of 48 Hz."
    }
    $target = [byte[]]$Native.Bytes.Clone()
    $target[0] = [byte]($clock -band 0xFF)
    $target[1] = [byte](($clock -shr 8) -band 0xFF)
    $target[6] = [byte]($vBlank -band 0xFF)
    $target[7] = [byte](
        ($target[7] -band 0xF0) -bor (($vBlank -shr 8) -band 0x0F))
    return $target
}

function Format-Hex {
    param([byte[]]$Bytes)
    return (($Bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function Test-BytesEqual {
    param([byte[]]$Left, [byte[]]$Right)

    if ($null -eq $Left -or $null -eq $Right -or
        $Left.Length -ne $Right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }
    return $true
}

function Get-ProfileText {
    param([object]$Profile, [string]$Name, [string]$Source, [switch]$Optional)

    $property = $Profile.PSObject.Properties[$Name]
    $value = if ($null -eq $property) { '' } else { [string]$property.Value }
    if ([string]::IsNullOrWhiteSpace($value)) {
        if ($Optional) { return '' }
        throw "$Source has an empty '$Name'."
    }
    if ($value -match '[\x00-\x1F]') {
        throw "$Source has a control character in '$Name'."
    }
    return $value.Trim()
}

function ConvertTo-CSharpString {
    param([string]$Value)
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Read-Profiles {
    $files = @(Get-ChildItem $script:profilesPath -Filter '*.json' -File |
        Sort-Object Name)
    if ($files.Count -eq 0) {
        throw "No profile manifests were found in $script:profilesPath."
    }
    $ids = @{}
    $identities = @{}
    return @(foreach ($file in $files) {
        $profile = Get-Content $file.FullName -Raw | ConvertFrom-Json
        $id = Get-ProfileText $profile 'id' $file.Name
        if ($id -cnotmatch '^[a-z0-9][a-z0-9-]{0,95}$' -or
            $file.BaseName -cne $id -or $ids.ContainsKey($id)) {
            throw "$($file.Name) has a duplicate or non-canonical profile ID."
        }
        $ids[$id] = $true
        $models = @($profile.systemModels | ForEach-Object {
            ([string]$_).Trim()
        })
        if ($models.Count -eq 0 -or @($models | Where-Object {
            $_ -cnotmatch '^MacBookPro[0-9]+,[0-9]+$'
        }).Count -ne 0) {
            throw "$($file.Name) has a non-canonical SMBIOS model."
        }
        $panel = Get-ProfileText $profile 'panelHardwareId' $file.Name
        $signature = Get-ProfileText `
            $profile 'normalizedEdidSignature' $file.Name
        $gpu = Get-ProfileText `
            $profile 'verifiedGpuDeviceIdPrefix' $file.Name
        if ($panel -cnotmatch '^[A-Z]{3}[0-9A-F]{4}$' -or
            $signature -cnotmatch '^[0-9A-F]{64}$' -or
            $gpu -cnotmatch '^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}$') {
            throw "$($file.Name) has non-canonical hardware identity."
        }
        foreach ($model in $models) {
            $identity = "$model|$panel|$signature|$gpu"
            if ($identities.ContainsKey($identity)) {
                throw "$($file.Name) duplicates an existing hardware identity."
            }
            $identities[$identity] = $true
        }
        $native = ConvertFrom-DtdHex `
            (Get-ProfileText $profile 'nativeTiming' $file.Name) `
            "$($file.Name) native timing"
        $target = ConvertFrom-DtdHex `
            (Get-ProfileText $profile 'targetTiming' $file.Name) `
            "$($file.Name) target timing"
        if (-not (Test-BytesEqual $target (New-TargetDtd `
                (Read-Dtd $native) $file.Name))) {
            throw "$($file.Name) target does not match the 48 Hz formula."
        }
        [pscustomobject]@{
            Id = $id
            Name = Get-ProfileText $profile 'displayName' $file.Name
            Models = $models
            Panel = $panel
            Signature = $signature
            Native = Format-Hex $native
            Target = Format-Hex $target
            GpuName = Get-ProfileText `
                $profile 'verifiedGpuName' $file.Name -Optional
            Gpu = $gpu
            Driver = Get-ProfileText `
                $profile 'verifiedDriverVersion' $file.Name -Optional
        }
    })
}

function New-CatalogSource {
    param([object[]]$Profiles)

    $blocks = @(foreach ($profile in $Profiles) {
        $id = ConvertTo-CSharpString $profile.Id
        $name = ConvertTo-CSharpString $profile.Name
        $models = @($profile.Models | ForEach-Object {
            ConvertTo-CSharpString $_
        }) -join ', '
        $panel = ConvertTo-CSharpString $profile.Panel
        $signature = ConvertTo-CSharpString $profile.Signature
        $native = ConvertTo-CSharpString $profile.Native
        $target = ConvertTo-CSharpString $profile.Target
        $gpuName = ConvertTo-CSharpString $profile.GpuName
        $gpu = ConvertTo-CSharpString $profile.Gpu
        $driver = ConvertTo-CSharpString $profile.Driver
@"
                new DisplayProfile(
                    $id,
                    $name,
                    new[] { $models },
                    $panel,
                    $signature,
                    DetailedTiming.ParseHex(
                        $native),
                    DetailedTiming.ParseHex(
                        $target),
                    $gpuName,
                    $gpu,
                    $driver)
"@
    })
    return @"
// Generated by tools/ProfileAuthoring.ps1. Do not edit by hand.
namespace MacBookEco.Core
{
    internal static class GeneratedProfileCatalog
    {
        internal static DisplayProfile[] Create()
        {
            return new[]
            {
$($blocks -join ",`r`n")
            };
        }
    }
}

"@
}

function Invoke-Catalog {
    $profiles = @(Read-Profiles)
    $expected = New-CatalogSource $profiles
    if ($Check) {
        $actual = if (Test-Path $script:catalogPath -PathType Leaf) {
            Get-Content $script:catalogPath -Raw
        } else {
            ''
        }
        if (($actual -replace "`r`n", "`n") -cne
            ($expected -replace "`r`n", "`n")) {
            throw ('ProfileCatalog.Generated.cs is stale. Run ' +
                'tools\ProfileAuthoring.ps1 -Generate.')
        }
        Write-Host "Profile catalog is current ($($profiles.Count) profile(s))."
        return
    }
    [IO.File]::WriteAllText(
        $script:catalogPath,
        $expected,
        [Text.UTF8Encoding]::new($false))
    Write-Host "Generated $script:catalogPath from $($profiles.Count) profile(s)."
}

function Test-Checksum {
    param([byte[]]$Bytes, [int]$Offset)
    $sum = 0
    for ($index = 0; $index -lt $script:blockLength; $index++) {
        $sum = ($sum + $Bytes[$Offset + $index]) -band 0xFF
    }
    return $sum -eq 0
}

function Test-FreeDescriptor {
    param([byte[]]$Bytes, [int]$Offset)
    $descriptor = [byte[]]::new($script:dtdLength)
    [Array]::Copy($Bytes, $Offset, $descriptor, 0, $script:dtdLength)
    return @($descriptor | Where-Object { $_ -ne 0 }).Count -eq 0 -or
        ($descriptor[0] -eq 0 -and $descriptor[1] -eq 0 -and
        $descriptor[2] -eq 0 -and $descriptor[3] -eq 0x10 -and
        @($descriptor[4..17] | Where-Object { $_ -ne 0 }).Count -eq 0)
}

function Get-NormalizedSignature {
    param([byte[]]$Edid)
    $normalized = [byte[]]::new($script:blockLength)
    [Array]::Copy($Edid, 0, $normalized, 0, $script:blockLength)
    12..17 | ForEach-Object { $normalized[$_] = 0 }
    ($script:dtdOffset + $script:dtdLength)..125 | ForEach-Object {
        $normalized[$_] = 0
    }
    $normalized[127] = 0
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha256.ComputeHash($normalized))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Invoke-Proposal {
    if (-not (Test-Path $EdidPath -PathType Leaf)) {
        throw "EDID input was not found: $EdidPath"
    }
    $edid = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($EdidPath))
    [byte[]]$header = @(0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
    if ($edid.Length -lt $script:blockLength -or
        $edid.Length % $script:blockLength -ne 0 -or
        -not (Test-BytesEqual $header $edid[0..7])) {
        throw 'The input is not a complete binary EDID document.'
    }
    if ($edid.Length -ne ($edid[126] + 1) * $script:blockLength) {
        throw 'The EDID extension count does not match the document length.'
    }
    for ($offset = 0; $offset -lt $edid.Length; $offset += $script:blockLength) {
        if (-not (Test-Checksum $edid $offset)) {
            throw "EDID block $($offset / $script:blockLength) has an invalid checksum."
        }
    }
    if (($edid[24] -band 0x02) -eq 0) {
        throw 'The EDID does not declare the first descriptor as preferred timing.'
    }
    if (@(1..3 | Where-Object {
        Test-FreeDescriptor $edid ($script:dtdOffset + ($_ * $script:dtdLength))
    }).Count -eq 0) {
        throw 'The base EDID has no free non-preferred descriptor.'
    }
    $native = Read-Dtd $edid $script:dtdOffset
    $target = [byte[]](New-TargetDtd $native 'The preferred timing')
    foreach ($index in 1..3) {
        $offset = $script:dtdOffset + ($index * $script:dtdLength)
        if (Test-BytesEqual $target $edid[$offset..($offset + 17)]) {
            throw 'The calculated 48 Hz timing already exists in the base EDID.'
        }
    }

    $model = $SystemModel.Trim()
    $gpu = $GpuDeviceIdPrefix.Trim().ToUpperInvariant()
    if ($model -notmatch '^MacBookPro[0-9]+,[0-9]+$' -or
        $gpu -cnotmatch '^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}$') {
        throw 'Canonical MacBookPro model and PCI VEN/DEV values are required.'
    }
    $encoded = ([int]$edid[8] -shl 8) -bor [int]$edid[9]
    $manufacturerValues = @(
        (($encoded -shr 10) -band 0x1F),
        (($encoded -shr 5) -band 0x1F),
        ($encoded -band 0x1F))
    if (@($manufacturerValues | Where-Object {
        $_ -lt 1 -or $_ -gt 26
    }).Count -ne 0) {
        throw 'The EDID has an invalid manufacturer code.'
    }
    $manufacturer = -join @($manufacturerValues | ForEach-Object {
        [char](64 + $_)
    })
    $product = [int]$edid[10] -bor ([int]$edid[11] -shl 8)
    $panel = $manufacturer + $product.ToString('X4')
    $name = if ([string]::IsNullOrWhiteSpace($DisplayName)) {
        "$model / $panel"
    } else {
        $DisplayName.Trim()
    }
    $proposal = [ordered]@{
        id = (($model + '-' + $panel + '-48hz').ToLowerInvariant() -replace
            '[^a-z0-9]+', '-').Trim('-')
        displayName = $name
        systemModels = @($model)
        panelHardwareId = $panel
        normalizedEdidSignature = Get-NormalizedSignature $edid
        nativeTiming = Format-Hex $native.Bytes
        targetTiming = Format-Hex $target
        verifiedGpuName = $GpuName.Trim()
        verifiedGpuDeviceIdPrefix = $gpu
        verifiedDriverVersion = $DriverVersion.Trim()
    }
    $json = ($proposal | ConvertTo-Json -Depth 3) + "`r`n"
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $json
        return
    }
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    if (Test-Path $fullOutputPath) {
        throw "Refusing to overwrite existing proposal: $fullOutputPath"
    }
    [IO.File]::WriteAllText(
        $fullOutputPath,
        $json,
        [Text.UTF8Encoding]::new($false))
    Write-Host "Wrote profile proposal: $fullOutputPath"
    Write-Host 'Review it before copying it to profiles.'
}

if ($Propose) {
    Invoke-Proposal
} else {
    Invoke-Catalog
}
