# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 0.2.x | Yes |
| 0.1.x | No |

Security fixes are released for the latest minor version.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private
vulnerability reporting flow:

<https://github.com/KingStan0/vitals/security/advisories/new>

If that is unavailable, contact the maintainer through
<https://github.com/KingStan0> and ask for a private channel. Include:

- affected version (`vitals --version`)
- Windows version and architecture
- steps to reproduce, or a minimal proof of concept
- impact and any known mitigations

You will get an acknowledgement as soon as practical. Please allow time for a
fix and coordinated disclosure before publishing details.

## Scope

Examples of in-scope issues:

- code execution or privilege escalation caused by parsing untrusted config or
  sensor data
- path traversal or unintended file writes in `export`, `--out` or config
  loading
- crashes triggered by malformed local input that could be used for denial of
  service
- supply-chain or release-artifact integrity problems

Examples of out-of-scope issues:

- missing sensors or inaccurate readings on specific hardware
- Windows SmartScreen warnings caused by the unsigned binary
- antivirus false positives
- vulnerabilities in third-party drivers or in PawnIO itself (report those to
  the upstream project)

## Design notes

- `vitals` has no telemetry and makes no outbound network connections.
- It never downloads, bundles or installs a kernel driver. LibreHardwareMonitor
  may use an existing PawnIO installation, which is a separate signed product.
- Release zips include a SHA-256 file and a GitHub build-provenance attestation.
  See the README for verification commands.
- The executable is currently unsigned. Code signing depends on obtaining a
  certificate or an accepted open-source signing program; it is tracked as a
  release-hardening task.
