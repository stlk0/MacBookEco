[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

function Read-Source {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    return [IO.File]::ReadAllText((Join-Path $repositoryRoot $RelativePath))
}

function Assert-That {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "VerifyAppOrchestration: $Message"
    }
}

$adminHelper = Read-Source 'src\App\ElevatedAdminHelper.cs'
$adminProgram = Read-Source 'src\Admin\Program.cs'
$helperExitCodes = Read-Source 'src\Core\AdminHelperExitCodes.cs'
$dashboard = Read-Source 'src\App\DashboardForm.cs'
$tray = Read-Source 'src\App\TrayApplicationContext.cs'
$program = Read-Source 'src\App\Program.cs'
$uninstallRecovery = Read-Source 'src\App\UninstallSafetyPolicy.cs'
[xml]$appTestsProject = Read-Source 'projects\MacBookEco.AppTests.csproj'

Assert-That -Condition (-not $adminProgram.Contains('last-admin-result.txt')) -Message (
    'elevated helper must not write path-based diagnostics into the trusted state root.')
foreach ($forbidden in @(
        'File.WriteAllText(',
        'File.SetAccessControl(',
        'Directory.CreateDirectory('
    )) {
    Assert-That -Condition (-not $adminProgram.Contains($forbidden)) -Message (
        "elevated helper must not perform path-based diagnostic mutation '$forbidden'.")
}
Assert-That -Condition (-not $adminHelper.Contains('ReadHelperReport()')) -Message (
    'GUI helper adapter must not read an unverified path-based helper report.')
Assert-That -Condition (-not $adminHelper.Contains('NamedPipeServerStream')) -Message (
    'GUI helper diagnostics must not add a caller-controlled IPC channel.')
Assert-That -Condition (-not $adminProgram.Contains(
    'NamedPipeClientStream')) -Message (
    'elevated helper diagnostics must remain process-exit codes only.')
Assert-That -Condition $adminHelper.Contains(
    'AdminHelperExitCodes.DiagnosticReason(exitCode)') -Message (
    'GUI helper adapter must decode only the bounded exit-code contract.')
foreach ($forbidden in @(
        'DeviceInstanceId',
        'MonitorDevicePath',
        'RegistryDevicePath',
        'ToByteArray()',
        'exception.Message'
    )) {
    Assert-That -Condition (-not $helperExitCodes.Contains($forbidden)) -Message (
        "diagnostic exit codes must not carry private or free-form data '$forbidden'.")
}
$appProjectReference = $appTestsProject.SelectSingleNode(
    "/Project/ItemGroup/ProjectReference[@Include='MacBookEco.csproj']")
Assert-That -Condition ($null -ne $appProjectReference) -Message (
    'App tests must reference the production SDK project.')
$appSourceLinks = @($appTestsProject.SelectNodes(
    "/Project/ItemGroup/Compile[starts-with(@Include, '..\src\')]"))
Assert-That -Condition ($appSourceLinks.Count -eq 0) -Message (
    'App tests must not recompile selected production source files.')
foreach ($source in @($dashboard, $tray)) {
    Assert-That -Condition (-not $source.Contains('_actions.SetDisplayRefreshRate(')) -Message (
        'presentation must dispatch display mutations through the shared runner.')
    Assert-That -Condition (-not $source.Contains('_actions.ApplyCpuPreset(')) -Message (
        'presentation must dispatch CPU mutations through the shared runner.')
    Assert-That -Condition (-not $source.Contains('_actions.RestoreCpuPower(')) -Message (
        'presentation must dispatch recovery mutations through the shared runner.')
}

Assert-That -Condition $tray.Contains('_runner.IsBusy') -Message (
    'Exit must consult the shared runner before closing the application.')

# Exiting with a privileged command in flight would abandon durable recovery
# state. The busy branch must therefore return before it reaches teardown.
# Asserted structurally rather than by button caption: the wording is
# presentation, the ordering is the safety property.
$exitStart = $tray.IndexOf(
    'private void ExitApplication()',
    [System.StringComparison]::Ordinal)
Assert-That -Condition ($exitStart -ge 0) -Message (
    'Tray must expose a single bounded Exit implementation.')
$busyBranch = $tray.IndexOf(
    '_runner.IsBusy',
    $exitStart,
    [System.StringComparison]::Ordinal)
$teardown = $tray.IndexOf(
    'ExitThread()',
    $exitStart,
    [System.StringComparison]::Ordinal)
Assert-That -Condition ($busyBranch -gt $exitStart -and $teardown -gt $busyBranch) -Message (
    'Exit must test the runner before it tears the application down.')
$busyBody = $tray.Substring($busyBranch, $teardown - $busyBranch)
Assert-That -Condition ($busyBody.Contains('return;')) -Message (
    'Exit while mutation is active must leave the application in tray.')
Assert-That -Condition $program.Contains(
    'DisplayWatchdogStartupRecovery.Recover(') -Message (
    'the composition root must reconcile stale watchdog sessions at startup.')
$actionServiceStart = $program.IndexOf(
    'return new WindowsOptimizationActionService(',
    [System.StringComparison]::Ordinal)
Assert-That -Condition ($actionServiceStart -ge 0) -Message (
    'the Windows action-service composition was not found.')
$actionServiceEnd = $program.IndexOf(
    ');',
    $actionServiceStart,
    [System.StringComparison]::Ordinal)
Assert-That -Condition ($actionServiceEnd -gt $actionServiceStart) -Message (
    'the Windows action-service composition has no constructor boundary.')
$actionServiceComposition = $program.Substring(
    $actionServiceStart,
    $actionServiceEnd - $actionServiceStart)
Assert-That -Condition $actionServiceComposition.Contains('startupRecovery') `
    -Message (
        'the action service must receive startup recovery so failed display ' +
        'reconciliation remains fail-closed.')

# The uninstall path is the only mutation coordinator outside the shared UI
# runner. It accepts no caller-selected action and uses the same narrow action
# service contract, in a fixed display-first recovery sequence.
foreach ($required in @(
        '_actions.InstallDisplaySupport()',
        '_actions.SetDisplayRefreshRate(60, displayConfirmation)',
        '_actions.RemoveDisplaySupport()',
        '_actions.RestoreCpuPower()'
    )) {
    Assert-That -Condition $uninstallRecovery.Contains($required) -Message (
        "uninstall recovery is missing fixed action '$required'.")
}
Assert-That -Condition (-not $uninstallRecovery.Contains(
    '_actions.ApplyCpuPreset(')) -Message (
    'uninstall recovery must never apply a new CPU preset.')

Write-Host 'VerifyAppOrchestration passed.'
