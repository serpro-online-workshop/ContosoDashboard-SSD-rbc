# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add secure document upload and management to ContosoDashboard. Employees can upload PDF, Office, text, and image files (≤ 25 MB) with a title, a category, an optional project, and tags. They can browse and filter My Documents, search the documents they can access, download and preview documents, and edit, replace, delete, and share them. Project and task pages show their documents, users get notifications, and Administrators get audit reports.

The feature extends the existing ASP.NET Core 8 Blazor Server application in place:

- **Data**: new EF Core entities (`Document`, `DocumentTag`, `DocumentShare`, `TaskDocument`, `DocumentActivity`) in the existing SQLite database.
- **Storage**: files go to the local filesystem outside `wwwroot`, behind `IFileStorageService`.
- **Scanning**: every file is scanned through `IMalwareScanner` (a local ClamAV daemon) before storage. A failed or incomplete scan rejects the file.
- **Authorization**: a single `DocumentService` enforces all authorization rules.
- **File access**: files are served only through two authorized minimal-API endpoints, one for download and one for preview.
- **Uploads**: uploads run over the Blazor circuit, using chunked copy with progress reporting.

See [research.md](research.md) for the decisions behind each choice.

## Technical Context

**Language/Version**: C# 12 on .NET 8 (`net8.0`), ASP.NET Core 8 Blazor Server (existing project)  
**Primary Dependencies**: Existing: Microsoft.EntityFrameworkCore.Sqlite 8.0.0, Bootstrap 5.3. New runtime packages: none (ClamAV is reached through its documented clamd socket protocol). New test-only packages: xunit 2.x, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing 8.0.x  
**Storage**: SQLite `ContosoDashboard.db` for metadata (EF Core, `EnsureCreated`); local filesystem `ContosoDashboard/AppData/uploads/{userId}/{projectId|personal}/{guid}.{ext}` for file content  
**Testing**: New `ContosoDashboard.Tests` xUnit project: service tests on in-memory SQLite, storage tests on a temp directory, a fake `IMalwareScanner`, and endpoint tests with `WebApplicationFactory` plus a test authentication handler  
**Target Platform**: Kestrel on a developer workstation (Windows, Linux, macOS), modern desktop browsers  
**Project Type**: Web application: a single server-rendered ASP.NET Core project plus a test project  
**Performance Goals**: Upload ≤ 30 s for 25 MB; document lists ≤ 2 s at 500 documents; search ≤ 2 s; preview ≤ 3 s (SC-002…SC-005)  
**Constraints**: Fully offline (no cloud SDKs or services); 25 MB per-file limit; type allowlist with content-signature check; scanning that rejects files on any scan failure; files never under `wwwroot`; integer document IDs; text category values; MIME type up to 255 characters; no orphaned metadata when a save fails  
**Scale/Scope**: Designed for 5,000 employees with role-based access; training data has 4 seeded users and 1 project. The feature adds about 8 pages/components, 5 entities, 3 services, and 2 HTTP endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Constitution v1.0.0 ([.specify/memory/constitution.md](../../.specify/memory/constitution.md)).

