# Research: Document Upload and Management

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-10-20

The Technical Context has no `NEEDS CLARIFICATION` items. This document records each design decision, its rationale, and the alternatives considered. It is grounded in the existing codebase: Blazor Server with `ServerPrerendered`, cookie mock auth, EF Core SQLite with `EnsureCreated`, services that check authorization by user ID, no HTTP endpoints, and no test project.

## 1. Upload transport in Blazor Server

- **Decision**: Use `InputFile` with `multiple`. Files in a selection are processed **one at a time**. Before opening each stream, copy the name, size, and content type into locals. Then call `OpenReadStream(maxAllowedSize: 25 MB)` and copy into a `MemoryStream` in 80 KB chunks, updating a per-file progress bar after each chunk. Clear the `IBrowserFile` reference, and re-render `InputFile` with a new `@key` after each batch.
- **Rationale**: This follows the stakeholder Blazor guidance and avoids stream-disposal errors. Processing sequentially caps server memory at about 25 MB per circuit. `InputFile` streams over SignalR in chunks, so the default `MaximumReceiveMessageSize` does not need to change. The copy loop gives real byte-level progress (FR-009).
- **Alternatives considered**: An HTTP multipart POST endpoint would need antiforgery wiring and a JS client; Blazor-side progress is simpler. Copying to a temp file instead of memory adds cleanup paths and has no benefit at 25 MB. Parallel processing of all selected files allows unbounded memory per user.

## 2. File storage abstraction and layout

- **Decision**: Define `IFileStorageService` with `UploadAsync`, `DeleteAsync`, `DownloadAsync`, `GetUrlAsync`. The training implementation is `LocalFileStorageService`, rooted at a configurable `DocumentStorage:RootPath` (default `AppData/uploads`, relative to the content root). The relative storage key is `{userId}/{projectId|personal}/{guid}{ext}`. The extension comes from the validated allowlist, never from user input, and the key is generated **before** any DB insert. Every resolved full path must stay inside the root (`Path.GetFullPath` prefix check). `GetUrlAsync` throws `NotSupportedException` locally: it is reserved for future SAS URLs, and no training code path calls it.
- **Rationale**: Meets FR-033 and the constitution's storage rules. The same key works as an Azure blob name, so migration only changes the DI registration. GUID names prevent path traversal and collisions. The DB stores only the relative key, which keeps it portable.
- **Alternatives considered**: Storing files in `wwwroot` exposes them without authorization. Storing BLOBs in SQLite bloats the DB and conflicts with the planned blob-storage migration. Content-hash names would deduplicate files, but they break the per-document replace and delete semantics.

## 3. Malware scanning (offline, fails closed)

- **Decision**: `IMalwareScanner.ScanAsync(Stream)` returns `Clean`, `Infected(signature)`, or `Error(reason)`. The training implementation is `ClamAvMalwareScanner`, which streams the bytes to a **local** `clamd` daemon over the documented `zINSTREAM` TCP protocol (`MalwareScanning:Host` default `127.0.0.1`, `Port` 3310, 30 s timeout, 64 KB chunks). Any result other than `Clean`, including a daemon that is unreachable or times out, rejects the file. No `IsScanned` flag exists, because only clean files are ever stored. The quickstart covers installing ClamAV (Linux/macOS packages, the Windows installer, or the `clamav/clamav` container) and running `freshclam` once. The EICAR test string is used for validation.
- **Rationale**: FR-010 and constitution §Security require real scanning and say a file must never be presented as scanned when it was not. ClamAV is free, cross-platform, and runs entirely offline after the signature download. The clamd protocol is a few dozen lines of socket code, so no NuGet dependency is needed.
- **Alternatives considered**:
  - Windows Defender `MpCmdRun` / AMSI: Windows-only.
  - A no-op scanner or a "disabled" development mode: it would present files as scanned when they are not, which violates Principle IV.
  - A cloud scanning API: violates the offline requirement.
  - Asynchronous post-upload scanning with quarantine: adds status states and background processing that the spec does not need. Synchronous scanning of ≤ 25 MB fits inside the 30 s target.
- **Risk**: Workstations without ClamAV cannot upload. This is intended, and the quickstart makes the requirement explicit. Automated tests use a fake scanner.

## 4. File type validation

- **Decision**: `DocumentValidator` performs three checks:
  - **Extension allowlist**: `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`.
  - **Content signature** that must match the extension:
    - `%PDF-` for PDF
    - `FF D8 FF` for JPEG
    - `89 50 4E 47 0D 0A 1A 0A` for PNG
    - `50 4B 03 04` (ZIP/OOXML) for `.docx/.xlsx/.pptx`
    - `D0 CF 11 E0 A1 B1 1A E1` (OLE2) for `.doc/.xls/.ppt`
    - for `.txt`: no NUL bytes and valid UTF-8 in the first 8 KB
  - **Size** must be between 1 byte and 26,214,400 bytes (25 MB) inclusive.

  The stored MIME type comes from a server-side extension → MIME map (for example `application/vnd.openxmlformats-officedocument.wordprocessingml.document`). The browser-supplied content type is ignored.
