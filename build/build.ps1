[CmdletBinding()]
param(
    [switch]$CompileOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repoRoot "MacBookEco.sln"
$outputDirectory = Join-Path $PSScriptRoot "out"
$versionPath = Join-Path $repoRoot "VERSION"
$globalJsonPath = Join-Path $repoRoot "global.json"
$profileCatalogCheck = Join-Path `
    $repoRoot `
    "tools\Generate-ProfileCatalog.ps1"

if ($env:OS -ne "Windows_NT") {
    throw "MacBook Eco builds require Windows and .NET Framework 4.8."
}

foreach ($requiredPath in @(
        $solutionPath,
        $versionPath,
        $globalJsonPath,
        $profileCatalogCheck
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required build input was not found: $requiredPath"
    }
}

& $profileCatalogCheck -Check

$dotnet = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
}
if ($null -eq $dotnet) {
    throw "The .NET SDK was not found. Install the version selected by global.json."
}

$sdkSettings = Get-Content -LiteralPath $globalJsonPath -Raw |
    ConvertFrom-Json
$expectedSdkVersion = [string]$sdkSettings.sdk.version
if ([string]::IsNullOrWhiteSpace($expectedSdkVersion)) {
    throw "global.json does not select a .NET SDK version."
}

Push-Location -LiteralPath $repoRoot
try {
    $actualSdkVersion = [string](& $dotnet.Source --version)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --version failed with exit code $LASTEXITCODE."
    }
    $actualSdkVersion = $actualSdkVersion.Trim()
}
finally {
    Pop-Location
}

if ($actualSdkVersion -ne $expectedSdkVersion) {
    throw (
        "global.json requires .NET SDK $expectedSdkVersion, but dotnet selected " +
        "$actualSdkVersion.")
}

$informationalVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()
if ($informationalVersion -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-[0-9A-Za-z.-]+)?$') {
    throw "VERSION must use semantic version syntax."
}

$numericVersion = $Matches[1] `
    + "." `
    + $Matches[2] `
    + "." `
    + $Matches[3] `
    + ".0"
$expectedAssemblyVersion =
    '[assembly: AssemblyVersion("' + $numericVersion + '")]'
$expectedFileVersion =
    '[assembly: AssemblyFileVersion("' + $numericVersion + '")]'
$expectedInformationalVersion =
    '[assembly: AssemblyInformationalVersion("' `
    + $informationalVersion `
    + '")]'

foreach ($assemblyInfoPath in @(
        (Join-Path $repoRoot "src\App\AssemblyInfo.cs"),
        (Join-Path $repoRoot "src\Admin\AssemblyInfo.cs"),
        (Join-Path $repoRoot "src\Watchdog\AssemblyInfo.cs")
    )) {
    $assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw
    if (-not $assemblyInfo.Contains($expectedAssemblyVersion) `
        -or -not $assemblyInfo.Contains($expectedFileVersion) `
        -or -not $assemblyInfo.Contains($expectedInformationalVersion)) {
        throw "Assembly version metadata does not match VERSION: $assemblyInfoPath"
    }
}

foreach ($manifestPath in @(
        (Join-Path $repoRoot "src\App\app.manifest"),
        (Join-Path $repoRoot "src\Admin\app.manifest"),
        (Join-Path $repoRoot "src\Watchdog\app.manifest")
    )) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    if (-not $manifest.Contains('version="' + $numericVersion + '"')) {
        throw "Manifest identity does not match VERSION: $manifestPath"
    }
}

Write-Host "SDK: $actualSdkVersion"
Write-Host "Target: .NET Framework 4.8, C# 7.3, x64"

Push-Location -LiteralPath $repoRoot
try {
    Write-Host "Restoring SDK build inputs..."
    & $dotnet.Source restore $solutionPath --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Host "Building solution..."
    & $dotnet.Source build `
        $solutionPath `
        --configuration Release `
        --no-restore `
        --no-incremental `
        --nologo `
        --verbosity minimal `
        -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$applicationOutput = Join-Path $outputDirectory "MacBookEco.exe"
$adminOutput = Join-Path $outputDirectory "MacBookEco.Admin.exe"
$watchdogOutput = Join-Path $outputDirectory "MacBookEco.Watchdog.exe"
$packagingTestOutput =
    Join-Path $outputDirectory "MacBookEco.PackagingTests.exe"
$watchdogTestOutput =
    Join-Path $outputDirectory "MacBookEco.WatchdogTests.exe"
$appTestOutput = Join-Path $outputDirectory "MacBookEco.AppTests.exe"

foreach ($requiredOutput in @(
        $applicationOutput,
        $adminOutput,
        $watchdogOutput,
        $packagingTestOutput,
        $watchdogTestOutput,
        $appTestOutput
    )) {
    if (-not (Test-Path -LiteralPath $requiredOutput -PathType Leaf)) {
        throw "The SDK build did not produce: $requiredOutput"
    }
}

if ($CompileOnly) {
    Write-Host "CompileOnly: packaged binaries were not loaded or executed."
}
else {
    Write-Host "Checking embedded helper integrity..."
    & $packagingTestOutput `
        $applicationOutput `
        $adminOutput `
        $watchdogOutput `
        $informationalVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Packaging integrity test failed with exit code $LASTEXITCODE."
    }

    Write-Host "Running non-mutating watchdog protocol test..."
    & $watchdogTestOutput $watchdogOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Watchdog protocol test failed with exit code $LASTEXITCODE."
    }

    Write-Host "Running host-safe behavior tests..."
    & $appTestOutput
    if ($LASTEXITCODE -ne 0) {
        throw "Host-safe behavior tests failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Build complete: $outputDirectory"
