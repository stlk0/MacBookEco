# Designated-hardware acceptance

These release-gate suites modify real hardware. They never run implicitly.

The experimental generator work does not run Windows, hostile-state VM, reboot,
visual rollback, cold-boot, or real-hardware acceptance. All of those phases are
deferred and none is reported as passed.

## Power preset cycle

Build from a normal Windows PowerShell prompt:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\build.ps1
```

Close MacBook Eco, open PowerShell as Administrator, and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  .\tests\Hardware.Acceptance\run-power-cycle.ps1 -DesignatedHardware
```

The harness requires elevation, matching `MacBookPro16,1` / `APPA044` hardware,
the explicit switch, and valid embedded helpers. It applies every CPU preset,
checks the active plan and result, then restores the initial plan. Evidence is
written below `build\acceptance\power-<UTC>`.

## Reviewed display-profile acceptance

Run this phase manually on reviewed hardware:

1. Start at native 60 Hz with no foreign override.
2. Install 48 Hz support through MacBook Eco and restart Windows.
3. Confirm `60 -> 48 -> 60`, then let one unconfirmed change time out and verify
   rollback.
4. At 60 Hz, remove support, restart, and verify that 48 Hz is absent.
5. Reinstall, restart, verify 48 Hz, and finish with a confirmed 48 Hz mode.

Record the monitor identity, diagnostics before and after restarts, refresh
rates, helper results, owned EDID bytes and hash, watchdog result, and final
pass/fail result. Keep this evidence outside the repository because it contains
machine-specific identifiers.

## Experimental generated-profile acceptance

A generated candidate is not supported or verified merely because its pure
formula and host-safe tests pass. If an experimental hardware phase is scheduled
explicitly, perform it only on designated hardware with a recoverable Windows
installation and keep all machine-specific evidence outside the repository.

The manual record must show that:

1. the exact model, controlling discrete GPU, panel ID, valid original EDID, and
   native DTD match the candidate shown before installation;
2. declining the separate experimental-risk warning launches no helper and
   changes no state, while acknowledgement sends only the fixed
   `install-experimental-display` command plus one canonical 64-hex comparison
   token that matches the candidate shown and cannot select EDID bytes or a
   target;
3. after restart, native 60 Hz remains available and 48 Hz was not selected
   automatically;
4. the first requested 48 Hz transition is temporary, the Watchdog restores the
   complete saved mode on rejection or timeout, and persistence occurs only
   after visual confirmation;
5. install read-back is byte-for-byte, reboot recovery regenerates the same
   identity and expected override, and removal deletes only that exact owned
   value;
6. a modified or foreign override, changed identity, or ambiguous topology is
   left untouched and reported fail-closed;
7. confirmed `60 -> 48 -> 60`, removal, restart, and cold-boot results are
   recorded without claiming broader panel or model support.

Until that explicit phase is completed, record experimental display acceptance
as **DEFERRED (not run; not passed)**.
