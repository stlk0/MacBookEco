# MacBook Eco

MacBook Eco is a small Windows tray utility for Intel MacBooks running Windows
through Boot Camp. It can reduce heat and idle power use with reviewed 48 Hz
and 58 Hz display modes and reversible CPU power presets.

It is a native WinForms application for .NET Framework 4.8. It has no service,
kernel driver, bundled runtime, analytics, or network access.

## Download

Download the installer from
[GitHub Releases](https://github.com/stlk0/MacBookEco/releases).

MacBook Eco is currently an unsigned alpha. Before installing, check the
[supported hardware](SUPPORTED_HARDWARE.md) and
[recovery guide](docs/RECOVERY.md).

## Status and support

Display support is available for reviewed exact profiles matching:

- MacBook Pro 16-inch, 2019 (`MacBookPro16,1`);
- internal panel `APPA044`;
- AMD Radeon Pro 5300M or 5500M.

Display changes require an exact hardware match. Other machines remain
diagnostic-only. See [supported hardware](SUPPORTED_HARDWARE.md) for the full
profile and tested driver.

## Features

- guarded switching between 48, 58, and 60 Hz with confirmation and automatic
  rollback;
- installation and removal of one app-owned 48 + 58 Hz display profile;
- three reversible CPU presets in an application-owned Windows power plan;
- battery, CPU, display, and read-only GPU telemetry;
- five-minute graphs while the dashboard is open;
- an unelevated tray application with short-lived UAC prompts only for system
  changes.

Unavailable sensor values are shown as `N/A`, not estimated as zero. CPU
temperature and package power require an already-running LibreHardwareMonitor
or OpenHardwareMonitor WMI provider. GPU sensor availability depends on the AMD
driver.

## Install and use

1. Download `MacBookEco-<version>-win-x64-setup.exe` from
   [GitHub Releases](https://github.com/stlk0/MacBookEco/releases), run it, and
   launch MacBook Eco.
2. On the supported machine, select **Install 48 + 58 Hz support**, approve UAC, and
   restart Windows.
3. Select **48 Hz** or **58 Hz**. Confirm the mode after checking the picture;
   otherwise the
   application and its watchdog restore the previous mode.
4. Choose a CPU preset if wanted. The application shows its values before it is
   applied and stores it in a separate MacBook Eco power plan.
5. Enable **Start with Windows** from the tray menu if needed.

Installing display support and selecting a refresh rate are separate operations. MacBook
Eco never removes an EDID override created by another tool.

An existing exact MacBook Eco profile is refreshed in the same helper operation,
even when an older build used different timings. The protected ownership journal
and exact live bytes must still match. Only modes currently exposed by Windows
appear in the selector. The replacement uses one helper operation and requires
only the final Windows restart that loads the new mode list.

48 Hz is the compatibility mode listed by Apple. The native-clock 58 Hz mode
has been hardware-verified on the primary Radeon Pro 5300M profile. It remains
explicitly experimental on the other matching APPA044 profiles until each has
equivalent hardware evidence.

The included profiles are:

| Profile | Display | CPU behavior |
|---|---:|---|
| Everyday | 60 Hz | Responsive, battery-aware |
| Cool & quiet | 48 Hz | Turbo disabled, moderate limits |
| Battery saver | 48 Hz | Stronger limits, passive cooling |

Display confirmation and the CPU UAC operation remain separate safety steps.
Cancelling the CPU step does not undo an already confirmed display change.

## Remove or recover

Uninstall MacBook Eco from Windows Settings. If display or power recovery is
still required, the uninstaller offers to run it after confirmation. The guided
flow returns the internal panel to **60 Hz**, repairs exact application-owned
state when necessary, removes **Eco display support**, restores the original Windows
power plan, verifies both results, and then continues removal. Accept any UAC
or display-mode confirmation prompts. Windows may request a restart afterward.

If guided recovery reports `Conflict`, `Indeterminate`, or remains pending,
MacBook Eco stays installed; follow the [recovery guide](docs/RECOVERY.md).
`/FORCEUNINSTALL` removes only the application; it does not repair display or
power state.

## Build and test

Install the .NET SDK selected by `global.json`, then run from Windows
PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\test-all.ps1
```

Create an installer with Inno Setup 6:

```powershell
.\build\package.ps1
```

The SDK is a development dependency only. Shipped binaries still target x64 and
use the installed .NET Framework 4.8; they do not bundle or require the .NET
SDK.

## Downloads and bug reports

Published installers and source archives are available from
[GitHub Releases](https://github.com/stlk0/MacBookEco/releases). For a bug,
use the repository issue form and paste **Copy public diagnostics** output.
That export deliberately excludes exact EDID fingerprints and free-form error
details. Do not add raw EDID data, serial numbers, device-instance IDs, or
registry exports to a public issue.

## Privacy

MacBook Eco has no application network access, updater, analytics, crash upload,
or remote administration. Hardware telemetry and recovery records remain on the
local computer. **Copy public diagnostics** only copies a redacted summary to the
clipboard; the user decides whether and where to share it.

## Code signing policy

Current alpha releases are unsigned. The project is preparing an application for
the SignPath Foundation open-source signing program. If accepted, signed releases
will use free code signing provided by SignPath.io, certificate by SignPath
Foundation, and Windows will show **SignPath Foundation** as the publisher.

The [code signing policy](docs/CODE_SIGNING.md) defines the signed files, build
origin, manual approval, project roles, and privacy statement. Never treat a
release as signed without verifying its Authenticode signatures.

## Documentation

- [Supported hardware](SUPPORTED_HARDWARE.md)
- [Recovery](docs/RECOVERY.md)
- [Design and safety model](https://github.com/stlk0/MacBookEco/blob/main/docs/DESIGN.md)
- [Code signing policy](docs/CODE_SIGNING.md)
- [Contributing](https://github.com/stlk0/MacBookEco/blob/main/CONTRIBUTING.md)
- [Security policy](https://github.com/stlk0/MacBookEco/blob/main/SECURITY.md)

## Limitations

- Only the listed MacBook, panel, and GPU combination supports display changes.
- GPU clocks, voltage, and fans are not changed.
- Sensor coverage depends on the installed Windows and AMD drivers.
- The interface is currently English.
- Alpha builds and the installer are not Authenticode-signed.

## Support

MacBook Eco is free and open source. If you find it useful, you can support the
developer's open-source work on [Ko-fi](https://ko-fi.com/stlk0). Support is
optional and does not affect access to features or issue priority.

## License

MIT. Apple, MacBook, Boot Camp, Windows, and Radeon are trademarks of their
respective owners. This project is independent and is not endorsed by Apple,
Microsoft, or AMD. See [third-party notices](THIRD_PARTY_NOTICES.md).
