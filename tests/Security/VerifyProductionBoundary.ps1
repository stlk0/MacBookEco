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
        throw "VerifyProductionBoundary: $Message"
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Boundary
    )

    Assert-That -Condition $Source.Contains($Token) -Message (
        "$Boundary is missing reviewed token '$Token'.")
}

function Assert-DoesNotContain {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Boundary
    )

    Assert-That -Condition (-not $Source.Contains($Token)) -Message (
        "$Boundary contains forbidden token '$Token'.")
}

$edid = Read-Source 'src\Platform.Windows\EdidOverrideService.cs'
$edidBaseBlock = Read-Source 'src\Core\EdidBaseBlock.cs'
$edidPolicy = Read-Source 'src\Core\EdidRecoveryPolicy.cs'
$overrideRegistry = Read-Source 'src\Platform.Windows\EdidOverrideRegistry.cs'
$devnodeReader = Read-Source 'src\Platform.Windows\MonitorDevnodeReader.cs'
$devnodeAccess = Read-Source 'src\Platform.Windows\MonitorDevnodeAccess.cs'
$power = Read-Source 'src\Platform.Windows\PowerSchemeService.cs'
$powerNative = Read-Source 'src\Platform.Windows\PowerSchemeNative.cs'
$statusReaders = Read-Source 'src\Platform.Windows\OptimizationStatusReaders.cs'
$journalStore = Read-Source 'src\Platform.Windows\JournalStore.cs'
$store = Read-Source 'src\Platform.Windows\SecureStateStore.cs'
$native = Read-Source 'src\Platform.Windows\NativeMethods.cs'

# EDID ownership and restore must stay on the checked journal/devnode surface.
foreach ($required in @(
        'JournalStore.OpenEdidMutation',
        'EdidRecoveryPolicy.ForInstall',
        'EdidRecoveryPolicy.ForRestore',
        'ResolveStoredForRestore'
    )) {
    Assert-Contains -Source $edid -Token $required -Boundary 'EDID service'
}
foreach ($forbidden in @(
        'Registry.LocalMachine',
        'OpenCurrentMonitorParameters',
        'DisplayModeService'
    )) {
    Assert-DoesNotContain -Source $edid -Token $forbidden -Boundary 'EDID service'
}
foreach ($required in @(
        'ResolveJournaledOriginalHardware',
        'ClassifyProtectedJournalOverride',
        'TryResolveOriginalBaseEdid'
    )) {
    Assert-Contains -Source $edid -Token $required `
        -Boundary 'stale EDID refresh recovery'
}
Assert-Contains -Source $statusReaders -Token 'TryResolveOriginalBaseEdid' `
    -Boundary 'stale EDID terminal read-back'
Assert-Contains -Source $edidPolicy `
    -Token 'previousState == EdidJournalState.Restored' `
    -Boundary 'restored EDID install policy'
$beginInstall = $edid.IndexOf(
    'private EdidOverrideOperationResult BeginNewInstall(',
    [StringComparison]::Ordinal)
$resolveOriginal = $edid.IndexOf(
    'ResolveJournaledOriginalHardware(',
    $beginInstall,
    [StringComparison]::Ordinal)
$selectProfile = $edid.IndexOf(
    'ProfileCatalog.Select(',
    $resolveOriginal,
    [StringComparison]::Ordinal)
Assert-That -Condition (
    $beginInstall -ge 0 -and
    $resolveOriginal -gt $beginInstall -and
    $selectProfile -gt $resolveOriginal) `
    -Message 'restored original EDID must be resolved before profile selection.'
foreach ($required in @(
        'TryRecoverExactOriginal',
        'Sha256Digest.Compute(candidate).Equals',
        'IsDetailedTimingDescriptor(descriptorIndex)'
    )) {
    Assert-Contains -Source $edidBaseBlock -Token $required `
        -Boundary 'exact original EDID recovery'
}
Assert-Contains -Source $overrideRegistry -Token 'DeleteExact' `
    -Boundary 'EDID registry adapter'
Assert-DoesNotContain -Source $overrideRegistry -Token 'DeleteOverrideValue' `
    -Boundary 'EDID registry adapter'
