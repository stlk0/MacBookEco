# Test strategy

MacBook Eco uses three deliberately different kinds of verification. A test
should live in the narrowest category that proves the behavior without
weakening the product's Windows and hardware safety boundaries.

## Compiled behavior tests

Use compiled tests for pure policy, state transitions, orchestration outcomes,
failure handling, read-back, and rollback behavior. These tests should assert
observable results and calls at an injected native or process boundary, not
private field identity, method names, source wording, or cache implementation.

`MacBookEco.AppTests` contains the Core, hostile-parser, App orchestration, and
deterministic telemetry cases. It references `MacBookEco.csproj`, so all of
them exercise the exact production App assembly rather than recompiling a
second selection of production files. `MacBookEco.WatchdogTests` similarly
references the production Watchdog project. The disposable-VM security harness
references the production Admin project.

The small repository runner remains intentional: the suites must also run as
ordinary .NET Framework executables in constrained Windows and VM workflows,
including harnesses that accept executable paths or return deferred exit codes.
Adopting a test framework would be a separate repository-wide migration, not a
reason to mix package-specific tests with the current executable contract.

Every compiled multi-case behavioral suite must:

- report every case instead of stopping at the first failure;
- include enough context to identify the failed state combination;
- cover complete small state matrices rather than selected examples;
- prove fail-closed behavior for unknown or unverifiable state;
- avoid live EDID, display-mode, power-plan, ACL, or hardware mutation unless
  the suite is explicitly a disposable-VM or designated-hardware harness.

Single-contract command-line harnesses, such as packaging integrity and
platform diagnostics, may remain one integration case when splitting them
would duplicate setup and obscure the artifact or host contract. A live
hardware probe must report unavailable hardware as deferred; it must not be a
required test case that passes without exercising its native boundary.

## Source and project boundary audits

Source inspection is appropriate only when the source shape is itself the
security or packaging contract. Current examples include explicit SDK compile
allowlists, forbidden dependencies in elevated helpers, absence of arbitrary
paths or shell commands, privileged intent-before-mutation ordering, native
signature ownership, composition-root safety bindings, and installer inputs.

Do not add source checks merely to prove that a class, method call, literal,
message, timeout expression, or implementation technique exists. Add a narrow
compiled seam and verify behavior instead. If a source audit must remain,
describe the safety boundary it protects rather than the spelling it expects.

## Host-specific verification

`tests/test-all.ps1` is the authoritative full-suite entry point. It builds the
SDK solution once, runs the already-built host-safe executables, performs the
source and dependency audits, and reports unavailable diagnostics as deferred
rather than passed.

The authoritative build runs in the GitHub Actions `build` workflow on
`windows-2022`. Linux development instances may run only host-independent
checks such as `git diff --check`, XML parsing, and focused `rg` audits. They
must not claim compiled .NET Framework tests as passed.

Hostile-state VM matrices, elevated EDID or power mutations, visual rollback,
cold boot, and real-hardware acceptance remain separate documented workflows.
Their absence from ordinary CI is a recorded deferral, not a successful test.
