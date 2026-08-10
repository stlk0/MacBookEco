## What changed

<!-- Describe the user-visible or internal change in a few sentences. -->

## Verification

- [ ] `tests\test-all.ps1` passes, or deferred checks are explained
- [ ] Generated projects were refreshed after source membership changes
- [ ] No runtime dependency, private hardware data, or unintended network access was added
- [ ] User-visible behavior and recovery documentation are current
- [ ] Release and signing changes preserve trusted origin and manual approval

## System mutations

<!-- Delete this section when the change is read-only. -->

- [ ] Ownership and live state are checked before mutation
- [ ] Intent is durable before mutation and success requires read-back
- [ ] Foreign state is left untouched and rollback was exercised