foreach ($required in @('SetupDiEnumDeviceInfo', 'GuidDevClassMonitor')) {
    Assert-Contains -Source $devnodeReader -Token $required `
        -Boundary 'monitor devnode reader'
}
Assert-Contains -Source $devnodeAccess -Token 'SetupDiOpenDeviceInfo' `
    -Boundary 'monitor devnode access'

# Keep the only remaining ordering audit narrow. It protects the real native
# fault boundary until the service exposes a compiled orchestration seam.
$creatingSave = $power.IndexOf(
    'creating = journals.SavePower(creating);',
    [StringComparison]::Ordinal)
$duplicateCall = $power.IndexOf('DuplicateScheme(', [StringComparison]::Ordinal)
Assert-That -Condition ($creatingSave -ge 0 -and $duplicateCall -gt $creatingSave) `
    -Message 'power intent must be durable before scheme duplication.'

$reactivateStart = $power.IndexOf(
    'private static PowerSchemeOperationResult ReactivateRetained(',
    [StringComparison]::Ordinal)
$continueStart = $power.IndexOf(
    'private static PowerSchemeOperationResult ContinueCreating(',
    [StringComparison]::Ordinal)
Assert-That -Condition ($reactivateStart -ge 0 -and $continueStart -gt $reactivateStart) `
    -Message 'retained power reactivation boundary was not found.'
$reactivateBody = $power.Substring(
    $reactivateStart,
    $continueStart - $reactivateStart)
$reactivateSave = $reactivateBody.IndexOf(
    'creating = journals.SavePower(creating);',
    [StringComparison]::Ordinal)
$reactivateContinue = $reactivateBody.IndexOf(
    'return ContinueCreating(journals, creating);',
    [StringComparison]::Ordinal)
Assert-That -Condition (
    $reactivateSave -ge 0 -and $reactivateContinue -gt $reactivateSave) `
    -Message 'reactivation must save its return target before mutation.'

# Only documented native not-found results may prove scheme absence.
$friendlyNameStart = $powerNative.IndexOf(
    'internal static bool TryReadFriendlyName(',
    [StringComparison]::Ordinal)
$schemeExistsStart = $powerNative.IndexOf(
    'internal static bool SchemeExists(',
    [StringComparison]::Ordinal)
Assert-That -Condition (
    $friendlyNameStart -ge 0 -and $schemeExistsStart -gt $friendlyNameStart) `
    -Message 'shared power existence classifier was not found.'
$friendlyNameBody = $powerNative.Substring(
    $friendlyNameStart,
    $schemeExistsStart - $friendlyNameStart)
Assert-Contains -Source $friendlyNameBody -Token 'IsDocumentedNotFound(error)' `
    -Boundary 'power existence classifier'
Assert-DoesNotContain -Source $friendlyNameBody -Token 'catch (' `
    -Boundary 'power existence classifier'
foreach ($required in @(
        'NativeMethods.ERROR_FILE_NOT_FOUND',
        'NativeMethods.ERROR_NOT_FOUND'
    )) {
    Assert-Contains -Source $powerNative -Token $required `
        -Boundary 'power not-found classifier'
}

# Writer and reader share one ownership marker rather than duplicated literals.
Assert-Contains -Source $powerNative -Token 'OwnedFriendlyName' `
    -Boundary 'power ownership marker'
foreach ($source in @($power, $statusReaders)) {
    Assert-DoesNotContain -Source $source -Token '"MacBook Eco (" +' `
        -Boundary 'power ownership consumer'
}

Assert-Contains -Source $journalStore -Token 'ValidatePowerReplacement' `
    -Boundary 'durable journal replacement'
foreach ($required in @(
        'GetFileInformationByHandle',
        'GetFinalPathNameByHandleW',
        'GetSecurityInfo',
        'LockFileEx',
        'ReplaceFileW',
        'Flush(true)'
    )) {
    Assert-Contains -Source $store -Token $required -Boundary 'secure state store'
}
Assert-DoesNotContain -Source $store -Token 'File.Replace' `
    -Boundary 'secure state store'

# Interop shape is itself the ownership contract for caller-selected GUIDs.
Assert-DoesNotContain -Source $native -Token 'PowerDeleteScheme' `
    -Boundary 'native power interop'
Assert-Contains -Source $native -Token 'ref IntPtr destinationSchemeGuid' `
    -Boundary 'native power duplicate interop'
Assert-DoesNotContain -Source $native -Token 'out IntPtr destinationSchemeGuid' `
    -Boundary 'native power duplicate interop'

Write-Host 'VerifyProductionBoundary passed.'