| # | Principle / Constraint | Pre-research | Post-design | Evidence |
|---|---|---|---|---|
| I | Spec-Driven Delivery | PASS | PASS | A spec with testable acceptance scenarios exists, and the plan and design artifacts trace to FR-001…FR-036. The spec must move from Draft to approved before `/speckit.implement`. |
| II | Offline-First Training | PASS | PASS | SQLite plus local filesystem. ClamAV runs as a local daemon with a local signature database; the one-time signature download is documented in [quickstart.md](quickstart.md). No cloud SDKs. Storage sits behind `IFileStorageService`, registered via DI. |
| III | Authorization at Service Boundaries | PASS | PASS | `DocumentService` evaluates the [authorization matrix](data-model.md#authorization-matrix) on every call, using role and department read from the database. Endpoints return 404 for unauthorized or missing documents. Hiding controls in the UI is cosmetic only. Mock cookie auth is unchanged. |
| IV | Secure Data and File Handling | PASS | PASS | Files live under `AppData/uploads` (outside `wwwroot`, git-ignored) with GUID storage names. The original name is never used in paths. Uploads enforce the 25 MB limit, the type allowlist, and a signature check, and any scan failure rejects the file. The path is generated before the insert, and the file is written before the DB record with a compensating delete. |
| V | Verifiable, Maintainable Changes | PASS (with addition) | PASS | The stack is unchanged. The repo has no tests today, so the plan adds a test project (justified below). Failure-path tests cover authorization, persistence, and file handling. Build, tests, and quickstart scenarios form the completion gate. |
| — | Integer `DocumentId`, text `Category`, 255-char MIME | PASS | PASS | See [data-model.md](data-model.md#document). |
| — | Schema change upgrade/reset path | PASS | PASS | `EnsureCreated` does not alter an existing database, so a documented clean reset of the database and `AppData/uploads` is required ([research.md §10](research.md#10-schema-evolution-for-the-training-database), [quickstart.md](quickstart.md#1-reset-the-training-database)). |

No violations. Complexity Tracking records the additions that require justification.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Entities, validation, authorization matrix, state transitions
├── quickstart.md        # Setup and end-to-end validation scenarios
├── contracts/
│   ├── http-endpoints.md      # Download/preview endpoints
│   ├── service-interfaces.md  # IFileStorageService, IMalwareScanner, IDocumentService, IDocumentActivityService
│   └── ui-routes.md           # Pages, components, navigation
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs                    # NEW Document, DocumentCategories
│   ├── DocumentTag.cs                 # NEW
│   ├── DocumentShare.cs               # NEW
│   ├── TaskDocument.cs                # NEW
│   ├── DocumentActivity.cs            # NEW DocumentActivity, DocumentActions
│   └── Notification.cs                # EDIT add DocumentShared, DocumentAdded
├── Data/
│   └── ApplicationDbContext.cs        # EDIT DbSets, relationships, indexes
├── Services/
│   ├── Storage/
│   │   ├── IFileStorageService.cs     # NEW
│   │   ├── LocalFileStorageService.cs # NEW
│   │   └── FileStorageOptions.cs      # NEW
│   ├── Scanning/
│   │   ├── IMalwareScanner.cs         # NEW
│   │   ├── ClamAvMalwareScanner.cs    # NEW
│   │   └── MalwareScanningOptions.cs  # NEW
│   ├── DocumentValidator.cs           # NEW size, extension, signature, MIME mapping
│   ├── DocumentAccessPolicy.cs        # NEW authorization matrix evaluation + readable-documents filter
│   ├── DocumentService.cs             # NEW IDocumentService (enforces DocumentAccessPolicy)
│   ├── DocumentActivityService.cs     # NEW IDocumentActivityService + reports
│   └── DashboardService.cs            # EDIT document count, recent documents
├── Endpoints/
│   └── DocumentEndpoints.cs           # NEW GET download/preview
├── Pages/
│   ├── Documents.razor                # NEW /documents (My Documents + upload)
│   ├── DocumentDetails.razor          # NEW /documents/{id} (edit, replace, share, delete)
│   ├── SharedDocuments.razor          # NEW /documents/shared
│   ├── DocumentSearch.razor           # NEW /documents/search
│   ├── DocumentReports.razor          # NEW /documents/reports (Administrator)
│   ├── TaskDetails.razor              # NEW /tasks/{id} (task info + documents)
│   ├── Tasks.razor                    # EDIT ViewTaskDetails navigates to /tasks/{id}
│   ├── ProjectDetails.razor           # EDIT project documents section
│   ├── Index.razor                    # EDIT document count card, Recent Documents widget
│   └── Login.cshtml.cs                # EDIT add Department claim
├── Shared/
│   ├── NavMenu.razor                  # EDIT Documents, Shared with Me (+ Reports for admins)
│   └── Documents/
│       ├── DocumentUploadDialog.razor # NEW multi-file upload with progress
│       ├── DocumentTable.razor        # NEW reusable list (sort/actions)
│       └── DocumentAttachDialog.razor # NEW attach existing document to task
├── Program.cs                         # EDIT DI, options, MapDocumentEndpoints
└── appsettings.json                   # EDIT DocumentStorage, MalwareScanning sections

ContosoDashboard.Tests/                # NEW xUnit project
├── ContosoDashboard.Tests.csproj
├── TestInfrastructure/                # SQLite in-memory context, fake scanner, temp storage, test auth handler
├── Services/                          # DocumentService, validator, activity/report, dashboard tests
├── Storage/                           # LocalFileStorageService tests
├── Endpoints/                         # Download/preview authorization tests
└── Performance/                       # 500-document list/search timing (Category=Performance)

.gitignore                             # EDIT ignore ContosoDashboard/AppData/
```

**Structure Decision**: Keep the existing single-project layout (`Models`/`Data`/`Services`/`Pages`/`Shared`) and add feature files next to their peers. The infrastructure abstractions get `Services/Storage` and `Services/Scanning` subfolders, so a future Azure implementation needs only a DI swap. A sibling `ContosoDashboard.Tests` project is added because the repository has no tests. It is run directly by project path, since `*.sln` is git-ignored.

## Complexity Tracking

> Additions that need justification under Constitution Principle V (no violations).

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| `ContosoDashboard.Tests` project + xUnit/Mvc.Testing packages | Principle V requires automated coverage, including failure paths, for authorization, persistence, and file handling; none exists today | Manual testing alone cannot repeatably prove IDOR protection, orphan prevention, or that scan failures reject files |
| `IMalwareScanner` + local ClamAV daemon | FR-010 and the constitution require real scanning that rejects files whenever a scan fails, while staying offline | A stub or signature-only checker would present files as scanned when they are not, violating Principle IV |
| Two minimal-API endpoints | Blazor Server cannot stream file responses, and files outside `wwwroot` need an authorized HTTP path (FR-033) | Static files would bypass authorization; MVC controllers add more ceremony for two GET routes |
| `TaskDetails.razor` page | FR-028 needs a task detail view, and `ViewTaskDetails` is currently a TODO | A modal in `Tasks.razor` would mix upload/attach workflows into the list page |
