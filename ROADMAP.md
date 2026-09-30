# AndroidRecovery Roadmap

AndroidRecovery is under active development.

The roadmap describes the planned evolution of the project. Features and priorities may change as development, testing, security review, and community feedback progress.

## Current Status

**Development in Progress**

The current implementation provides the foundation for:

* Authorized Android device discovery through ADB.
* Read-only device interaction.
* Device authorization-state handling.
* Category-based acquisition.
* Evidence package creation.
* Evidence metadata and provenance.
* SHA-256 hashing and verification.
* Acquisition progress and cancellation.
* Device disconnect handling.
* Evidence analysis.
* Signature-based file classification.
* Search and basic result filtering.
* Evidence integrity verification.
* Recovery/export foundation.
* Automated unit and integration testing.
* Windows desktop UI using WinUI 3.

## Phase 1 — Project Foundation

**Status: Completed**

* [x] .NET solution structure.
* [x] WinUI desktop application foundation.
* [x] MVVM architecture.
* [x] Dependency injection.
* [x] Configuration infrastructure.
* [x] Structured application logging.
* [x] ADB process execution infrastructure.
* [x] Authorized-device detection.
* [x] Unauthorized/offline device handling.
* [x] Automated test foundation.

## Phase 2 — Read-Only Acquisition

**Status: Completed / Stabilizing**

* [x] Acquisition provider abstraction.
* [x] ADB acquisition provider.
* [x] Read-only acquisition.
* [x] Category selection.
* [x] Acquisition setup.
* [x] Free-space preflight.
* [x] Streaming file acquisition.
* [x] SHA-256 hashing.
* [x] Progress reporting.
* [x] Cancellation.
* [x] Device disconnect handling.
* [x] Acquisition logging.
* [x] Acquisition report generation.

Supported acquisition categories currently include:

* Photos
* Videos
* Audio
* Documents
* Downloads
* DCIM
* Pictures
* Movies
* Music

## Phase 3 — Evidence Packages

**Status: Completed / Stabilizing**

* [x] Evidence package structure.
* [x] Metadata.
* [x] File manifest.
* [x] SQLite evidence database.
* [x] Acquisition log.
* [x] Acquisition report.
* [x] Acquired files.
* [x] SHA-256 file hashes.
* [x] Evidence verification.
* [x] Provenance tracking.
* [x] Tamper-detection checks.
* [x] Incomplete/invalid evidence handling.

The evidence format will continue to evolve toward a documented and versioned format.

## Phase 4 — Evidence Analysis

**Status: In Progress**

* [x] Evidence verification before analysis.
* [x] File enumeration.
* [x] Signature-based file classification.
* [x] File provenance.
* [x] Analysis results.
* [x] Basic search.
* [x] Name/path/type/format filtering.
* [x] Basic result sorting.
* [ ] Advanced filtering.
* [ ] Advanced sorting.
* [ ] Confidence-based classification.
* [ ] Indexed search improvements.
* [ ] Image analysis.
* [ ] Video analysis.
* [ ] Audio analysis.
* [ ] Document analysis.
* [ ] Database analysis.

## Phase 5 — Preview

**Status: Planned / In Progress**

* [ ] Image preview.
* [ ] Document preview.
* [ ] Audio preview.
* [ ] Video preview.
* [ ] Safe preview isolation.
* [ ] Preview size limits.
* [ ] Unsupported-format handling.
* [ ] Preview metadata.
* [ ] Preview performance improvements.

## Phase 6 — Recovery and Export

**Status: In Progress**

* [ ] Result selection.
* [ ] Select-all / clear-selection.
* [ ] Selective recovery/export.
* [ ] Destination selection.
* [ ] Destination safety checks.
* [ ] Path traversal protection.
* [ ] Duplicate-file handling.
* [ ] Streaming export.
* [ ] Export progress.
* [ ] Export cancellation.
* [ ] Export verification.
* [ ] SHA-256 verification after export.
* [ ] Recovery/export report.
* [ ] Recovery history.

Recovery/export is intended to export legitimately acquired data from verified evidence packages.

It is not intended to provide deleted-file recovery, security bypass, credential extraction, or unauthorized access.

## Phase 7 — Professional Evidence Workflow

**Status: Planned**

