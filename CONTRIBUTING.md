# Contributing to AndroidRecovery

Thank you for your interest in contributing to AndroidRecovery.

AndroidRecovery is an open-source Windows application for authorized Android device acquisition, analysis, evidence management, and recovery/export.

## Before Contributing

Please read:

* `README.md`
* `SECURITY.md`
* `ROADMAP.md` when available

Contributions should preserve the project's read-only-first and evidence-integrity principles.

## Development Principles

Contributors should:

* Prefer read-only device operations.
* Preserve evidence integrity and provenance.
* Avoid modifying user data on connected Android devices.
* Use explicit authorization checks.
* Keep acquisition operations cancellable.
* Avoid bypassing Android security controls.
* Avoid credential extraction or authentication bypass.
* Avoid FRP or lock-screen bypass.
* Avoid privilege escalation or root exploits.
* Keep sensitive information out of logs and commits.
* Add tests for new functionality where practical.

## Development Setup

1. Clone the repository.
2. Open `AndroidRecovery.sln`.
3. Install the required .NET SDK and Windows development workload.
4. Place trusted Android Platform Tools under:

```text
tools/platform-tools/
```

The repository intentionally does not commit Android Platform Tools binaries.

## Building

Build the solution with:

```cmd
dotnet build AndroidRecovery.sln -c Debug -p:AppxGeneratePriEnabled=false
```

## Testing

Run the available test projects with:

```cmd
dotnet test AndroidRecovery.sln
```

Follow the project documentation for real-device testing.

## Pull Requests

Before opening a pull request:

* Build the affected projects.
* Run relevant tests.
* Confirm no generated binaries or local evidence are included.
* Confirm no secrets or personal data are included.
* Update documentation when behavior changes.
* Explain important architectural or security implications.

Pull requests should describe:

* What changed.
* Why it changed.
* How it was tested.
* Any limitations or known issues.

## Commit Messages

Prefer clear, descriptive commit messages.

Examples:

```text
feat: add evidence package verification
fix: prevent unauthorized device acquisition
test: add acquisition cancellation coverage
docs: update device testing guide
refactor: separate analysis providers
```

## Security-Sensitive Changes

Changes involving:

* Android device communication
* acquisition
* evidence integrity
* hashing
* authorization
* file export
* native code
* process execution
* permissions

should receive additional review and include appropriate tests.

Security vulnerabilities should not be disclosed through public GitHub issues. See `SECURITY.md`.

## Code Ownership

AndroidRecovery was created and is currently maintained by **Dinesh Ezhumalai**.

Contributors retain appropriate attribution for their work through Git history and project documentation.

The project may be transferred to another maintainer or organization in the future.
