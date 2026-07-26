## Summary

<!-- Describe the behavior change and why it is needed. -->

## Validation

- [ ] `pwsh .\eng\Verify-Local.ps1` (or `-FullCi` when preparing a release)
- [ ] `git diff --check`

## Checklist

- [ ] Tests cover the changed behavior.
- [ ] Public API or documentation changes are explained.
- [ ] No credentials, database exports, or IDE state are included.
