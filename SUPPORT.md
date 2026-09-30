# Support

Thank you for using AndroidRecovery.

AndroidRecovery is an open-source project under active development. Support information and documentation will evolve as the project matures.

## Before Asking for Help

Please check the following resources first:

* `README.md`
* `CONTRIBUTING.md`
* `SECURITY.md`
* `ROADMAP.md` when available
* Documentation under `docs/` when available

Also search existing GitHub Issues and Discussions for similar problems.

## Bug Reports

If you believe you have found a reproducible software defect, create a GitHub Issue using the appropriate issue template when available.

Include:

* AndroidRecovery version or commit.
* Windows version.
* .NET SDK/runtime version.
* Device manufacturer and model, when relevant.
* Android version, when relevant.
* Steps to reproduce.
* Expected behavior.
* Actual behavior.
* Relevant error messages.
* Relevant logs after removing sensitive information.

Do not attach private evidence, credentials, personal files, or other sensitive information.

## Device Compatibility Issues

Android device behavior can vary significantly between manufacturers, Android versions, security configurations, and USB/ADB configurations.

When reporting a device compatibility issue, provide:

* Device manufacturer.
* Device model.
* Android version.
* ADB authorization state.
* Whether USB debugging is enabled.
* Whether the device is detected by trusted Android Platform Tools.
* The AndroidRecovery operation that failed.
* Relevant sanitized logs.

Never include private device data or evidence unless it has been appropriately sanitized.

## Feature Requests

Feature requests are welcome.

Before submitting one:

1. Search existing issues and discussions.
2. Check the project roadmap.
3. Explain the problem or use case.
4. Describe the proposed behavior.
5. Explain any security, privacy, or evidence-integrity considerations.

Feature requests involving Android security bypass, credential extraction, unauthorized access, or similar functionality may fall outside the project's security boundaries.

## Documentation Issues

If documentation is incorrect, incomplete, or difficult to understand, please report the problem or submit a pull request with an improvement.

Useful documentation contributions include:

* Setup instructions.
* Troubleshooting guides.
* Device compatibility information.
* Architecture documentation.
* Testing instructions.
* User guides.
* Developer documentation.

## Security Issues

Do **not** report security vulnerabilities through public GitHub Issues or Discussions.

Follow the process described in:

```text
SECURITY.md
```

## Community Discussions

Use GitHub Discussions, when enabled, for questions and broader project conversations.

Examples include:

* General usage questions.
* Development questions.
* Architecture discussions.
* Ideas for future improvements.
* Device compatibility experiences.

Keep discussions respectful and avoid sharing private or sensitive information.

## Real-Device Testing

AndroidRecovery can interact with real Android devices.

Only test with devices and data for which you have appropriate authorization.

When reporting test results, prefer sanitized information such as:

```text
Device: Example Manufacturer Example Model
Android: Example Version
ADB: Authorized
Operation: Read-only acquisition
Result: Successful
```

Do not publish:

* Personal photographs.
* Private documents.
* Messages.
* Contacts.
* Authentication information.
* Unredacted evidence packages.
* Private device identifiers.

## Response Expectations

AndroidRecovery is maintained as an open-source project and may not provide immediate responses to every question or issue.

Community contributions, reproducible reports, documentation improvements, and tested fixes are highly appreciated.

## Maintainer

AndroidRecovery was created and is currently maintained by **Dinesh Ezhumalai**.

The project may be transferred to another maintainer or organization in the future.
