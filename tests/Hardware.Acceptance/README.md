# Designated-hardware acceptance

These release-gate suites modify real hardware. They never run implicitly.

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

## Display acceptance

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
