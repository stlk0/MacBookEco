[CmdletBinding()]
param()

# Audits the architectural boundaries that make the privileged design hold.
#
# Scope rule for anything added here: this file checks *dependencies*, never
# *shape*. "The Watchdog source set cannot reach a registry write" is a safety
# property worth failing the build over. "DashboardForm declares a field named
# _batteryCard" is not: it freezes an implementation detail, breaks on every
# rename, and tells a contributor nothing about what they got wrong.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$directoryBuildPropsPath = Join-Path $repositoryRoot "Directory.Build.props"
$sourceProjects = @{
    App = "projects\MacBookEco.csproj"
    Admin = "projects\MacBookEco.Admin.csproj"
    Watchdog = "projects\MacBookEco.Watchdog.csproj"
}

function Assert-That {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "VerifyBaseline: $Message"
    }
}

function Get-ManifestEntries {
    param(
        [Parameter(Mandatory = $true)][string]$Name
    )

    Assert-That -Condition $sourceProjects.ContainsKey($Name) -Message (
        "unknown production source project '$Name'.")
    $projectPath = Join-Path $repositoryRoot $sourceProjects[$Name]
    Assert-That -Condition (Test-Path -LiteralPath $projectPath -PathType Leaf) -Message (
        "missing SDK project '$projectPath'.")

    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $entries = @(
        $project.SelectNodes('/Project/ItemGroup/Compile') |
            ForEach-Object {
                ([string]$_.Include).Replace("..\", "").Replace("\", "/")
            })
    Assert-That -Condition ($entries.Count -gt 0) -Message (
        "SDK project '$Name' must have an explicit source allowlist.")

    foreach ($entry in $entries) {
        $entryText = [string]$entry
        Assert-That -Condition ($entryText -match "^src/[A-Za-z0-9._/-]+\.cs$") -Message "source manifest '$Name' contains non-canonical entry '$entryText'."
        Assert-That -Condition ($entryText -notmatch "[*?]" -and $entryText -notmatch "(^|/)\.\.(/|$)") -Message "source manifest '$Name' contains a glob or traversal entry '$entryText'."
    }

    $duplicates = @($entries | Group-Object | Where-Object { $_.Count -gt 1 })
    Assert-That -Condition ($duplicates.Count -eq 0) -Message "source manifest '$Name' contains duplicate paths."

    foreach ($entry in $entries) {
        $path = Join-Path $repositoryRoot $entry.Replace("/", "\")
        Assert-That -Condition (Test-Path -LiteralPath $path -PathType Leaf) -Message (
            "SDK project '$Name' references a missing file '$entry'.")
    }

    return @($entries)
}

function Assert-DoesNotContainForbiddenPath {
    param(
        [Parameter(Mandatory = $true)][string[]]$Entries,
        [Parameter(Mandatory = $true)][string[]]$Patterns,
        [Parameter(Mandatory = $true)][string]$SourceSet
    )

    foreach ($entry in $Entries) {
        foreach ($pattern in $Patterns) {
            Assert-That -Condition ($entry -notmatch $pattern) -Message "source manifest '$SourceSet' includes forbidden source '$entry'."
        }
    }
}

function Assert-DoesNotContainForbiddenText {
    param(
        [Parameter(Mandatory = $true)][string[]]$Entries,
        [Parameter(Mandatory = $true)][string[]]$Tokens,
        [Parameter(Mandatory = $true)][string]$SourceSet
    )

    foreach ($entry in $Entries) {
        $path = Join-Path $repositoryRoot $entry.Replace("/", "\")
        $content = [IO.File]::ReadAllText($path)
        foreach ($token in $Tokens) {
            Assert-That -Condition (-not $content.Contains($token)) -Message "source manifest '$SourceSet' source '$entry' contains forbidden token '$token'."
        }
    }
}

function Get-RelativeSourceEntries {
    param([Parameter(Mandatory = $true)][string]$RelativeDirectory)

    $directory = Join-Path $repositoryRoot $RelativeDirectory
    Assert-That -Condition (Test-Path -LiteralPath $directory -PathType Container) -Message "missing source directory '$RelativeDirectory'."
    return @(Get-ChildItem -LiteralPath $directory -Filter '*.cs' -Recurse |
        ForEach-Object {
            $_.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
        })
}

Assert-That -Condition (Test-Path -LiteralPath $directoryBuildPropsPath -PathType Leaf) -Message (
    "missing Directory.Build.props.")

$adminEntries = Get-ManifestEntries -Name "Admin"
$watchdogEntries = Get-ManifestEntries -Name "Watchdog"
$appEntries = Get-ManifestEntries -Name "App"

# ---------------------------------------------------------------------------
# The watchdog runs unelevated and its whole job is to put a display mode back.
# Keeping its compiled surface small is what makes that claim auditable, so the
# set stays explicitly enumerated rather than inferred.
# ---------------------------------------------------------------------------
$expectedWatchdogEntries = @(
    # The exit codes this process returns, shared with the two readers in the
    # tray process so the cross-process contract has one definition.
    'src/Core/DisplayWatchdogExitCodes.cs',
    'src/Watchdog/AssemblyInfo.cs',
    'src/Watchdog/DisplayWatchdogProtocol.cs',
    'src/Watchdog/Program.cs',
    'src/Core/DetailedTiming.cs',
    'src/Core/DisplayModeKey.cs',
    'src/Core/DisplayEndpoint.cs',
    'src/Core/EdidBaseBlock.cs',
    # Replaces a private copy of the same constant-time loop that used to live
    # inside DisplayWatchdogProtocol.cs. Pure, no I/O and no interop, so the
    # surface this guard protects did not actually grow.
    'src/Core/FixedTimeComparer.cs',
    'src/Core/HardwareSnapshot.cs',
    'src/Core/HexCodec.cs',
    'src/Core/MonitorIdentity.cs',
    'src/Core/Sha256Digest.cs',
    'src/Platform.Windows/DisplayModeNativeMethods.cs',
    'src/Platform.Windows/DisplayModeService.cs',
    'src/Platform.Windows/DisplayTopologyNativeMethods.cs',
    'src/Platform.Windows/DisplayTopologyReader.cs',
    'src/Platform.Windows/InternalPanelSelector.cs',
    'src/Platform.Windows/MonitorDevnodeReader.cs',
    'src/Platform.Windows/StableDisplayTargetResolver.cs'
)
Assert-That -Condition ($watchdogEntries.Count -eq $expectedWatchdogEntries.Count) -Message 'Watchdog source set must remain minimal and explicit.'
foreach ($required in $expectedWatchdogEntries) {
    Assert-That -Condition ($watchdogEntries -contains $required) -Message "Watchdog source set is missing its reviewed source '$required'."
}

# ---------------------------------------------------------------------------
# Process boundaries.
# ---------------------------------------------------------------------------
Assert-DoesNotContainForbiddenPath -Entries $adminEntries -Patterns @(
    "^src/App/",
    "^src/Telemetry/",
    "^src/Watchdog/"
) -SourceSet "Admin"
Assert-DoesNotContainForbiddenText -Entries $adminEntries -Tokens @(
    "System.Windows.Forms",
    "MacBookEco.Telemetry",
    "MacBookEco.Watchdog"
) -SourceSet "Admin"

Assert-DoesNotContainForbiddenPath -Entries $watchdogEntries -Patterns @(
    "^src/Admin/",
    "^src/App/",
    "^src/Telemetry/",
    "^src/Platform\.Windows/(EdidOverrideService|PowerSchemeService|TransactionJournal|NativeMethods|EdidOverrideRegistry|MonitorDevnodeAccess|SecureStateStore)\.cs$"
) -SourceSet "Watchdog"
Assert-DoesNotContainForbiddenText -Entries $watchdogEntries -Tokens @(
    "MacBookEco.Admin",
    "EdidOverrideService",
    "PowerSchemeService",
    "TransactionJournal",
    "PowerDuplicateScheme",
    "PowerSetActiveScheme",
    "PowerDeleteScheme",
    "RegSetValueEx",
    "RegDeleteValue",
    "RegCreateKeyEx",
    "RegFlushKey",
    "EDID_OVERRIDE"
) -SourceSet "Watchdog"

Assert-DoesNotContainForbiddenPath -Entries $appEntries -Patterns @(
    '^src/Admin/',
    '^src/Watchdog/Program\.cs$',
    '^src/Platform\.Windows/(EdidOverrideService|PowerSchemeService)\.cs$'
) -SourceSet 'App'

# ---------------------------------------------------------------------------
# Layer purity. Core and Application are policy; they must stay free of the
# platform so they can be reasoned about and unit-tested without Windows.
# ---------------------------------------------------------------------------
$coreEntries = Get-RelativeSourceEntries -RelativeDirectory 'src\Core'
Assert-DoesNotContainForbiddenText -Entries $coreEntries -Tokens @(
    'System.Windows.Forms',
    'System.Management',
    'Microsoft.Win32',
    'DllImport(',
    '[DllImport',
    'System.Diagnostics.Process',
    'PerformanceCounter'
) -SourceSet 'Core'

$applicationEntries = Get-RelativeSourceEntries -RelativeDirectory 'src\Application'
Assert-DoesNotContainForbiddenText -Entries $applicationEntries -Tokens @(
    'System.Windows.Forms',
    'System.Management',
    'Microsoft.Win32',
    'DllImport(',
    '[DllImport'
) -SourceSet 'Application'

# The shell owns navigation and workflow, not platform access: native adapters
# reach it only through the composition root.
Assert-DoesNotContainForbiddenText -Entries @(
    'src/App/DashboardForm.cs',
    'src/App/TrayApplicationContext.cs'
) -Tokens @(
    'MacBookEco.Platform.Windows',
    'new DisplayTelemetryProvider(',
    'new HardwareDiscoveryService(',
    'new PowerSchemeService(',
    'new EdidOverrideService(',
    'new DisplayModeService(',
    'DllImport('
) -SourceSet 'Presentation'

# The profiles controller projects state into a view. It must not become a
# second place that can start a privileged mutation.
Assert-DoesNotContainForbiddenText -Entries @(
    'src/App/DashboardProfilesController.cs'
) -Tokens @(
    'OptimizationCommand.',
    'IOptimizationActionService',
    'MessageBox.Show(',
    'OptimizationCommandRunner'
) -SourceSet 'Profiles controller'

# The time-series model is deliberately drawable-agnostic so it can be tested
# without a message loop.
Assert-DoesNotContainForbiddenText -Entries @(
    'src/App/TimeSeriesBuffer.cs',
    'src/App/TimeSeriesStatistics.cs',
    'src/App/TimeSeriesAxisRange.cs'
) -Tokens @(
    'System.Drawing',
    'System.Windows.Forms'
) -SourceSet 'Time-series model'

# ---------------------------------------------------------------------------
# Composition root. Program.cs builds the native adapters; the coordinator
# receives them. Only the direction is asserted, not the exact spelling of
# every constructor.
# ---------------------------------------------------------------------------
$program = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\App\Program.cs'))
Assert-That -Condition $program.Contains('new WindowsOptimizationActionService(') -Message 'App composition root must construct the Windows action service.'

$windowsActions = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\App\WindowsOptimizationActionService.cs'))
foreach ($forbidden in @(
        'new HardwareDiscoveryService(',
        'new DisplayModeService(',
        'new StableDisplayTargetResolver(',
        'new EdidOverrideService(',
        'new PowerSchemeService('
    )) {
    Assert-That -Condition (-not $windowsActions.Contains($forbidden)) -Message "Windows action service must receive '$forbidden' from the composition root."
}

# CPU mutations are limited by a pure SMBIOS policy in both the unelevated UI
# and elevated helper. The helper gate must run before a privileged journal is
# opened; recovery deliberately has no such gate.
$powerSchemeService = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\Platform.Windows\PowerSchemeService.cs'))
$applyStart = $powerSchemeService.IndexOf('public PowerSchemeOperationResult ApplyPreset(')
$applyJournal = $powerSchemeService.IndexOf('JournalStore.OpenPowerMutation()', $applyStart)
$applyGate = $powerSchemeService.IndexOf('RequireSupportedHardware();', $applyStart)
Assert-That -Condition ($applyStart -ge 0 -and $applyGate -gt $applyStart -and $applyJournal -gt $applyGate) -Message 'CPU apply must verify supported SMBIOS hardware before opening the power journal.'
Assert-That -Condition $powerSchemeService.Contains('CpuHardwareSupportPolicy.Classify(') -Message 'CPU apply must use the shared pure hardware policy.'

$appProgram = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\App\Program.cs'))
$uninstallProbe = $appProgram.IndexOf('CheckUninstallSafety()')
$singleInstance = $appProgram.IndexOf('new Mutex(')
Assert-That -Condition ($uninstallProbe -ge 0 -and $singleInstance -gt $uninstallProbe) -Message 'uninstall safety probe must run before the single-instance UI path.'
Assert-That -Condition $appProgram.Contains('TimeSpan.FromSeconds(10)') -Message 'uninstall safety probe must have a bounded internal deadline.'
Assert-That -Condition $appProgram.Contains('RecoverForUninstall()') -Message 'the app must expose the fixed uninstall recovery workflow before its normal UI path.'

$installerScript = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'build\installer.iss'))
foreach ($requiredInstallerToken in @(
        'notimestamp',
        'InitializeUninstall',
        '--check-uninstall-safe',
        '--recover-for-uninstall',
        'UninstallNeedRestart',
        "HasCommandLineParameter('/FORCEUNINSTALL')",
        'if Started and (ExitCode = 0)',
        'if ForceUninstallRequested then'
    )) {
    Assert-That -Condition $installerScript.Contains($requiredInstallerToken) -Message "installer uninstall contract is missing '$requiredInstallerToken'."
}

# ---------------------------------------------------------------------------
# SDK projects are the source of truth for compilation. They must keep explicit
# allowlists so a new file cannot silently enter a privileged target.
# ---------------------------------------------------------------------------
$projectFiles = @(Get-ChildItem `
    -LiteralPath (Join-Path $repositoryRoot "projects") `
    -Filter "*.csproj")
foreach ($projectFile in $projectFiles) {
    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
    Assert-That -Condition ($project.Project.Sdk -eq "Microsoft.NET.Sdk") -Message (
        "$($projectFile.Name) must use Microsoft.NET.Sdk.")
    $compileNodes = @($project.SelectNodes('/Project/ItemGroup/Compile'))
    Assert-That -Condition ($compileNodes.Count -gt 0) -Message (
        "$($projectFile.Name) must declare an explicit source allowlist.")
}

# ---------------------------------------------------------------------------
# Common SDK settings preserve the product's runtime and compiler contract.
# ---------------------------------------------------------------------------
$requiredBuildProperties = @{
    TargetFramework = 'net48'
    LangVersion = '7.3'
    PlatformTarget = 'x64'
    EnableDefaultCompileItems = 'false'
    GenerateAssemblyInfo = 'false'
    NoStdLib = 'true'
    TreatWarningsAsErrors = 'true'
    Deterministic = 'true'
}
[xml]$commonBuild = Get-Content -LiteralPath $directoryBuildPropsPath -Raw
foreach ($propertyName in $requiredBuildProperties.Keys) {
    $node = $commonBuild.SelectSingleNode(
        "/Project/PropertyGroup/$propertyName")
    $actualValue = if ($null -eq $node) { '' } else { [string]$node.InnerText }
    Assert-That `
        -Condition ($actualValue -eq $requiredBuildProperties[$propertyName]) `
        -Message (
            "Directory.Build.props must set $propertyName to " +
            "'$($requiredBuildProperties[$propertyName])'.")
}
$pathMapNode = $commonBuild.SelectSingleNode('/Project/PropertyGroup/PathMap')
Assert-That -Condition ($null -ne $pathMapNode) -Message (
    'Directory.Build.props must set a deterministic PathMap.')

$appProject = [IO.File]::ReadAllText(
    (Join-Path $repositoryRoot "projects\MacBookEco.csproj"))
foreach ($requiredAppBuildToken in @(
        'MacBookEco.Admin.csproj',
        'MacBookEco.Watchdog.csproj',
        'ReferenceOutputAssembly="false"',
        'OutputItemType="CompanionExecutable"',
        'BeforeTargets="BeforeResGen"',
        '<LogicalName>%(Filename)%(Extension)</LogicalName>'
    )) {
    Assert-That -Condition $appProject.Contains($requiredAppBuildToken) -Message (
        "App SDK project is missing '$requiredAppBuildToken'.")
}

Write-Host "VerifyBaseline passed."
