# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.0.x (latest) | ✅ |
| < 1.0.6 | ⚠️ Upgrade recommended (see CHANGELOG for hardening fixes) |

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's private
vulnerability reporting ("Security" tab → "Report a vulnerability") on the Nilog repository.
Include the affected version, a minimal reproduction and the impact you expect. You can expect
an acknowledgement within 7 days.

## Scope and responsibilities

Nilog is a call-site extension layer over `Microsoft.Extensions.Logging`.

| Concern | Owner |
|---------|-------|
| Bounded template cache, bounded exception reports, isolation of throwing `ToString()` | Nilog |
| Redaction of secrets/PII in arguments and exception messages | The application |
| Log-forging protection (newline/control characters in values), encoding, retention | The provider / sink |
| Exceptions thrown by providers' `Log` implementation | The provider (they propagate) |
| Templates must be compile-time constants; never build templates from untrusted input | The application (analyzer `NILOG001`/`NILOG003` helps) |

`Exception.Data` is intentionally not rendered in exception reports.

## Supply chain

- GitHub Actions are pinned to commit SHAs; workflows default to read-only permissions.
- NuGet restore uses lock files (`packages.lock.json`) with `--locked-mode` in CI.
- Package signing and SBOM publication are release-time procedures that require maintainer
  credentials; they are not performed by the repository automatically.