* [ ] Complete evidence browser.
* [ ] Evidence package history.
* [ ] Evidence comparison.
* [ ] Advanced provenance visualization.
* [ ] Evidence validation reports.
* [ ] Acquisition reports.
* [ ] Analysis reports.
* [ ] Recovery/export reports.
* [ ] Report export.
* [ ] Improved evidence metadata.
* [ ] Versioned evidence schema.
* [ ] Evidence format documentation.

## Phase 8 — Device Provider Expansion

**Status: Planned**

The architecture is intended to support multiple acquisition providers.

Planned providers include:

* [x] ADB provider.
* [ ] MTP provider.
* [ ] Additional authorized device providers where technically and legally appropriate.

Provider implementations must preserve the project's authorization, read-only, privacy, and evidence-integrity principles.

## Phase 9 — User Experience

**Status: In Progress**

* [x] Home screen.
* [x] Recovery workflow navigation.
* [x] Category selection.
* [x] Device selection.
* [x] Acquisition setup.
* [x] Acquisition progress.
* [x] Evidence workflow.
* [x] Results workflow foundation.
* [ ] Complete preview experience.
* [ ] Complete recovery/export experience.
* [ ] Improved error presentation.
* [ ] Full keyboard navigation.
* [ ] Screen-reader accessibility.
* [ ] High-contrast support.
* [ ] Responsive layouts.
* [ ] Additional usability testing.

## Phase 10 — Testing and Reliability

**Status: In Progress**

* [x] Unit tests.
* [x] Integration tests.
* [x] Acquisition tests.
* [x] Analysis tests.
* [x] Recovery tests.
* [x] Evidence verification tests.
* [x] Cancellation tests.
* [x] Tenant/session-style isolation checks where applicable.
* [ ] Larger real-world datasets.
* [ ] Extended real-device compatibility testing.
* [ ] Performance benchmarking.
* [ ] Memory-usage testing.
* [ ] Long-running acquisition testing.
* [ ] Failure-injection testing.
* [ ] Additional Windows environment testing.

## Phase 11 — Developer Experience

**Status: In Progress**

* [x] Repository documentation foundation.
* [x] Contribution guidelines.
* [x] Security policy.
* [x] Code of Conduct.
* [x] Support documentation.
* [ ] Architecture documentation.
* [ ] Acquisition architecture documentation.
* [ ] Evidence format specification.
* [ ] Developer setup guide.
* [ ] Real-device testing guide.
* [ ] Architecture Decision Records.
* [ ] Automated CI.
* [ ] Automated security scanning.
* [ ] Automated release builds.

## Phase 12 — Release Engineering

**Status: Planned**

* [ ] Versioning strategy.
* [ ] Release build pipeline.
* [ ] Signed Windows packages.
* [ ] Release artifacts.
* [ ] Release notes.
* [ ] Upgrade documentation.
* [ ] Automated GitHub releases.
* [ ] Stable release channel.
* [ ] Development release channel.

## Future Possibilities

The following are longer-term possibilities and are not commitments:

* Cross-platform analysis components.
* Command-line interface.
* Automated evidence inspection.
* Additional file-format analyzers.
* Advanced media metadata extraction.
* Plugin/provider architecture.
* Additional Android acquisition mechanisms.
* Automated compatibility reporting.
* Additional forensic-style evidence documentation.

Any future feature must remain consistent with the project's authorization, privacy, security, and evidence-integrity principles.

## Explicitly Out of Scope

AndroidRecovery is not intended to become a security-bypass or unauthorized-access tool.

The project will not intentionally provide functionality for:

* Lock-screen bypass.
* FRP bypass.
* Credential extraction.
* Password/PIN extraction.
* Encryption bypass.
* Root exploits.
* Privilege escalation.
* Bootloader-unlock tooling.
* Unauthorized device access.
* Stealth acquisition.
* Destructive device modification.
* Evidence destruction or concealment.

## Roadmap Philosophy

The project prioritizes:

1. Security and authorization.
2. Evidence integrity.
3. Data safety.
4. Reproducibility.
5. Testability.
6. Device compatibility.
7. Performance.
8. User experience.

Roadmap items may be reordered as technical requirements, security considerations, testing results, and community contributions evolve.

## Maintainer

AndroidRecovery was created and is currently maintained by **Dinesh Ezhumalai**.

The project may be transferred to another maintainer or organization in the future.

