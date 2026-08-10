[CmdletBinding()]
param(
    [switch]$DesignatedHardware
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Hardware acceptance: $Message"
    }
}

function Get-ActivePowerScheme {
    $text = (powercfg /getactivescheme | Out-String).Trim()
    $match = [regex]::Match(
        $text,
        '(?i)([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')
    Assert-Condition $match.Success 'powercfg did not return an active scheme GUID.'
    return [pscustomobject]@{
        Guid = [Guid]$match.Groups[1].Value
        Text = $text
    }
}

function Invoke-FixedHelperCommand {
    param(
        [string]$HelperPath,
        [string[]]$Arguments,
        [string]$ExpectedName,
        [string]$EvidencePath,
        [string]$Label
    )

    $output = @(& $HelperPath @Arguments 2>&1 | ForEach-Object {
        $_.ToString()
    })
    $exitCode = $LASTEXITCODE
    $active = Get-ActivePowerScheme

    @(
        "label=$Label"
        "exitCode=$exitCode"
        "active=$($active.Text)"
        'stdout='
        $output
    ) | Set-Content -LiteralPath $EvidencePath -Encoding UTF8

    Assert-Condition ($exitCode -eq 0) (
        "$Label returned exit code $exitCode. Evidence: $EvidencePath")
    if (-not [string]::IsNullOrWhiteSpace($ExpectedName)) {
        Assert-Condition ($active.Text -like "*$ExpectedName*") (
            "$Label did not activate the expected app-owned scheme.")
    }

    return $active
}

Assert-Condition $DesignatedHardware (
    'pass -DesignatedHardware explicitly; this suite changes Windows power plans.')

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
Assert-Condition (
    $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) (
    'run this script from an elevated Windows PowerShell session.')

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$outputDirectory = Join-Path $repositoryRoot 'build\out'
$version = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'VERSION') -Raw).Trim()
$application = Join-Path $outputDirectory 'MacBookEco.exe'
$helper = Join-Path $outputDirectory 'MacBookEco.Admin.exe'
$watchdog = Join-Path $outputDirectory 'MacBookEco.Watchdog.exe'
$diagnostics = Join-Path $outputDirectory 'MacBookEco.PlatformDiagnostics.exe'
$packagingTests = Join-Path $outputDirectory 'MacBookEco.PackagingTests.exe'
foreach ($requiredFile in @(
        $application,
        $helper,
        $watchdog,
        $diagnostics,
        $packagingTests
    )) {
    Assert-Condition (
        Test-Path -LiteralPath $requiredFile -PathType Leaf) (
        "required prebuilt file is missing: $requiredFile")
}

Assert-Condition (-not (Get-Process -Name 'MacBookEco' -ErrorAction SilentlyContinue)) (
    'close MacBookEco before running the exclusive power-cycle acceptance suite.')

$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssZ')
$evidenceRoot = Join-Path $repositoryRoot "build\acceptance\power-$timestamp"
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$transcriptPath = Join-Path $evidenceRoot 'transcript.txt'
Start-Transcript -LiteralPath $transcriptPath -Force | Out-Null
trap {
    $failureText = $_ | Out-String
    try {
        [IO.File]::WriteAllText(
            (Join-Path $evidenceRoot 'failure.txt'),
            $failureText,
            (New-Object Text.UTF8Encoding($false)))
    }
    catch {
    }

    try {
        Stop-Transcript | Out-Null
    }
    catch {
    }

    Write-Error $failureText
    exit 1
}
Write-Host "Evidence directory: $evidenceRoot"

$packageOutput = @(& $packagingTests `
    $application `
    $helper `
    $watchdog `
    $version 2>&1 | ForEach-Object {
    $_.ToString()
})
Assert-Condition ($LASTEXITCODE -eq 0) 'embedded helper integrity failed.'
$packageOutput | Set-Content `
    -LiteralPath (Join-Path $evidenceRoot 'package-integrity.txt') `
    -Encoding UTF8

