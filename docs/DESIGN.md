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
9. An experimental display install requires a separate, explicit risk
   acknowledgement before UAC or any mutation is requested.

Invalid protected state, foreign overrides, modified app-owned resources, and
unresolved hardware are reported as conflicts. They are not repaired or deleted
automatically.

## Processes

MacBook Eco uses three executables:

- `MacBookEco.exe` owns the tray, dashboard, discovery, telemetry, and normal
  display-mode selection. It launches the other processes when needed.
- `MacBookEco.Admin.exe` is a one-shot elevated helper for EDID overrides and
  the application-owned power plan. It accepts a fixed command set and exits
  after the operation. Display install and removal take no profile, EDID, DTD,
  path, or device argument.
- `MacBookEco.Watchdog.exe` is a one-shot unelevated rollback process. It starts
  before a temporary display mode and restores the complete previous mode if
  confirmation never arrives.

The companion executables are embedded in the main application. Their bytes are
verified before launch while replacement is denied for the duration of the
operation.

## Display changes

A reviewed display profile contains the exact model, panel, original EDID,
native timing, GPU device, and added timing. Installation proceeds only when
those values match and the target override location is free. Reviewed static
profiles are selected first and retain their existing behavior.

When no reviewed profile matches, a local experimental candidate may be
considered. The exact SMBIOS manufacturer must be `Apple Inc.`; the model must
be an allowlisted Intel MacBook with discrete graphics; one internal panel and
its controlling compiled adapter pair must be unambiguous; the complete EDID
must be valid and have no foreign override; and a nonpreferred descriptor must
be free. The runtime allowlist currently contains two exact 16-inch AMD pairs;
NVIDIA models remain research-catalog entries. The preferred native DTD must
encode a refresh from 59 through 61 Hz inclusive. Complete-document validation
accepts a conservative EDID 1.4 subset: reserved base encodings and
unparsed monitor-descriptor types fail closed, and CTA revision 3 extensions
must have an empty data-block collection plus valid DTD and zero-padding
structure. Other checksum-correct extension or data-block forms remain
unsupported until their complete semantics are validated.

Generation uses positive integer arithmetic. With the native pixel clock in Hz
and `horizontalTotal = horizontalActive + horizontalBlanking`:

```text
targetVTotal =
    nativePixelClockHz / (horizontalTotal * 48)          // integer floor

targetVBlank = targetVTotal - verticalActive

targetPixelClock10Khz =
    (horizontalTotal * targetVTotal * 48 + 5000) / 10000 // nearest integer
```

The generator preserves active dimensions, horizontal blanking and sync,
physical dimensions, borders, flags, vertical front porch, and vertical sync
width. All additional vertical blanking goes to the vertical back porch. It
rejects rather than repairs a candidate if a DTD field is out of range, sync is
not wholly inside blanking, arithmetic overflows, the encoded pixel clock is
greater than native, or the encoded refresh differs from 48 Hz by more than
0.01 Hz. Native 60 Hz stays the preferred descriptor; 48 Hz is inserted only in
the free nonpreferred descriptor.

An experimental profile ID is deterministic over the generator recipe version,
model, canonical GPU vendor/device, panel ID, normalized complete source-EDID
signature, and native DTD. The elevated helper receives the fixed
`install-experimental-display` command plus one canonical 64-hex,
comparison-only acknowledgement token. It reads the hardware again, repeats
every gate, reproduces the candidate ID, and rejects a token mismatch before
recording intent or writing. The token cannot select a timing, monitor, path, or
EDID bytes. The ordinary argument-free `install-display` command rejects fresh
experimental selection. Neither command nor the protected journal carries
caller-provided EDID or DTD bytes. The journal records bounded identity facts,
the normalized source digest, and the exact expected owned-state hash.

Before an experimental install, the unelevated UI shows the model, panel ID,
native timing, and calculated timing without raw EDID or machine-unique device
data. The risk acknowledgement is a behavioral gate: declining or closing it
does not enqueue a command, launch the helper, request UAC, or change state. Its
opaque token is accepted only for the exact candidate shown; a candidate change
before or after UAC fails closed. Acknowledgement does not replace the helper's
independent revalidation.

The helper records the expected owned bytes before writing them, then verifies
the registry value byte-for-byte. Removal deletes only that exact owned value.
A foreign or changed value is left untouched. Forward install and install retry
rediscover the live controlling GPU as well as every other candidate fact.
Status, offline recovery, and removal regenerate the candidate from fresh
SMBIOS and durable monitor facts plus the protected source digest and canonical
GPU binding encoded by the allowlist key and profile ID. A currently available
complete EDID must reproduce that digest. The only missing-document fallback is
the exact 128-byte app-owned base that Windows can expose after restart; it can
reconcile or remove exact-owned state but never authorize a fresh write. These
paths deliberately do not require an active CCD/GPU route: otherwise a broken
display driver could strand the exact app-owned override that recovery exists
to remove. Any required identity, formula, expected-hash, or byte-comparison
mismatch stops without mutation.

For a 48/60 Hz switch, the application:

1. resolves the internal panel and saves its stable identity and complete mode;
2. before entering experimental 48 Hz, verifies that exact native 60 Hz remains
   available as the recovery mode;
3. starts the watchdog and waits for readiness;
4. tests and applies the new mode temporarily;
5. asks the user to confirm the picture;
6. commits the mode or lets the watchdog restore the saved mode.

Installing a profile never selects 48 Hz. After restart the user must request
the first 48 Hz transition explicitly, and it remains temporary until visual
confirmation succeeds.

The saved identity is resolved again before rollback. A Windows display number
such as `DISPLAY1` is never treated as a durable identity.

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
Windows state. A generated transaction must reproduce its model, canonical
profile-bound GPU pair, panel, normalized EDID, native DTD, formula, and expected
override. A forward write also re-proves the live controlling GPU; exact-owned
offline removal instead relies on the durable monitor proof and byte-for-byte
ownership comparison described above. If the facts required for that path
cannot be proven again, the application stops and reports a conflict or
recovery-required state without adopting, rewriting, or deleting live state.
User-facing recovery steps are documented in [RECOVERY.md](RECOVERY.md).

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
shell command, caller-provided registry path, arbitrary executable, EDID/DTD
payload, or caller-selected profile recipe.

The display driver can still fail to apply or restore a mode. The watchdog
reduces that risk but cannot guarantee recovery from a broken driver or GPU
reset. Alpha binaries are not Authenticode-signed; stable public releases should
be signed and tested on designated hardware.

The experimental generator has not been validated by this change on Windows, in
the hostile-state VM matrix, or on real hardware. Those acceptance phases are
deferred and are not counted as passed.
