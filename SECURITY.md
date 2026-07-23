# Security policy

## Supported versions

Security fixes are applied to the latest released version. Upgrade to the newest version before reporting a suspected issue.

## Reporting a vulnerability

Please do not disclose a suspected vulnerability in a public issue. Use GitHub's private security advisory flow for this repository. Include:

- the affected package and version;
- a minimal reproduction or example input;
- the expected and observed behavior;
- any relevant Netezza version or configuration details.

Catalog SQL helpers return SQL text and do not execute it. Applications must still validate or safely quote untrusted identifiers and values before execution.
