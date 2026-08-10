[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('PrecreatedLocks', 'ReparseLocks', 'HardLinkedLocks')]
    [string]$Scenario,

    [switch]$DisposableVm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $DisposableVm) {
    throw 'Pass -DisposableVm only in a disposable VM.'
}

$marker = Join-Path $env:SystemDrive '.macbookeco-disposable-vm'
if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "Disposable-VM marker is missing: $marker"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run child-object staging from an Administrator token.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repositoryRoot 'build\TestBuild.ps1')

$testProgram = Build-TestExecutable `
    -RepositoryRoot $repositoryRoot `
    -Manifest 'PlatformSecurityTests' `
    -OutputPath (Get-TestOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Name 'MacBookEco.PlatformSecurity.Tests')

$programData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
$root = Join-Path $programData 'MacBookEco.State'
if (Test-Path -LiteralPath $root) {
    throw "Refusing to reuse existing fixed state root: $root. Revert the VM snapshot before staging another case."
}

& $testProgram create-clean-root
if ($LASTEXITCODE -ne 0) {
    throw "Could not create checked state root. Exit=$LASTEXITCODE"
}

$edidLock = Join-Path $root 'edid.transaction.lock'
$powerLock = Join-Path $root 'power.transaction.lock'
switch ($Scenario) {
    'PrecreatedLocks' {
        Set-Content -LiteralPath $edidLock -Value 'untrusted-edid-lock' -Encoding ASCII
        Set-Content -LiteralPath $powerLock -Value 'untrusted-power-lock' -Encoding ASCII
    }
    'ReparseLocks' {
        $edidTarget = Join-Path $env:TEMP 'MacBookEco.HostileEdidLockTarget'
        $powerTarget = Join-Path $env:TEMP 'MacBookEco.HostilePowerLockTarget'
        if ((Test-Path -LiteralPath $edidTarget) -or (Test-Path -LiteralPath $powerTarget)) {
            throw 'Refusing to reuse hostile reparse targets.'
        }

        New-Item -ItemType Directory -Path $edidTarget | Out-Null
        New-Item -ItemType Directory -Path $powerTarget | Out-Null
        & cmd.exe /d /c "mklink /J `"$edidLock`" `"$edidTarget`""
        if ($LASTEXITCODE -ne 0) { throw 'Could not create EDID lock junction.' }
        & cmd.exe /d /c "mklink /J `"$powerLock`" `"$powerTarget`""
        if ($LASTEXITCODE -ne 0) { throw 'Could not create power lock junction.' }
    }
    'HardLinkedLocks' {
        & $testProgram create-edid-lock
        if ($LASTEXITCODE -ne 0) { throw 'Could not create checked EDID lock.' }
        & cmd.exe /d /c "mklink /H `"$powerLock`" `"$edidLock`""
        if ($LASTEXITCODE -ne 0) { throw 'Could not create power lock hardlink.' }
    }
}

Write-Host "STAGED: $Scenario below $root"