- **Rationale**: Covers FR-002/FR-003 and the edge cases where the extension and the content disagree. Server-derived MIME types are trustworthy for `Content-Type` on download and preview.
- **Alternatives considered**: Trusting the extension or the browser MIME type alone is easy to spoof. A full content-sniffing library is an extra dependency for 7 formats.

## 5. Authorization model

- **Decision**: All rules live in `DocumentService` and are evaluated per call against the **current DB state** of the acting user (role, department) and the document (uploader, project, shares). Pages and endpoints pass only the authenticated user ID from `ClaimTypes.NameIdentifier`. The rules are in the [authorization matrix](data-model.md#authorization-matrix):
  - a "team" is the set of users with the same `Department`;
  - project access requires `ProjectMember` or `ProjectManagerId`;
  - uploaders keep access to their own documents;
  - direct and team shares grant read-only access;
  - Administrators can read everything.

  Requests the user is not allowed to make return the same result as a missing document (`null` from the service, 404 at endpoints). `Login.cshtml.cs` also gains the `Department` claim for display and future policies; the service does not depend on it.
- **Rationale**: Matches the existing pattern (`TaskService.GetTaskByIdAsync`) and Principle III, and it prevents IDOR (FR-032). Reading role and department from the DB avoids stale cookie claims during the 8-hour sliding session.
- **Alternatives considered**: ASP.NET Core resource-based `IAuthorizationHandler`s would be more ceremony, and the Blazor and endpoint callers would still need the same data. Claims-only checks would be stale after role or membership changes.

## 6. Serving downloads and previews

- **Decision**: Two minimal-API endpoints in `Endpoints/DocumentEndpoints.cs`, mapped with `RequireAuthorization()`:
  - `GET /api/documents/{id:int}/download`: `Content-Disposition: attachment` with the original file name. ASP.NET's `Results.File(..., fileDownloadName)` emits the RFC 6266/5987 encoding, which handles special characters.
  - `GET /api/documents/{id:int}/preview`: PDF, JPEG, and PNG only, served `inline`. Any other type gets 404.

  Both add `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`. Preview also adds a restrictive `Content-Security-Policy: sandbox; default-src 'none'; img-src 'self'; object-src 'self'`. Preview opens in a **new browser tab**, because the app-wide `X-Frame-Options: DENY` header (Program.cs) forbids iframes. Each successful call records a `Downloaded` or `Previewed` activity.
- **Rationale**: Blazor Server cannot return file responses, and the files live outside `wwwroot` (FR-033). Opening a new tab keeps the existing anti-clickjacking header intact while meeting "preview without downloading" (FR-019).
- **Alternatives considered**: MVC controllers need `AddControllers` and more boilerplate for two routes. A JS-interop byte stream into a blob URL pushes 25 MB through SignalR and bypasses HTTP caching and security headers. Relaxing `X-Frame-Options` to `SAMEORIGIN` for an embedded preview would weaken a global security header for convenience.

## 7. Save ordering and consistency

- **Decision**:
  - **Upload**: validate → scan → generate key → `storage.UploadAsync` → DB transaction (insert `Document` + tags + `Uploaded` activity) → commit. If the DB write fails, delete the stored file, then rethrow.
  - **Replace**: validate → scan → upload to a **new** key → DB update (file fields + `Replaced` activity) → commit → best-effort delete of the old key. If the DB update fails, delete the new file.
  - **Delete**: DB transaction removes the document, shares, task links, and tags (cascade) and writes a `Deleted` activity → commit → best-effort file delete, logged on failure.
  - **Notifications**: written after the commit, inside a try/catch with logging.
- **Rationale**: The stakeholder sequence "generate path → save file → save metadata" means a DB record never points to a missing file (FR-012). Writing the new key on replace means a failed replace never corrupts the current file. Any leftover is always an unreferenced file, which is harmless and never a dangling record.
- **Alternatives considered**: DB-first insert followed by the file write risks records without files. A two-phase status column (`Pending`/`Active`) adds states and cleanup jobs for no extra guarantee here.

## 8. Lists, filtering, and search performance

- **Decision**:
  - **Queries**: server-side EF Core with `AsNoTracking` and projection to list DTOs.
  - **Indexes**: `UploadedByUserId`, `ProjectId`, `UploadedDate`, `Category`, `DocumentTag(Tag)`, `DocumentShare(SharedWithUserId)`, `DocumentShare(SharedWithDepartment)`.
  - **Lists**: My Documents and project lists are unpaged up to the specified 500 documents.
  - **Search**: an access-filtered `EF.Functions.Like` over title, description, tag, uploader display name, and project name. The pattern escapes `%`, `_`, and `\`; the query is trimmed and limited to 2–100 characters; results are ordered by `UploadedDate desc` and capped at 100.
  - **Validation**: a seeding script in the tests checks the 2-second targets at 500 documents.
- **Rationale**: SQLite `LIKE` is case-insensitive for ASCII and fast at the expected volumes (thousands of rows). Projection avoids loading navigation graphs, and the cap bounds rendering cost.
- **Alternatives considered**: SQLite FTS5 needs raw SQL, triggers, and a schema outside EF's `EnsureCreated`, which is not justified at this scale. Client-side filtering cannot scale and could leak inaccessible rows into memory.

## 9. Notifications

- **Decision**: Append `DocumentShared` and `DocumentAdded` to `NotificationType` (it is stored as an int, so appending is safe). Use the existing `INotificationService.CreateNotificationAsync`. Recipients:
  - **Share**: the direct recipient, or the department members at share time, excluding the sharer; each user is notified at most once per share action.
  - **Project add**: the project's members and manager, excluding the uploader.

  The notification text includes the document title and, for project adds, the project name. It does not include a link to file content.
- **Rationale**: Reuses the existing notification center (FR-026, FR-031) without new infrastructure.
- **Alternatives considered**: Real-time push (SignalR hub broadcast) is not required by the spec, and email is out of scope (offline).

## 10. Schema evolution for the training database

- **Decision**: Keep `EnsureCreated`. Because it never alters an existing database, the feature ships with a documented **clean reset**: stop the app, then delete `ContosoDashboard/ContosoDashboard.db*` and `ContosoDashboard/AppData/uploads/`. The app recreates and reseeds both on the next start. `ContosoDashboard/AppData/` is added to `.gitignore`.
- **Rationale**: The constitution requires an upgrade or clean-reset path and states that `EnsureCreated` is not a production migration strategy. Migrations would change the established project pattern for a training app with only seed data.
- **Alternatives considered**: EF Core migrations are the production path. They are noted as a future step, but switching from `EnsureCreated` mid-course would break existing trainee databases differently. Hand-written `ALTER TABLE` on startup is fragile.

## 11. Audit trail and reports

- **Decision**:
  - **Storage**: `DocumentActivity` rows with a **nullable** `DocumentId` FK (`OnDelete: SetNull`), plus snapshot columns (`DocumentTitle`, `FileType`) so records survive deletion (FR-034).
  - **Actions**: `Uploaded`, `Downloaded`, `Previewed`, `MetadataUpdated`, `Replaced`, `Shared`, `Unshared`, `Deleted`.
  - **Reports**: Administrators query a date range for:
    - most uploaded types (`GROUP BY FileType` over `Uploaded`);
    - most active uploaders (`GROUP BY UserId` over `Uploaded`, top 10);
    - access patterns (counts of `Downloaded`/`Previewed` per day and the top 10 most accessed documents).
- **Rationale**: A single append-only table serves both compliance and reports, and the snapshots keep reports meaningful after deletions.
- **Alternatives considered**: Logging to a file through `ILogger` cannot be queried for reports. A cascade delete of activities loses audit history.

## 12. Data conventions

- **Decision**:
  - `DocumentId` is an `int` identity.
  - `Category` is a `string` (≤ 50) validated against the 6 fixed values in `DocumentCategories`.
  - `FileType` is `string(255)`.
  - Timestamps are stored in UTC (`DateTime.UtcNow`) and displayed in local time.
  - Tags are trimmed, compared case-insensitively, and stored lowercased; each is at most 50 characters, with at most 10 per document.
  - Title is required, at most 200 characters; description at most 2,000; the original file name at most 255.
- **Rationale**: Satisfies the stakeholder database constraints and matches the existing `int` keys on `User` and `Project`.
- **Alternatives considered**: A GUID key or an enum category, both explicitly rejected by the stakeholders.

## 13. Testing strategy

- **Decision**: New `ContosoDashboard.Tests` (xUnit, `net8.0`) project referencing the app:
  - **Services**: `DocumentService`, `DocumentValidator`, `DocumentActivityService`, and `DashboardService` against `ApplicationDbContext` on in-memory SQLite (`DataSource=:memory:` with an open connection), so relational constraints are real. They use a temp-directory `LocalFileStorageService` and a configurable `FakeMalwareScanner`.
  - **Endpoints**: `WebApplicationFactory<Program>` with a test auth scheme that sets the `NameIdentifier`, asserting 200, 404, and headers for owners, sharees, non-members, and admins.
  - **Failure paths**: oversize, empty, and mismatched-signature files; scanner `Infected`/`Error`; a DB failure after the file write (no orphan record, file removed); an unauthorized edit, delete, or share; search exclusion.
  - **Wiring**: `Program.cs` adds `public partial class Program;` so the factory can reference it.
- **Rationale**: Principle V requires automated coverage, including failure paths, for authorization, persistence, and file handling. In-memory SQLite exercises the same provider as production training.
- **Alternatives considered**: The EF InMemory provider ignores relational constraints and transactions. bUnit component tests are valuable but lower priority than service and endpoint security tests; they are optional in tasks.

## 14. Runtime environment note

- **Decision**: The project targets `net8.0`, and the plan does not change that. On machines with only a newer runtime, run with `DOTNET_ROLL_FORWARD=Major` or install the .NET 8 runtime, as documented in the quickstart.
- **Rationale**: The constitution requires preserving the .NET 8 architecture. This workstation was verified to run the app with roll-forward on the .NET 10 runtime.
- **Alternatives considered**: Retargeting to `net10.0` is a separate, unspecified change.
