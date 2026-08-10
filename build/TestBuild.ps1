# SDK build entry point for opt-in diagnostics and disposable-VM harnesses.

Set-StrictMode -Version Latest

$script:TestProjects = @{
    PlatformSecurityTests = @{
        Project = "projects\MacBookEco.PlatformSecurityTests.csproj"
        Output = "build\out\tests\MacBookEco.PlatformSecurity.Tests.exe"
    }
    PlatformDiagnostics = @{
        Project = "projects\MacBookEco.PlatformDiagnostics.csproj"
        Output = "build\out\MacBookEco.PlatformDiagnostics.exe"
    }
}

function Get-TestOutputPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $RepositoryRoot,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    return Join-Path $RepositoryRoot ("build\out\tests\" + $Name + ".exe")
}

function Build-TestExecutable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $RepositoryRoot,
        [Parameter(Mandatory = $true)] [string] $Manifest,
        [Parameter(Mandatory = $true)] [string] $OutputPath
    )

    if (-not $script:TestProjects.ContainsKey($Manifest)) {
        throw "Unknown SDK test project: $Manifest"
    }

    if ($env:OS -ne "Windows_NT") {
        throw "MacBook Eco test executables require Windows."
    }

    $rootPath = [IO.Path]::GetFullPath($RepositoryRoot)
    $shape = $script:TestProjects[$Manifest]
    $projectPath = Join-Path $rootPath $shape.Project
    $expectedOutputPath = [IO.Path]::GetFullPath(
        (Join-Path $rootPath $shape.Output))
    $requestedOutputPath = [IO.Path]::GetFullPath($OutputPath)
    if (-not $requestedOutputPath.Equals(
            $expectedOutputPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Manifest output must be $expectedOutputPath."
    }

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "SDK test project was not found: $projectPath"
    }

    $dotnet = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    }
    if ($null -eq $dotnet) {
        throw "The .NET SDK selected by global.json was not found."
    }

    Push-Location -LiteralPath $rootPath
    try {
        & $dotnet.Source build `
            $projectPath `
            --configuration Release `
            --no-incremental `
            --nologo `
            --verbosity minimal `
            -p:Platform=x64 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "$Manifest build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $expectedOutputPath -PathType Leaf)) {
        throw "$Manifest build did not produce $expectedOutputPath."
    }

    return $expectedOutputPath
}
