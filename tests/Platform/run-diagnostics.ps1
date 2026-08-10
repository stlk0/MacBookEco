[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $projectRoot "build\TestBuild.ps1")

# Kept beside the product binaries rather than under build\out\tests: the
# hardware-acceptance harness runs this exact path.
$outputPath = Build-TestExecutable `
    -RepositoryRoot $projectRoot `
    -Manifest "PlatformDiagnostics" `
    -OutputPath (Join-Path $projectRoot "build\out\MacBookEco.PlatformDiagnostics.exe")

& $outputPath
$diagnosticsExitCode = $LASTEXITCODE
if ($diagnosticsExitCode -eq 2) {
    exit 2
}
if ($diagnosticsExitCode -ne 0) {
    throw "Platform diagnostics failed with exit code $diagnosticsExitCode."
}
