# Design and safety model

MacBook Eco is deliberately small, but it changes display and power settings.
The design below records only the boundaries needed to make those changes
recoverable. Class names and UI layout are implementation details, not part of
the contract.

## Safety rules

1. Unknown or ambiguous hardware is read-only.
2. The tray application runs without administrator privileges.
3. A system change is made only by a short-lived helper with fixed commands.
4. The application changes only state it can identify as its own.
5. Intent is recorded before a privileged mutation and success requires
   read-back from Windows.
6. Display mode changes are temporary until the user confirms them.
7. Recovery never guesses a display, registry value, or power plan.
8. Monitoring remains read-only and stops expensive polling when hidden.

Invalid protected state, foreign overrides, modified app-owned resources, and
unresolved hardware are reported as conflicts. They are not repaired or deleted
automatically.

## Processes

MacBook Eco uses three executables:

- `MacBookEco.exe` owns the tray, dashboard, discovery, telemetry, and normal
  display-mode selection. It launches the other processes when needed.
- `MacBookEco.Admin.exe` is a one-shot elevated helper for EDID overrides and
  the application-owned power plan. It accepts a fixed command set and exits
  after the operation.
- `MacBookEco.Watchdog.exe` is a one-shot unelevated rollback process. It starts
  before a temporary display mode and restores the complete previous mode if
  confirmation never arrives.

The companion executables are embedded in the main application. Their bytes are
verified before launch while replacement is denied for the duration of the
operation.

## Display changes

A reviewed display profile contains the exact model, panel, original EDID,
native timing, GPU device, and added timings. Installation proceeds only when
those values match and both required override locations are free.

The helper records the expected owned bytes before writing them, then verifies
the registry value byte-for-byte. Removal deletes only that exact owned value.
A foreign or changed value is left untouched.

For a 48 Hz/60 Hz Eco/native 60 Hz switch, the application:

1. resolves the internal panel and saves its stable identity and complete mode;
2. starts the watchdog and waits for readiness;
3. tests and applies the new mode temporarily;
4. asks the user to confirm the picture;
5. commits the mode or lets the watchdog restore the saved mode.

The saved identity is resolved again before rollback. A Windows display number
such as `DISPLAY1` is never treated as a durable identity.

An older app-owned profile can be refreshed without compiling every historical
frequency into the current catalog. For a historical profile ID, the helper
requires the protected journal's stable monitor identity and SHA-256 to match
the exact live 128-byte override. It then completes a compare-before-delete
restore and starts a new journaled install. A missing journal, mismatched hash,
foreign value, or modified override is never migrated.

Windows can retain the old effective EDID until restart after that exact-owned
override is removed. To avoid an intermediate restart, the helper may recover
the original base block only by reversing app-supported non-preferred DTD
insertions and matching the protected full-block SHA-256 exactly. It records
the replacement intent before writing the new override, so only the final
restart is required. No normalized or model-only match authorizes this path.

## Power changes

CPU presets are written to a duplicated, application-owned Windows power plan;
the user's original plan is not edited. Before the first change, the helper
records both the original plan and the destination GUID. Every supported setting
is verified before the new plan becomes active.

Restoring selects the recorded original plan only when it is safe to do so. The
inactive MacBook Eco plan is retained for manual cleanup rather than deleted on
an ambiguous ownership decision.

## Protected state and recovery

Privileged operation state is stored below `%ProgramData%\MacBookEco.State`.
The elevated helper uses fixed file names and validates ownership, permissions,
object type, final path, reparse status, and link count. Display and power
operations use separate locks and bounded records.

An interrupted operation is reconciled against the recorded intent and current
Windows state. If they no longer agree, the application stops and reports a
conflict. User-facing recovery steps are documented in [RECOVERY.md](RECOVERY.md).

## Telemetry

Battery and display readings come from Windows. CPU load and frequency use
Windows performance data; optional CPU temperature and package power come from
an already-running compatible WMI sensor provider. Radeon telemetry dynamically
uses read-only AMD ADL functions from the installed driver.

Missing readings stay unavailable throughout the application. GPU polling runs
only while the dashboard is visible, history is bounded in memory, and there is
no database, continuous log, or network transmission.

## Security assumptions and residual risk

The watchdog protocol protects against accidental races and other Windows user
accounts; it is not a sandbox against a process already running as the same
user. The elevated helper protects the administrator boundary by accepting no
shell command, caller-provided registry path, or arbitrary executable.

The display driver can still fail to apply or restore a mode. The watchdog
reduces that risk but cannot guarantee recovery from a broken driver or GPU
reset. Alpha binaries are not Authenticode-signed; stable public releases should
be signed and tested on designated hardware.
