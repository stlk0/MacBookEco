[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('PrecreatedRoot', 'ReparseRoot', 'HostileLocks')]
    [string]$Scenario,

    [switch]$DisposableVm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require-DisposableVm {
    if (-not $DisposableVm) {
        throw 'This suite is destructive to the fixed ProgramData test root. Pass -DisposableVm only in a disposable VM.'
    }

    $marker = Join-Path $env:SystemDrive '.macbookeco-disposable-vm'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "Disposable-VM marker is missing: $marker"
    }
}

function Require-ElevatedAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the elevated half from an Administrator token.'
    }
}

function Get-ActivePowerSchemeGuid {
    $lines = @(& powercfg.exe /getactivescheme)
    if ($LASTEXITCODE -ne 0) {
        throw "powercfg /getactivescheme failed with exit code $LASTEXITCODE."
    }

    $text = [string]::Join([Environment]::NewLine, [string[]]$lines)
    $match = [regex]::Match(
        $text,
        '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}')
    if (-not $match.Success) {
        throw "Could not parse active power scheme GUID: $text"
    }

    return $match.Value.ToUpperInvariant()
}

function Invoke-ExpectedHelperConflict {
    param(
        [Parameter(Mandatory = $true)][string]$HelperPath,
        [Parameter(Mandatory = $true)][string[]]$HelperArguments,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $output = @(& $HelperPath @HelperArguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = [string]::Join([Environment]::NewLine, [string[]]$output)
    if ($exitCode -eq 0 -or $text -notmatch 'SecureStateConflictException') {
        throw "$Label did not stop at the hostile state. Exit=$exitCode Output=$text"
    }

    Write-Host "PASS: $Label was rejected before mutation."
}

Require-DisposableVm
Require-ElevatedAdministrator

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repositoryRoot 'build\TestBuild.ps1')

# Rebuilt here rather than trusted from an earlier step: this program is about
# to run with an Administrator token.
$testProgram = Build-TestExecutable `
    -RepositoryRoot $repositoryRoot `
    -Manifest 'PlatformSecurityTests' `
    -OutputPath (Get-TestOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Name 'MacBookEco.PlatformSecurity.Tests')

switch ($Scenario) {
    'PrecreatedRoot' {
        & $testProgram expect-root-conflict
    }
    'ReparseRoot' {
        & $testProgram expect-root-conflict
    }
    'HostileLocks' {
        & $testProgram expect-edid-lock-conflict
        if ($LASTEXITCODE -eq 0) {
            & $testProgram expect-power-lock-conflict
        }
    }
}
if ($LASTEXITCODE -ne 0) {
    throw "SecureStateStore hostile-state assertion failed with exit code $LASTEXITCODE."
}

$adminHelper = Join-Path $repositoryRoot 'build\out\MacBookEco.Admin.exe'
if (-not (Test-Path -LiteralPath $adminHelper -PathType Leaf)) {
    throw "Build the helper first: $adminHelper"
}

$beforePower = Get-ActivePowerSchemeGuid
Invoke-ExpectedHelperConflict -HelperPath $adminHelper -HelperArguments @('apply-power', 'normal') -Label 'Power helper'
$afterPower = Get-ActivePowerSchemeGuid
if ($beforePower -ne $afterPower) {
    throw "Hostile state changed the active power scheme: before=$beforePower after=$afterPower"
}

Invoke-ExpectedHelperConflict -HelperPath $adminHelper -HelperArguments @('install-display') -Label 'EDID helper'
Write-Host 'PASS: hostile state blocked both production helpers; active power scheme was unchanged.'
