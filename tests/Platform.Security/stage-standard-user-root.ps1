[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('PrecreatedRoot', 'ReparseRoot')]
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
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run staging from the separate Standard User token, not Administrator.'
}

$programData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::CommonApplicationData)
$root = Join-Path $programData 'MacBookEco.State'
if (Test-Path -LiteralPath $root) {
    throw "Refusing to overwrite existing fixed state root: $root. Revert the VM snapshot before staging another case."
}

switch ($Scenario) {
    'PrecreatedRoot' {
        New-Item -ItemType Directory -Path $root -ErrorAction Stop | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'edid.journal') -Value 'untrusted' -Encoding ASCII
        Write-Host "STAGED: Standard User pre-created $root"
    }
    'ReparseRoot' {
        $target = Join-Path $env:LOCALAPPDATA 'MacBookEco.HostileStateTarget'
        if (Test-Path -LiteralPath $target) {
            throw "Refusing to reuse hostile junction target: $target"
        }

        New-Item -ItemType Directory -Path $target -ErrorAction Stop | Out-Null
        & cmd.exe /d /c "mklink /J `"$root`" `"$target`""
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $root)) {
            throw 'Could not create the hostile root junction.'
        }

        Write-Host "STAGED: Standard User created junction $root -> $target"
    }
}
