# Security Policy

## AndroidRecovery Security

AndroidRecovery is designed as a read-only-first Windows application for authorized Android device acquisition, analysis, evidence management, and recovery/export.

Security, privacy, evidence integrity, and protection of user data are core project principles.

## Supported Versions

AndroidRecovery is currently under active development.

| Version            | Supported    |
| ------------------ | ------------ |
| Development / main | Yes          |
| Older releases     | Case-by-case |

Security support for released versions will be documented as stable releases are published.

## Security Principles

AndroidRecovery is designed to:

* Operate only on devices for which the user has appropriate authorization.
* Prefer read-only device operations.
* Preserve acquired evidence and provenance.
* Verify evidence integrity using cryptographic hashes where applicable.
* Avoid modifying source-device user data.
* Protect sensitive information from unnecessary logging.
* Validate external process execution and input.
* Handle cancellation and device disconnection safely.
* Prevent unauthorized cross-session or cross-evidence access.
* Fail safely when authorization or required resources are unavailable.

## Explicit Security Boundaries

AndroidRecovery does **not** provide functionality intended to:

* Bypass Android lock screens.
* Bypass Factory Reset Protection (FRP).
* Extract credentials, passwords, PINs, or authentication secrets.
* Bypass Android encryption.
* Exploit vulnerabilities to obtain unauthorized privileges.
* Root devices through exploit techniques.
* Unlock bootloaders.
* Disable Android security mechanisms.
* Modify, delete, or conceal evidence on source devices.
* Perform stealth acquisition without appropriate authorization.

These boundaries are part of the project's security model.

## Reporting a Security Vulnerability

Please do **not** publicly disclose security vulnerabilities through GitHub Issues, pull requests, discussions, or other public channels.

When reporting a vulnerability, provide enough information to reproduce and understand the issue safely.

Where possible, include:

* A clear description of the vulnerability.
* Affected component or project.
* Affected version or commit.
* Steps to reproduce.
* Expected behavior.
* Actual behavior.
* Security impact.
* Relevant logs or screenshots after removing sensitive information.
* A suggested mitigation, if known.

Do not include:

* Personal data.
* Device credentials.
* Authentication secrets.
* Private keys.
* Real user evidence.
* Unredacted forensic data.

## Responsible Disclosure

Security reports will be reviewed and investigated responsibly.

Please allow maintainers reasonable time to:

1. Validate the report.
2. Determine affected components.
3. Develop and test a fix.
4. Assess whether other versions are affected.
5. Publish appropriate security information.

Public disclosure should be coordinated where practical.

## Evidence and Privacy

AndroidRecovery may process sensitive information obtained from an authorized Android device.

Users and contributors must treat acquired evidence as potentially sensitive.

Do not commit the following to the repository:

* Device backups.
* Evidence packages.
* Personal photographs or videos.
* Messages or documents containing personal information.
* Authentication credentials.
* Private keys.
* Production logs containing sensitive information.
* Database files containing personal information.

Local evidence, logs, generated recovery data, and other sensitive artifacts should remain outside version control.

## Dependency and Supply-Chain Security

Contributors should:

* Use trusted package sources.
* Avoid unnecessary dependencies.
* Keep dependencies reasonably up to date.
* Review security implications when adding dependencies.
* Avoid committing downloaded third-party binaries unless their inclusion is explicitly documented and legally appropriate.

Android Platform Tools binaries are intentionally excluded from the repository.

## Native Code and External Processes

Changes involving native code, ADB, external processes, file-system operations, or device communication require additional security consideration.

Contributors should:

* Validate inputs.
* Avoid shell injection vulnerabilities.
* Prefer argument-safe process execution.
* Use explicit executable paths where appropriate.
* Apply appropriate timeouts and cancellation.
* Avoid unnecessary elevated privileges.
* Handle unexpected process termination safely.

## Security Testing

Security-sensitive changes should include appropriate automated tests where practical.

Examples include:

* Authorization checks.
* Evidence integrity verification.
* Path traversal prevention.
* Unsafe file-name handling.
* Process argument validation.
* Cancellation behavior.
* Device disconnect handling.
* Evidence isolation.
* Sensitive-data logging checks.

## Security Updates

Security-related fixes may be documented through:

* GitHub Security Advisories.
* Release notes.
* `CHANGELOG.md`.
* Relevant project documentation.

The project will provide additional security contact and disclosure information as the project matures.

## Maintainer

AndroidRecovery was created and is currently maintained by **Dinesh Ezhumalai**.

The project may be transferred to another maintainer or organization in the future.
