[CmdletBinding()]
param(
    [switch]$OfficialRelease,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionPath = Join-Path $repoRoot "VERSION"
$outputDirectory = Join-Path $PSScriptRoot "out"
$releaseDirectory = Join-Path $PSScriptRoot "release"

function Require-CleanReleaseTree {
    $changes = @(& git -C $repoRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed; packaging requires a Git working tree."
    }

    if ($changes.Count -ne 0) {
        throw "Packaging requires a clean non-ignored working tree. Commit, stash, or remove: $($changes -join '; ')"
    }
}

function Get-InnoSetupCompiler {
    param([string]$RequestedPath)

    $candidate = $RequestedPath
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        foreach ($path in @(
                (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
                (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
                (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
            )) {
            if (Test-Path -LiteralPath $path) {
                $candidate = $path
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            $candidate = $command.Source
        }
    }

    if ([string]::IsNullOrWhiteSpace($candidate) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Inno Setup 6 compiler was not found. Install it or pass -IsccPath."
    }

    Write-Host "Inno Setup compiler: $candidate"
    return $candidate
}

if (-not (Test-Path -LiteralPath $versionPath)) {
    throw "VERSION was not found."
}

$version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
if (-not ($version -match `
        '^(?<Core>[0-9]+\.[0-9]+\.[0-9]+)(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')) {
    throw "VERSION must be a three-part semantic version with an optional prerelease."
}
$binaryVersion = $Matches.Core + ".0"

Require-CleanReleaseTree
if ($OfficialRelease) {
    $expectedTag = "v" + $version
    $actualTag = (& git -C $repoRoot describe --exact-match --tags HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualTag -ne $expectedTag) {
        throw "Official release HEAD must be tagged $expectedTag."
    }
}

& (Join-Path $PSScriptRoot "build.ps1")
if ($LASTEXITCODE -ne 0) {
    throw "The release build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $releaseDirectory)) {
    New-Item -ItemType Directory -Path $releaseDirectory | Out-Null
}

$packageName = "MacBookEco-$version-win-x64-payload"
$stagingDirectory = Join-Path $releaseDirectory $packageName
$installerName = "MacBookEco-$version-win-x64-setup.exe"
$installerPath = Join-Path $releaseDirectory $installerName
$installerHashPath = $installerPath + ".sha256"
$sourcePackageName = "MacBookEco-$version-source"
$sourceArchivePath = Join-Path $releaseDirectory ($sourcePackageName + ".zip")
$sourceArchiveHashPath = $sourceArchivePath + ".sha256"

$resolvedReleaseDirectory = [IO.Path]::GetFullPath($releaseDirectory)
$releasePrefix = $resolvedReleaseDirectory.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

foreach ($target in @(
        $stagingDirectory,
        $installerPath,
        $installerHashPath,
        $sourceArchivePath,
        $sourceArchiveHashPath
    )) {
    $resolvedTarget = [IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith(
            $releasePrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside build\\release: $resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
$requiredBinaries = @(
    "MacBookEco.exe",
    "MacBookEco.Admin.exe",
    "MacBookEco.Watchdog.exe"
)
$requiredPayloadFiles = @($requiredBinaries) + "MacBookEco.exe.config"
foreach ($payloadFile in $requiredPayloadFiles) {
    $source = Join-Path $outputDirectory $payloadFile
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required release payload was not found: $source"
    }

    Copy-Item -LiteralPath $source -Destination $stagingDirectory
}

$integrityVerifier = Join-Path $outputDirectory "MacBookEco.PackagingTests.exe"
if (-not (Test-Path -LiteralPath $integrityVerifier)) {
    throw "The compiled packaging integrity verifier was not found."
}

Write-Host "Verifying companion resources in the staged payload..."
& $integrityVerifier `
    (Join-Path $stagingDirectory "MacBookEco.exe") `
    (Join-Path $stagingDirectory "MacBookEco.Admin.exe") `
    (Join-Path $stagingDirectory "MacBookEco.Watchdog.exe") `
    $version
if ($LASTEXITCODE -ne 0) {
    throw "Staged companion integrity verification failed with exit code $LASTEXITCODE."
}

foreach ($document in @(
        "README.md",
        "LICENSE",
        "SUPPORTED_HARDWARE.md",
        "THIRD_PARTY_NOTICES.md"
    )) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $stagingDirectory
}

$stagingDocs = Join-Path $stagingDirectory "docs"
New-Item -ItemType Directory -Path $stagingDocs | Out-Null
foreach ($document in @(
        "CODE_SIGNING.md",
        "RECOVERY.md"
    )) {
    Copy-Item `
        -LiteralPath (Join-Path (Join-Path $repoRoot "docs") $document) `
        -Destination $stagingDocs
}

$hashLines = New-Object System.Collections.Generic.List[string]
foreach ($payloadFile in $requiredPayloadFiles) {
    $hash = Get-FileHash `
        -LiteralPath (Join-Path $stagingDirectory $payloadFile) `
        -Algorithm SHA256
    $hashLines.Add($hash.Hash.ToLowerInvariant() + "  " + $payloadFile)
}
Set-Content `
    -LiteralPath (Join-Path $stagingDirectory "SHA256SUMS.txt") `
    -Value $hashLines `
    -Encoding ASCII

$iscc = Get-InnoSetupCompiler -RequestedPath $IsccPath
Write-Host "Compiling per-user installer..."
& $iscc `
    "/DAppVersion=$version" `
    "/DBinaryVersion=$binaryVersion" `
    "/DPayloadDir=$stagingDirectory" `
    "/DOutputDir=$releaseDirectory" `
    (Join-Path $PSScriptRoot "installer.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "The expected installer was not produced: $installerPath"
}

$installerHash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Set-Content `
    -LiteralPath $installerHashPath `
    -Value ($installerHash.Hash.ToLowerInvariant() + "  " + $installerName) `
    -Encoding ASCII

# The verified payload is installer input, not a second distribution format.
Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

Write-Host "Archiving tracked source files from HEAD (git $((& git --version).Trim()))."
& git -C $repoRoot archive `
    --format=zip `
    ("--prefix=" + $sourcePackageName + "/") `
    ("--output=" + $sourceArchivePath) `
    HEAD
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $sourceArchivePath)) {
    throw "git archive did not create the expected source package."
}

$sourceArchiveHash = Get-FileHash -LiteralPath $sourceArchivePath -Algorithm SHA256
Set-Content `
    -LiteralPath $sourceArchiveHashPath `
    -Value ($sourceArchiveHash.Hash.ToLowerInvariant() + "  " + (Split-Path -Leaf $sourceArchivePath)) `
    -Encoding ASCII

Write-Host "Installer: $installerPath"
Write-Host "SHA-256: $($installerHash.Hash)"
Write-Host "Source package: $sourceArchivePath"
Write-Host "SHA-256: $($sourceArchiveHash.Hash)"
