[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$profilesPath = Join-Path $repoRoot 'profiles'
$outputPath = Join-Path `
    $repoRoot `
    'src\DisplayProfiles\ProfileCatalog.Generated.cs'

function Require-Text {
    param(
        [object]$Profile,
        [string]$Name,
        [string]$Source
    )

    $value = [string]$Profile.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Source has an empty '$Name'."
    }
    if ($value -match '[\x00-\x1F]') {
        throw "$Source has a control character in '$Name'."
    }

    return $value.Trim()
}

function Get-OptionalText {
    param(
        [object]$Profile,
        [string]$Name
    )

    $value = [string]$Profile.$Name
    if ([string]::IsNullOrEmpty($value)) {
        return ''
    }
    if ($value -match '[\x00-\x1F]') {
        throw "Profile field '$Name' has a control character."
    }

    return $value.Trim()
}

function Escape-CSharp {
    param([string]$Value)

    return $Value.Replace('\', '\\').Replace('"', '\"')
}

function Normalize-DetailedTiming {
    param(
        [string]$Value,
        [string]$Name,
        [string]$Source
    )

    $canonical = $Value.Trim()
    if ($canonical -cnotmatch '^[0-9A-F]{2}( [0-9A-F]{2}){17}$') {
        throw "$Source has a non-canonical '$Name'; exactly 18 bytes are required."
    }

    $parts = $canonical.Split(' ')

    if ($parts[0] -eq '00' -and $parts[1] -eq '00') {
        throw "$Source has a zero pixel clock in '$Name'."
    }

    return $canonical
}

function Read-DetailedTiming {
    param([string]$Value)

    $bytes = @($Value.Split(' ') | ForEach-Object {
        [Convert]::ToInt32($_, 16)
    })
    $horizontalActive = $bytes[2] -bor (($bytes[4] -band 0xF0) -shl 4)
    $horizontalBlanking = $bytes[3] -bor (($bytes[4] -band 0x0F) -shl 8)
    $verticalActive = $bytes[5] -bor (($bytes[7] -band 0xF0) -shl 4)
    $verticalBlanking = $bytes[6] -bor (($bytes[7] -band 0x0F) -shl 8)
    $horizontalSyncOffset = $bytes[8] -bor (($bytes[11] -band 0xC0) -shl 2)
    $horizontalSyncWidth = $bytes[9] -bor (($bytes[11] -band 0x30) -shl 4)
    $verticalSyncOffset = (($bytes[10] -band 0xF0) -shr 4) -bor `
        (($bytes[11] -band 0x0C) -shl 2)
    $verticalSyncWidth = ($bytes[10] -band 0x0F) -bor `
        (($bytes[11] -band 0x03) -shl 4)

    return [pscustomobject]@{
        PixelClock10Khz = $bytes[0] -bor ($bytes[1] -shl 8)
        HorizontalActive = $horizontalActive
        HorizontalBlanking = $horizontalBlanking
        VerticalActive = $verticalActive
        VerticalBlanking = $verticalBlanking
        HorizontalSyncOffset = $horizontalSyncOffset
        HorizontalSyncWidth = $horizontalSyncWidth
        VerticalSyncOffset = $verticalSyncOffset
        VerticalSyncWidth = $verticalSyncWidth
    }
}

function Validate-TimingPair {
    param(
        [string]$NativeValue,
        [string]$TargetValue,
        [string]$Source
    )

    $native = Read-DetailedTiming $NativeValue
    $target = Read-DetailedTiming $TargetValue
    $nativeBytes = $NativeValue.Split(' ')
    $targetBytes = $TargetValue.Split(' ')
    if (([Convert]::ToInt32($nativeBytes[17], 16) -band 0x80) -ne 0) {
        throw "$Source has an interlaced native timing."
    }
    if ($native.HorizontalActive -ne $target.HorizontalActive -or
        $native.VerticalActive -ne $target.VerticalActive) {
        throw "$Source changes the active resolution."
    }
    if ($target.PixelClock10Khz -gt $native.PixelClock10Khz) {
        throw "$Source raises the target pixel clock."
    }
    if ($target.HorizontalBlanking -ne $native.HorizontalBlanking -or
        $target.HorizontalSyncOffset -ne $native.HorizontalSyncOffset -or
        $target.HorizontalSyncWidth -ne $native.HorizontalSyncWidth -or
        $target.VerticalSyncOffset -ne $native.VerticalSyncOffset -or
        $target.VerticalSyncWidth -ne $native.VerticalSyncWidth) {
        throw "$Source changes timing fields other than vertical back porch."
    }
    if ($target.HorizontalSyncOffset + $target.HorizontalSyncWidth -gt
            $target.HorizontalBlanking -or
        $target.VerticalSyncOffset + $target.VerticalSyncWidth -gt
            $target.VerticalBlanking) {
        throw "$Source has target sync outside blanking."
    }
    if ($native.HorizontalSyncOffset + $native.HorizontalSyncWidth -gt
            $native.HorizontalBlanking -or
        $native.VerticalSyncOffset + $native.VerticalSyncWidth -gt
            $native.VerticalBlanking) {
        throw "$Source has native sync outside blanking."
    }

    $nativeTotalPixels = [int64]($native.HorizontalActive +
        $native.HorizontalBlanking) * [int64]($native.VerticalActive +
        $native.VerticalBlanking)
    $nativeClockHz = [int64]$native.PixelClock10Khz * 10000
    $nativeRefresh = $nativeClockHz / [double]$nativeTotalPixels
    if ($nativeRefresh -lt 59.0 -or $nativeRefresh -gt 61.0) {
        throw "$Source native refresh is outside 59 through 61 Hz."
    }

    $horizontalTotal = $native.HorizontalActive + $native.HorizontalBlanking
    $expectedVerticalTotal = [int64][Math]::Floor(
        $nativeClockHz / ([double]$horizontalTotal * 48))
    $expectedVerticalBlanking = $expectedVerticalTotal -
        $native.VerticalActive
    $clockNumerator = [int64]$horizontalTotal *
        $expectedVerticalTotal * 48
    $expectedClock = [int64][Math]::Floor(
        ($clockNumerator + 5000) / 10000.0)
    if ($target.VerticalBlanking -ne $expectedVerticalBlanking -or
        $target.PixelClock10Khz -ne $expectedClock) {
        throw "$Source target does not follow the reviewed 48 Hz formula."
    }
    if ($target.VerticalBlanking -le $native.VerticalBlanking -or
        $target.VerticalBlanking -gt 4095) {
        throw "$Source target vertical blanking is outside the safe range."
    }

    foreach ($index in @(2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17)) {
        if ($nativeBytes[$index] -cne $targetBytes[$index]) {
            throw "$Source changes a preserved DTD byte at index $index."
        }
    }
    if (([Convert]::ToInt32($nativeBytes[7], 16) -band 0xF0) -ne
        ([Convert]::ToInt32($targetBytes[7], 16) -band 0xF0)) {
        throw "$Source changes the vertical-active DTD bits."
    }

    $totalPixels = [int64]($target.HorizontalActive +
        $target.HorizontalBlanking) * [int64]($target.VerticalActive +
        $target.VerticalBlanking)
    $refresh = ([int64]$target.PixelClock10Khz * 10000.0) / $totalPixels
    if ([Math]::Abs($refresh - 48.0) -gt 0.01) {
        throw "$Source target refresh is not within 0.01 Hz of 48 Hz."
    }
}

function Add-StringLine {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Indent,
        [string]$Value,
        [string]$Suffix
    )

    $Lines.Add($Indent + '"' + (Escape-CSharp $Value) + '"' + $Suffix)
}

