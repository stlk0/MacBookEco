# MacBook Eco recovery

Keep MacBook Eco installed until display and power recovery are complete. Do not
edit `%ProgramData%\MacBookEco.State`, its permissions, or arbitrary EDID
registry paths. Invalid or pre-created state is reported as `Conflict` rather
than repaired automatically.

## Pending or indeterminate operation

If a command reports `Indeterminate`, leave the tray application running. The
elevated helper may still be completing and will be reconciled when it exits.

If the result remains pending, reopen the dashboard, review Diagnostics, and
retry the same action only after checking the reported live state.

## Display mode

For an unconfirmed 48/60 Hz change, use the rollback option or wait for the
countdown. The watchdog restores the saved mode after re-identifying the same
internal panel.

Installing either a reviewed or experimental profile never selects 48 Hz. After
restart, first verify in Windows that the exact native 60 Hz mode is available,
then request 48 Hz manually. The first experimental transition is temporary and
is kept only after visual confirmation; closing or declining the prompt restores
the complete saved mode.

If Windows display topology changed and automatic rollback could not identify
the panel, select the native mode in Windows display settings and save the
Diagnostics output.

## 48 Hz support

For `InstallPending`, `RestorePending`, or `Conflict`, retry the same install or
remove action from the current build. MacBook Eco will compare the recorded
identity and exact live override before making another change. It will not
delete a foreign or modified override.

For an experimental install or install retry, the elevated helper reads the
active hardware again and must reproduce the same model, live controlling GPU,
panel ID, normalized complete source-EDID signature, native DTD, generator formula,
deterministic descriptor placement, and exact expected override. Offline removal
also reproduces the model, the canonical GPU binding encoded by the profile ID,
panel and timing facts, formula, descriptor placement, and expected override,
but does not require an active display-adapter route. This keeps exact-owned
removal available when a driver or topology has failed. The durable monitor
identity must still resolve uniquely, the journaled hash must match the
recompiled override, and the live value must be byte-for-byte identical before
deletion. The journal stores identity, a normalized complete
source-EDID digest, and an owned-state hash—not EDID or DTD payloads. When the
complete source document remains available, its digest must match. Recovery
accepts a missing document only when Windows exposes exactly the journal-owned
128-byte base after restart; that fallback never authorizes a fresh write. If
any fact required for the current path is missing or different, recovery fails
closed and leaves the live override untouched.

If automatic recovery cannot revalidate the experimental profile, keep or
select native 60 Hz in Windows display settings, restart Windows, and copy the
public Diagnostics report before retrying. If the desktop is not usable, use
Windows Advanced Startup to regain a visible recovery desktop, then select the
native mode; do not guess at EDID registry paths or delete protected journal
files. A foreign or modified override requires deliberate administrator review
and is never adopted as MacBook Eco-owned state.

## Power plan

For `Creating`, `RestorePending`, or `Conflict`, retry the same CPU action. The
helper rechecks the active plan and exact application-owned plan before it
continues.

After **Restore original Windows power plan** succeeds, the inactive MacBook Eco
plan is intentionally retained. An experienced administrator may inspect it
with `powercfg /list` and remove only its exact inactive GUID.

## Uninstall

If uninstall reports that recovery is required, choose automatic recovery. The
uninstaller asks MacBook Eco to return the panel to native 60 Hz, repair only
exact application-owned display state when needed, remove 48 Hz support, and
restore the original power plan. It continues only after read-back verifies both
resources as restored. Accept the UAC and display-mode confirmation prompts; a
restart may be requested after removal.

If automatic recovery stops, MacBook Eco remains installed. Complete the
display and power steps above before trying again. `/FORCEUNINSTALL` removes the
application without restoring system state and should be used only when normal
recovery is no longer available.

Windows, hostile-state VM, reboot, visual rollback, cold-boot, and real-hardware
acceptance for generated profiles remain deferred. No such deferred check is a
reported pass.

For a bug report, include copied Diagnostics, the operation result, and whether
the display topology, active power plan, UAC prompt, or Windows restart changed
during the operation. Use **Copy public diagnostics**; do not append raw EDID
data, serial numbers, device-instance IDs, registry exports, or other
machine-specific identifiers.
