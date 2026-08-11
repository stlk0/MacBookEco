[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$windowsPowerShell = Join-Path `
    $env:WINDIR `
    'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $windowsPowerShell)) {
    throw 'Windows PowerShell was not found. The offline .NET Framework suites require Windows.'
}

function Invoke-RequiredSuite {
    param(
        [string]$Name,
        [string]$ScriptPath,
        [int[]]$DeferredExitCodes = @()
    )

    if (-not (Test-Path -LiteralPath $ScriptPath)) {
        throw "Required suite '$Name' was not found: $ScriptPath"
    }

    Write-Host ''
    Write-Host "=== Required: $Name ==="
    & $windowsPowerShell `
        -NoLogo `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $ScriptPath
    $suiteExitCode = $LASTEXITCODE
    if ($DeferredExitCodes -contains $suiteExitCode) {
        Write-Host "DEFERRED (not run; not passed): $Name is unavailable on this host."
        return
    }
    if ($suiteExitCode -ne 0) {
        throw "Required suite '$Name' failed with exit code $suiteExitCode."
    }

    Write-Host "PASS (required): $Name"
}

function Invoke-RequiredExecutable {
    param(
        [string]$Name,
        [string]$ExecutablePath,
        [int[]]$DeferredExitCodes = @()
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "Required executable '$Name' was not found: $ExecutablePath"
    }

    Write-Host ''
    Write-Host "=== Required: $Name ==="
    & $ExecutablePath
    $suiteExitCode = $LASTEXITCODE
    if ($DeferredExitCodes -contains $suiteExitCode) {
        Write-Host "DEFERRED (not run; not passed): $Name is unavailable on this host."
        return
    }
    if ($suiteExitCode -ne 0) {
        throw "Required executable '$Name' failed with exit code $suiteExitCode."
    }

    Write-Host "PASS (required): $Name"
}

Push-Location -LiteralPath $projectRoot
try {
    Invoke-RequiredSuite `
        -Name 'SDK build, package integrity, and host-safe behavior' `
        -ScriptPath (Join-Path $projectRoot 'build\build.ps1')
    Invoke-RequiredSuite `
        -Name 'offline display profile authoring behavior' `
        -ScriptPath (Join-Path `
            $projectRoot `
            'tests\ProfileAuthoring\verify.ps1')
    Invoke-RequiredSuite `
        -Name 'native ownership and durable-ordering audit' `
        -ScriptPath (Join-Path $projectRoot 'tests\Security\VerifyProductionBoundary.ps1')
    Invoke-RequiredSuite `
        -Name 'application command-runner boundary audit' `
        -ScriptPath (Join-Path $projectRoot 'tests\Security\VerifyAppOrchestration.ps1')
    Invoke-RequiredSuite `
        -Name 'telemetry identity and read-only native-surface audit' `
        -ScriptPath (Join-Path $projectRoot 'tests\Security\VerifyTelemetryBoundary.ps1')
    Invoke-RequiredSuite `
        -Name 'designated-hardware acceptance harness audit' `
        -ScriptPath (Join-Path $projectRoot 'tests\Hardware.Acceptance\verify-harness.ps1')
    Invoke-RequiredExecutable `
        -Name 'read-only Platform diagnostics' `
        -ExecutablePath (Join-Path `
            $projectRoot `
            'build\out\MacBookEco.PlatformDiagnostics.exe') `
        -DeferredExitCodes @(2)

    # Required, not optional: this suite is what proves the watchdog's compiled
    # surface has not quietly grown. Deleting it used to downgrade the run to a
    # SKIP that still reported overall success.
    Invoke-RequiredSuite `
        -Name 'Architecture baseline' `
        -ScriptPath (Join-Path `
            $projectRoot `
            'tests\Architecture\VerifyBaseline.ps1')

    Write-Host ''
    Write-Host 'DEFERRED (not run; not passed): run tests\Platform.Security\README.md in a disposable NTFS Windows VM with separate Standard User and Administrator tokens.'
    Write-Host 'DEFERRED (not run; not passed): execute the audited power mutation cycle with explicit opt-in on designated supported hardware.'
    Write-Host 'DEFERRED (not run; not passed): elevated EDID mutation and visible display rollback acceptance require a separate explicit hardware phase.'
    Write-Host 'DEFERRED (not run; not passed): live AMD ADL telemetry requires designated supported hardware.'
    Write-Host 'DEFERRED (not run; not passed): hardware acceptance requires a supported MacBook and is outside this host-safe command.'
    Write-Host ''
    Write-Host 'PASS: all required non-destructive suites completed. Deferred suites are not counted as passed.'
}
finally {
    Pop-Location
}
