# Security policy

MacBook Eco changes display and power configuration. Hardware matching,
privilege boundaries, ownership checks, rollback, and copied diagnostics are
security-sensitive.

## Reporting a vulnerability

Report privately through **GitHub Security Advisories** from the repository's
Security tab. Do not open a public issue for a vulnerability that could leave a
display unusable, modify foreign configuration, expose machine identifiers, or
turn the elevated helper into an arbitrary command runner.

Useful reports include the affected version, reproduction steps, the observed
result, and redacted Diagnostics. Expect a best-effort acknowledgement within
seven days.

## Supported releases

Once stable public releases exist, only the latest signed stable release will
receive security fixes. Development and alpha builds are intended for local
testing on explicitly supported hardware.

`0.1.1-alpha` is not Authenticode-signed, so Windows may show SmartScreen or UAC
warnings. Do not work around them by adding antivirus exclusions.

See [the design and safety model](docs/DESIGN.md) for the privilege and recovery
boundaries.
