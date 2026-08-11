[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$path = Join-Path $PSScriptRoot 'run-power-cycle.ps1'
if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw 'Hardware acceptance power-cycle harness is missing.'
}

$parseErrors = $null
$tokens = $null
[void][Management.Automation.Language.Parser]::ParseFile(
    $path,
    [ref]$tokens,
    [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw "Hardware acceptance harness has parser errors: $($parseErrors[0])"
}

$source = [IO.File]::ReadAllText($path)
foreach ($required in @(
        '[switch]$DesignatedHardware',
        'WindowsBuiltInRole]::Administrator',
        'MacBookPro16,1',
        'APPA044',
        "@('apply-power',",
        "@('restore-power')",
        'Get-FileHash -Algorithm SHA256',
        'final.Guid -eq $initial.Guid',
        'result=PASS'
    )) {
    if (-not $source.Contains($required)) {
        throw "Hardware acceptance harness is missing '$required'."
    }
}

foreach ($forbidden in @(
        'install-display',
        'remove-display',
        '/setactive',
        'PowerSetActiveScheme',
        'Remove-Item',
        'Stop-Process'
    )) {
    if ($source.IndexOf(
            $forbidden,
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Power-cycle harness contains forbidden token '$forbidden'."
    }
}

Write-Host 'Hardware acceptance harness audit passed.'
