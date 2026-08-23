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

For an unconfirmed 48 Hz/60 Hz Eco/native 60 Hz change, use the rollback option
or wait for the countdown. The watchdog restores the saved mode after
re-identifying the same internal panel.

If Windows display topology changed and automatic rollback could not identify
the panel, select the native mode in Windows display settings and save the
Diagnostics output.

## Eco display support

For `InstallPending`, `RestorePending`, or `Conflict`, retry the same install or
remove action from the current build. MacBook Eco will compare the recorded
identity and exact live override before making another change. It will not
delete a foreign or modified override. This also allows a newer build to replace
an older app-owned timing profile without first uninstalling the older build.

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
exact application-owned display state when needed, remove Eco display support, and
restore the original power plan. It continues only after read-back verifies both
resources as restored. Accept the UAC and display-mode confirmation prompts; a
restart may be requested after removal.

If automatic recovery stops, MacBook Eco remains installed. Complete the
display and power steps above before trying again. `/FORCEUNINSTALL` removes the
application without restoring system state and should be used only when normal
recovery is no longer available.

For a bug report, include copied Diagnostics, the operation result, and whether
the display topology, active power plan, UAC prompt, or Windows restart changed
during the operation. Use **Copy public diagnostics**; do not append raw EDID
data, serial numbers, device-instance IDs, registry exports, or other
machine-specific identifiers.
