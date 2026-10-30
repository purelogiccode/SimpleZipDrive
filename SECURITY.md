# Security Policy

## Supported versions

SimpleZipDrive is a rolling-release desktop application. Security fixes are shipped in the
latest release; older releases are not patched separately.

| Version | Supported |
|---|---|
| Latest release (3.1.x) | :white_check_mark: |
| Older releases | :x: |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately through GitHub's private vulnerability reporting:

1. Open <https://github.com/purelogiccode/SimpleZipDrive/security/advisories/new>.
2. Describe the issue, the affected version (Help -> About), the operating system and mount
   backend (Dokan, WinFsp or FUSE), and the steps to reproduce it.
3. Include a proof of concept or a minimal archive if it helps reproduce the problem. Do not
   attach confidential data.

If you cannot use GitHub, contact the maintainer through the profile at
<https://github.com/purelogiccode> and ask for a private channel before sharing details.

## What to expect

- **Acknowledgement:** within 5 business days.
- **Triage:** we confirm the report, assess severity, and agree on a remediation plan.
- **Disclosure:** we aim to publish a fix and a GitHub security advisory within 90 days. We will
  credit reporters in the advisory unless you prefer to stay anonymous.

## Scope

In scope:

- The application source in this repository (archive parsing, mounting, caching, settings,
  logging, update checker and bug reporter).
- The build and release pipeline in `.github/workflows` and `scripts/`.
- Vulnerable dependencies shipped in a release bundle.

Out of scope:

- Vulnerabilities in third-party filesystem drivers (Dokan, WinFsp, libfuse3, macFUSE) or the
  operating system - report those to the upstream project.
- Issues that require an attacker to already control the local machine or the user's account.
- Denial of service caused by mounting an untrusted archive that the user deliberately opened,
  unless it leads to memory corruption, code execution or data disclosure.
- Missing hardening headers on the documentation site (GitHub Pages).

## Safe harbour

We will not pursue legal action against researchers who act in good faith, avoid privacy
violations and data destruction, and give us a reasonable opportunity to fix the issue before
public disclosure.