$diagnosticOutput = @(& $diagnostics 2>&1 | ForEach-Object {
    $_.ToString()
})
Assert-Condition ($LASTEXITCODE -eq 0) 'platform diagnostics failed.'
$diagnosticText = $diagnosticOutput -join [Environment]::NewLine
$diagnosticOutput | Set-Content `
    -LiteralPath (Join-Path $evidenceRoot 'platform-diagnostics.txt') `
    -Encoding UTF8
Assert-Condition (
    $diagnosticText -like '*Apple model: MacBookPro16,1*') (
    'the designated machine is not the reviewed MacBookPro16,1.')
Assert-Condition ($diagnosticText -like '*Panel: APPA044*') (
    'the designated machine does not expose the reviewed APPA044 panel.')
Assert-Condition ($diagnosticText -like '*Platform diagnostics: PASS*') (
    'the read-only hardware gate did not pass.')

$initial = Get-ActivePowerScheme
$initialPreset = $null
if ($initial.Text -like '*MacBook Eco (Everyday,*') {
    $initialPreset = 'normal'
}
elseif ($initial.Text -like '*MacBook Eco (Cool & quiet,*') {
    $initialPreset = 'cool'
}
elseif ($initial.Text -like '*MacBook Eco (Battery saver,*') {
    $initialPreset = 'battery'
}

@(
    "startedUtc=$([DateTime]::UtcNow.ToString('o'))"
    "windowsBuild=$([Environment]::OSVersion.Version)"
    "initialActive=$($initial.Text)"
    "helperSha256=$((Get-FileHash -Algorithm SHA256 -LiteralPath $helper).Hash)"
) | Set-Content -LiteralPath (Join-Path $evidenceRoot 'manifest.txt') -Encoding UTF8

$sequence = @(
    @{ Argument = 'normal'; Expected = 'MacBook Eco (Everyday,'; Label = 'Everyday' }
    @{ Argument = 'cool'; Expected = 'MacBook Eco (Cool & quiet,'; Label = 'Cool-and-quiet' }
    @{ Argument = 'battery'; Expected = 'MacBook Eco (Battery saver,'; Label = 'Battery-saver' }
)

foreach ($entry in $sequence) {
    Invoke-FixedHelperCommand `
        -HelperPath $helper `
        -Arguments @('apply-power', $entry.Argument) `
        -ExpectedName $entry.Expected `
        -EvidencePath (Join-Path $evidenceRoot "$($entry.Label).txt") `
        -Label $entry.Label | Out-Null
}

if ($null -ne $initialPreset) {
    Invoke-FixedHelperCommand `
        -HelperPath $helper `
        -Arguments @('apply-power', $initialPreset) `
        -ExpectedName $(switch ($initialPreset) {
            'normal' { 'MacBook Eco (Everyday,' }
            'cool' { 'MacBook Eco (Cool & quiet,' }
            'battery' { 'MacBook Eco (Battery saver,' }
        }) `
        -EvidencePath (Join-Path $evidenceRoot 'restore-initial-preset.txt') `
        -Label 'Restore-initial-preset' | Out-Null
}
else {
    $restored = Invoke-FixedHelperCommand `
        -HelperPath $helper `
        -Arguments @('restore-power') `
        -ExpectedName '' `
        -EvidencePath (Join-Path $evidenceRoot 'restore-original.txt') `
        -Label 'Restore-original'
    Assert-Condition ($restored.Guid -eq $initial.Guid) (
        'restore-power did not return to the exact initially active GUID.')
}

$final = Get-ActivePowerScheme
Assert-Condition ($final.Guid -eq $initial.Guid) (
    'the acceptance cycle did not preserve the initially active scheme GUID.')
Add-Content -LiteralPath (Join-Path $evidenceRoot 'manifest.txt') -Value @(
    "completedUtc=$([DateTime]::UtcNow.ToString('o'))"
    "finalActive=$($final.Text)"
    'result=PASS'
)

Stop-Transcript | Out-Null
Write-Host "Hardware power acceptance PASS"
Write-Host "Evidence: $evidenceRoot"
