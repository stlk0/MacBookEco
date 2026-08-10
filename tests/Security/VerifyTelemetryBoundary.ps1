[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Read-Source {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    return [IO.File]::ReadAllText((Join-Path $repositoryRoot $RelativePath))
}

function Assert-That {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "VerifyTelemetryBoundary: $Message"
    }
}

$display = Read-Source 'src\Telemetry\DisplayTelemetryProvider.cs'
$gpu = Read-Source 'src\Telemetry\WindowsGpuTelemetryProvider.cs'

Assert-That -Condition $display.Contains('InternalDisplayTargetResolver') -Message (
    'display telemetry must use the reviewed internal-panel resolver.')
Assert-That -Condition (
    $display.Contains('new EdidBaseBlock(') -and
    $display.Contains('NormalizedSignature')) -Message (
    'display telemetry must retain normalized panel signature evidence.')
Assert-That -Condition (
    -not $display.Contains('target.MonitorIdentity.EdidFingerprint')) -Message (
    'display telemetry must not export the exact base-EDID fingerprint.')
Assert-That -Condition (-not $display.Contains('EnumDisplayDevices')) -Message (
    'display telemetry must not fall back to the Windows primary monitor.')

# Read-only telemetry is a native-surface contract. An exact import allowlist
# fails the build if an ADL mutation entry point is added, while lifecycle and
# value classification remain compiled behavior tests.
$expectedAdlImports = @(
    'ADL2_Main_Control_Create',
    'ADL2_Main_Control_Destroy',
    'ADL2_Adapter_NumberOfAdapters_Get',
    'ADL2_Adapter_AdapterInfo_Get',
    'ADL2_Overdrive_Caps',
    'ADL2_OverdriveN_PerformanceStatus_Get',
    'ADL2_OverdriveN_Temperature_Get',
    'ADL2_Overdrive6_CurrentStatus_Get',
    'ADL2_Overdrive6_Temperature_Get',
    'ADL2_Overdrive5_CurrentActivity_Get',
    'ADL2_Overdrive5_Temperature_Get',
    'ADL2_New_QueryPMLogData_Get',
    'ADL2_Overdrive6_CurrentPower_Get'
)
$actualAdlImports = @(
    [regex]::Matches(
        $gpu,
        'internal static extern int (ADL2_[A-Za-z0-9_]+)\s*\(') |
        ForEach-Object { $_.Groups[1].Value }
)
Assert-That -Condition ($actualAdlImports.Count -eq $expectedAdlImports.Count) `
    -Message 'GPU telemetry ADL import surface changed.'
foreach ($expected in $expectedAdlImports) {
    Assert-That -Condition ($actualAdlImports -contains $expected) -Message (
        "GPU telemetry is missing reviewed read-only ADL import '$expected'.")
}

Write-Host 'VerifyTelemetryBoundary passed.'
