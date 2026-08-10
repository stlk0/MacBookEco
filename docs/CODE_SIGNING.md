# Code signing policy

## Current status

MacBook Eco is preparing an application to the SignPath Foundation open-source
program. Current alpha releases are unsigned. A release must not be described as
signed unless every signature in the release scope below has been verified.

If the application is accepted, signed releases will carry this attribution:

> Free code signing provided by
> [SignPath.io](https://signpath.io/), certificate by
> [SignPath Foundation](https://signpath.org/).

The Authenticode publisher will therefore be **SignPath Foundation**. SignPath
Foundation is not the developer or maintainer of MacBook Eco.

## Release scope

Only artifacts produced from this repository by the release workflow may be
submitted for signing. The intended signed release scope is:

- `MacBookEco.Admin.exe` and `MacBookEco.Watchdog.exe`;
- `MacBookEco.exe`, after it embeds the exact signed helper executables;
- the Inno Setup installer and its uninstaller.

The source archive and checksum files are not Authenticode-signed. Upstream
libraries, tools, and other third-party artifacts must not be signed using the
MacBook Eco signing policy.

Release signing must use a GitHub-hosted runner and SignPath origin verification.
The unsigned artifact must first be stored as a GitHub Actions artifact. Every
release signing request requires manual approval, and only the returned signed
artifact may be attached to the corresponding GitHub Release. Signing keys and
certificate private material must never be present in the repository or GitHub
Actions secrets.

## Roles

- Committers and reviewers: [stlk0](https://github.com/stlk0)
- Signing approvers: [stlk0](https://github.com/stlk0)

Changes from contributors without commit access require maintainer review before
merge. Everyone with a code-signing role must use multi-factor authentication for
GitHub and SignPath.

## Privacy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.

MacBook Eco has no application network access, updater, analytics, crash upload,
or remote administration. Hardware telemetry and protected recovery records stay
on the local computer. **Copy public diagnostics** places a deliberately redacted
summary on the clipboard; the user decides whether and where to share it.

## Security reports

Report suspected misuse of a signed artifact through
[GitHub Security Advisories](https://github.com/stlk0/MacBookEco/security/advisories/new).
See the [security policy](../SECURITY.md) for reporting guidance.
