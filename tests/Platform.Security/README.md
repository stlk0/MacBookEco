# Opt-in two-token NTFS security suite

This release-gate suite targets the real `%ProgramData%\MacBookEco.State` root.
Run it only in a disposable Windows VM with separate Standard User and
Administrator accounts. It is intentionally excluded from `tests\test-all.ps1`.

## Prepare the VM

1. Revert to a clean snapshot before each scenario.
2. Build with `build\build.ps1 -CompileOnly`.
3. Create `C:\.macbookeco-disposable-vm` manually.
4. Pass `-DisposableVm` to every script.

The scripts deliberately do not clean up privileged state.

## Hostile root scenarios

As Standard User, stage one case:

```powershell
.\tests\Platform.Security\stage-standard-user-root.ps1 -Scenario PrecreatedRoot -DisposableVm
# or: -Scenario ReparseRoot
```

Using the separate Administrator account, run the matching assertion:

```powershell
.\tests\Platform.Security\run-elevated-assertions.ps1 -Scenario PrecreatedRoot -DisposableVm
# or: -Scenario ReparseRoot
```

The expected result is a state-store conflict. The active power GUID must remain
unchanged and neither helper may reach a native mutation.

## Hostile child scenarios

On a fresh snapshot, stage one child case as Administrator and run
`HostileLocks`:

```powershell
.\tests\Platform.Security\stage-elevated-child.ps1 -Scenario PrecreatedLocks -DisposableVm
# or: ReparseLocks / HardLinkedLocks
.\tests\Platform.Security\run-elevated-assertions.ps1 -Scenario HostileLocks -DisposableVm
```

Each case must report `Conflict` without repair, deletion, or continued helper
mutation.

## Evidence

Keep console output, power-plan GUIDs before and after helper invocation, the
account that staged each root, the VM snapshot and Windows build, and the
separate supported-hardware EDID acceptance result. Parser or source checks do
not count as execution of this suite.
