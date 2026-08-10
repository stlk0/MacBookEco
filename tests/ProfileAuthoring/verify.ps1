[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$proposalTool = Join-Path $repoRoot 'tools\New-DisplayProfileProposal.ps1'
$catalogTool = Join-Path $repoRoot 'tools\Generate-ProfileCatalog.ps1'
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    ('MacBookEco-profile-authoring-' + [Guid]::NewGuid().ToString('N'))

function Set-Checksum {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    $Bytes[$Offset + 127] = 0
    $sum = 0
    for ($index = 0; $index -lt 127; $index++) {
        $sum = ($sum + $Bytes[$Offset + $index]) -band 0xFF
    }
    $Bytes[$Offset + 127] = [byte]((256 - $sum) -band 0xFF)
}

function New-SyntheticEdid {
    $bytes = New-Object byte[] 256
    [byte[]]$header = @(0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00)
    [Array]::Copy($header, 0, $bytes, 0, $header.Length)
    # APP manufacturer code and A044 product code.
    $bytes[8] = 0x06
    $bytes[9] = 0x10
    $bytes[10] = 0x44
    $bytes[11] = 0xA0
    $bytes[18] = 1
    $bytes[19] = 4
    $bytes[24] = 0x02
    $bytes[126] = 1

    $nativeHex =
        'E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A'
    [byte[]]$native = @($nativeHex.Split(' ') | ForEach-Object {
        [Convert]::ToByte($_, 16)
    })
    [Array]::Copy($native, 0, $bytes, 54, $native.Length)
    Set-Checksum $bytes 0

    # Minimal checksum-correct CTA extension. The proposal tool only needs to
    # prove that the complete document is present and intact.
    $bytes[128] = 0x02
    $bytes[129] = 0x03
    $bytes[130] = 0x04
    Set-Checksum $bytes 128
    return $bytes
}

function Invoke-Proposal {
    param([string]$Path)

    $json = & $proposalTool `
        -EdidPath $Path `
        -SystemModel 'MacBookPro16,1' `
        -GpuDeviceIdPrefix 'PCI\VEN_1002&DEV_7340' `
        -GpuName 'AMD Radeon Pro 5300M' `
        -DriverVersion '30.0.13045.22003'
    return ($json | Out-String | ConvertFrom-Json)
}

New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $edidPath = Join-Path $temporaryRoot 'synthetic.edid'
    $bytes = New-SyntheticEdid
    [IO.File]::WriteAllBytes($edidPath, $bytes)

    $proposal = Invoke-Proposal $edidPath
    if ($proposal.panelHardwareId -cne 'APPA044') {
        throw 'The proposal did not preserve the panel identity.'
    }
    if ($proposal.nativeTiming -cne
        'E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A') {
        throw 'The proposal did not preserve the native timing.'
    }
    if ($proposal.targetTiming -cne
        'DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A') {
        throw 'The proposal did not calculate the reviewed 48 Hz timing.'
    }

    $bytes[20] = $bytes[20] -bxor 0x01
    [IO.File]::WriteAllBytes($edidPath, $bytes)
    $rejected = $false
    try {
        Invoke-Proposal $edidPath | Out-Null
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw 'The proposal tool accepted a checksum-corrupt EDID.'
    }

    & $catalogTool -Check

    Write-Host 'Profile authoring behavior passed (3/3).'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