$files = @(Get-ChildItem -LiteralPath $profilesPath -Filter '*.json' -File |
    Sort-Object -Property Name)
if ($files.Count -eq 0) {
    throw "No profile manifests were found in $profilesPath."
}

$ids = @{}
$identities = @{}
$profiles = @(foreach ($file in $files) {
    $profile = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $id = Require-Text $profile 'id' $file.Name
    if ($id -cnotmatch '^[a-z0-9][a-z0-9-]{0,95}$') {
        throw "$($file.Name) has a non-canonical profile ID."
    }
    if ($ids.ContainsKey($id)) {
        throw "Duplicate profile ID '$id'."
    }
    $ids.Add($id, $true)
    if ($file.BaseName -cne $id) {
        throw "$($file.Name) must use its canonical profile ID as its filename."
    }

    $systemModels = @($profile.systemModels)
    if ($systemModels.Count -eq 0) {
        throw "$($file.Name) has no system models."
    }
    $systemModels = @($systemModels | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace([string]$_)) {
            throw "$($file.Name) has an empty system model."
        }
        ([string]$_).Trim()
    })
    foreach ($model in $systemModels) {
        if ($model -cnotmatch '^MacBookPro[0-9]+,[0-9]+$') {
            throw "$($file.Name) has a non-canonical SMBIOS model."
        }
    }

    $panel = Require-Text $profile 'panelHardwareId' $file.Name
    if ($panel -cnotmatch '^[A-Z]{3}[0-9A-F]{4}$') {
        throw "$($file.Name) has a non-canonical panel hardware ID."
    }
    $signature = Require-Text `
        $profile `
        'normalizedEdidSignature' `
        $file.Name
    if ($signature -cnotmatch '^[0-9A-F]{64}$') {
        throw "$($file.Name) has a non-canonical EDID signature."
    }

    $native = Normalize-DetailedTiming `
        (Require-Text $profile 'nativeTiming' $file.Name) `
        'nativeTiming' `
        $file.Name
    $target = Normalize-DetailedTiming `
        (Require-Text $profile 'targetTiming' $file.Name) `
        'targetTiming' `
        $file.Name
    Validate-TimingPair $native $target $file.Name

    $gpuPrefix = Require-Text `
        $profile `
        'verifiedGpuDeviceIdPrefix' `
        $file.Name
    if ($gpuPrefix -cnotmatch '^PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}$') {
        throw "$($file.Name) has a non-canonical GPU device ID prefix."
    }
    foreach ($model in $systemModels) {
        $identity = $model + '|' + $panel + '|' + $signature + '|' + $gpuPrefix
        if ($identities.ContainsKey($identity)) {
            throw "$($file.Name) duplicates an existing hardware identity."
        }
        $identities.Add($identity, $true)
    }

    [pscustomobject]@{
        Id = $id
        DisplayName = Require-Text $profile 'displayName' $file.Name
        SystemModels = $systemModels
        PanelHardwareId = $panel
        NormalizedEdidSignature = $signature
        NativeTiming = $native
        TargetTiming = $target
        VerifiedGpuName = Get-OptionalText $profile 'verifiedGpuName'
        VerifiedGpuDeviceIdPrefix = $gpuPrefix
        VerifiedDriverVersion = Get-OptionalText `
            $profile `
            'verifiedDriverVersion'
    }
})

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('// Generated by tools/Generate-ProfileCatalog.ps1. Do not edit by hand.')
$lines.Add('namespace MacBookEco.Core')
$lines.Add('{')
$lines.Add('    internal static class GeneratedProfileCatalog')
$lines.Add('    {')
$lines.Add('        internal static DisplayProfile[] Create()')
$lines.Add('        {')
$lines.Add('            return new[]')
$lines.Add('            {')
for ($index = 0; $index -lt $profiles.Count; $index++) {
    $profile = $profiles[$index]
    $lines.Add('                new DisplayProfile(')
    Add-StringLine $lines '                    ' $profile.Id ','
    Add-StringLine $lines '                    ' $profile.DisplayName ','
    $models = @($profile.SystemModels | ForEach-Object {
        '"' + (Escape-CSharp $_) + '"'
    }) -join ', '
    $lines.Add('                    new[] { ' + $models + ' },')
    Add-StringLine $lines '                    ' $profile.PanelHardwareId ','
    Add-StringLine $lines '                    ' $profile.NormalizedEdidSignature ','
    $lines.Add('                    DetailedTiming.ParseHex(')
    Add-StringLine $lines '                        ' $profile.NativeTiming '),'
    $lines.Add('                    DetailedTiming.ParseHex(')
    Add-StringLine $lines '                        ' $profile.TargetTiming '),'
    Add-StringLine $lines '                    ' $profile.VerifiedGpuName ','
    Add-StringLine $lines '                    ' $profile.VerifiedGpuDeviceIdPrefix ','
    $suffix = if ($index -eq $profiles.Count - 1) { ')' } else { '),' }
    Add-StringLine $lines '                    ' $profile.VerifiedDriverVersion $suffix
}
$lines.Add('            };')
$lines.Add('        }')
$lines.Add('    }')
$lines.Add('}')
$expected = ($lines -join "`r`n") + "`r`n"

if ($Check) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "Generated catalog is missing: $outputPath"
    }
    $actual = Get-Content -LiteralPath $outputPath -Raw
    if (($actual -replace "`r`n", "`n") -cne
        ($expected -replace "`r`n", "`n")) {
        throw ('ProfileCatalog.Generated.cs is stale. Run ' +
            'tools\Generate-ProfileCatalog.ps1.')
    }

    Write-Host "Profile catalog is current ($($profiles.Count) profile(s))."
    return
}

[IO.File]::WriteAllText(
    $outputPath,
    $expected,
    [Text.UTF8Encoding]::new($false))
Write-Host "Generated $outputPath from $($profiles.Count) profile(s)."
