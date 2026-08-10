# Contributing

## Build and test

MacBook Eco targets x64, C# 7.3, and the installed .NET Framework 4.8. Install
the .NET SDK selected by `global.json` and run the complete host-safe suite:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test-all.ps1
```

The shipped executables must keep working without a bundled runtime, service,
kernel driver, or NuGet runtime dependency. Do not add network access or build a
shell command from user input.

The SDK-style files under `projects/` define target membership through explicit
`Compile` items. After adding, removing, or moving a source file, update every
affected project. Do not enable default source discovery: the Admin and Watchdog
source surfaces must remain explicit and reviewable.

## Safety-sensitive changes

A system mutation must identify owned state, record intent before changing it,
verify live state afterward, and have a tested recovery path. Unknown hardware
and foreign configuration must remain untouched.

Tests that modify the real EDID, power plan, or protected state run only on
designated hardware or in the disposable VM described by their local README.
They are not implied by the host-safe test command.

## Display profiles

Do not submit a calculated timing without evidence from the actual machine.
Use the offline proposal utility and reviewed-manifest workflow documented in
[`profiles/README.md`](profiles/README.md), then use the
[display profile issue](.github/ISSUE_TEMPLATE/display-profile.yml), which
requests the model, panel, original and candidate timings, GPU/driver, rollback,
removal, cold-boot, and telemetry evidence. The installed application never
loads profile files or calculates a fallback timing.

Never commit serial numbers, device-instance suffixes, battery reports, registry
exports, acceptance evidence, or private machine logs.

## Release and signing changes

The release workflow, build scripts, signing configuration, and
[code signing policy](docs/CODE_SIGNING.md) are security-sensitive. Keep their
changes focused and review the complete build-to-release path. Do not commit API
tokens, private keys, certificate containers, or a local SignPath configuration
that contains credentials.

Only project binaries built by the trusted GitHub Actions workflow may be
submitted for project signing. Do not submit upstream or locally supplied
binaries. Every release signing request requires explicit approval by a listed
signing approver.

## Pull requests

Keep changes focused. Update public documentation only when user-visible
behavior, supported hardware, recovery, or a security boundary changes. Run the
host-safe suite and describe any deferred hardware or VM verification.
