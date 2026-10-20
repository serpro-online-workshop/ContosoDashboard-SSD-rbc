<!--
Sync Impact Report
Version change: unversioned template -> 1.0.0 (initial constitution)
Modified principles: five template placeholders -> five ContosoDashboard principles
Added sections: Security and Technical Constraints; Development Workflow and Quality Gates
Removed sections: none
Follow-up TODO: record the project owner's formal ratification date
-->
# ContosoDashboard Constitution

## Core Principles

### I. Spec-Driven Delivery
Every new feature or material behavior change MUST have an approved feature specification before implementation. The specification MUST define user-facing behavior and testable acceptance criteria; the implementation plan and tasks MUST trace work back to those requirements. Changes to existing behavior MUST identify compatibility impact.

### II. Offline-First Training
Core application workflows MUST work without cloud services or an internet connection. Local persistence MUST use SQLite, and local file storage MUST remain on the filesystem. Cloud integrations MUST be isolated behind interfaces and dependency-injection configuration so training workflows do not acquire cloud SDK or service requirements.

### III. Authorization at Service Boundaries
Every protected operation MUST enforce authorization in its service or endpoint boundary using the authenticated user's identity, role, ownership, and applicable project membership. Hiding a control in the UI MUST NOT be treated as authorization. The mock cookie authentication is for training only and MUST NOT be described as production identity or security.

### IV. Secure Data and File Handling
Local database files, uploaded documents, and other user data MUST remain outside source control. Uploaded files MUST be stored outside `wwwroot`, use generated unique paths, and never use user-supplied names as filesystem paths. Uploads MUST enforce the documented size and type limits, perform malware scanning before making files available, and require authorization for download, replacement, sharing, and deletion. Failed persistence MUST NOT leave a database record pointing to a missing file.

### V. Verifiable, Maintainable Changes
Changes MUST preserve the existing .NET 8, ASP.NET Core, Blazor Server, and Entity Framework Core architecture unless an approved specification requires otherwise. New behavior MUST have focused automated coverage; authorization, persistence, and file-handling changes MUST include failure-path coverage. Every change MUST build, and user-visible changes MUST be checked against their acceptance criteria. New dependencies and abstractions MUST be justified by a concrete requirement.

## Security and Technical Constraints

- ContosoDashboard is a fictional training application, is not actively maintained, and MUST NOT be represented as production-ready.
- The training runtime MUST use the existing mock authentication model, SQLite, and local filesystem storage; it MUST NOT require Azure resources or other external services.
- The document feature MUST preserve integer identifiers for documents and store category values as text, consistent with the stakeholder requirements.
- Document uploads MUST enforce the 25 MB per-file limit and the stakeholder-defined file type allowlist. A file MUST NOT be presented as malware-scanned unless a scanner reports it clean; scanner failure MUST prevent the file from being made available.
- Document metadata and file storage MUST be coordinated so failures do not expose unauthorized files or leave metadata referencing unavailable content. Storage implementations MUST be accessed through `IFileStorageService` so a future provider can be substituted without moving storage concerns into UI or business logic.
- EF Core schema changes MUST include a documented database upgrade or clean-reset path. `EnsureCreated()` is for the training database and MUST NOT be treated as a production migration strategy.

## Development Workflow and Quality Gates

- Feature work MUST follow the Spec Kit sequence: specification, clarification when needed, technical plan, actionable tasks, implementation, and acceptance verification. Required artifacts MUST remain consistent with one another.
- Before completion, contributors MUST run `dotnet build ContosoDashboard/ContosoDashboard.csproj` and the relevant automated tests. When an interaction cannot be covered automatically, contributors MUST record and perform reproducible manual acceptance checks.
- Reviews MUST verify authorization boundaries, data handling, error paths, and compliance with this constitution. Deviations MUST be documented with rationale and approved by the project owner.
- Documentation MUST be updated when setup, configuration, persistence, or observable behavior changes. Local database files, uploads, credentials, and machine-specific settings MUST NOT be committed.

## Governance

This constitution governs feature specifications, plans, tasks, implementation, and review. Amendments MUST be proposed with their rationale and impact, reviewed and approved by the project owner, and reflected in the version and amendment date. Compliance MUST be checked during specification review and code review; any exception MUST be explicit, scoped, and approved before implementation.

Constitution versions follow semantic versioning: MAJOR for incompatible governance changes or removed/redefined principles, MINOR for new principles or materially expanded requirements, and PATCH for clarifications that do not change obligations.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): record the project owner's formal adoption date | **Last Amended**: 2026-10-20
