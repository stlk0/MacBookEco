[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repositoryRoot 'build\TestBuild.ps1')

Build-TestExecutable `
    -RepositoryRoot $repositoryRoot `
    -Manifest 'PlatformSecurityTests' `
    -OutputPath (Get-TestOutputPath `
        -RepositoryRoot $repositoryRoot `
        -Name 'MacBookEco.PlatformSecurity.Tests') | Out-Null

Write-Host 'Platform.Security VM harness compiled; no privileged test was run.'
