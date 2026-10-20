# Contract: UI Routes and Components

All new pages use `@attribute [Authorize]` and obtain the user ID the same way as the existing pages (`AuthenticationStateProvider` → `ClaimTypes.NameIdentifier`). They never make authorization decisions on their own. Action visibility comes from `DocumentDetail.Can*` flags, and every action is re-checked by the service.

## Routes

| Route | Page | Purpose | Spec |
|---|---|---|---|
| `/documents` | `Pages/Documents.razor` | My Documents: sortable table (title, category, upload date, size, project), filters (category, project, date range), **Upload** button | US1, FR-013/014 |
| `/documents/{documentId:int}` | `Pages/DocumentDetails.razor` | Metadata, Download, Preview (new tab), Edit, Replace, Share/unshare, Delete with confirmation; "Document not available" when null | US2, US5 |
| `/documents/shared` | `Pages/SharedDocuments.razor` | Shared with Me list | FR-026 |
| `/documents/search` | `Pages/DocumentSearch.razor` | Search box (2–100 chars) + results; empty state "No documents found" | US4 |
| `/documents/reports` | `Pages/DocumentReports.razor` | `@attribute [Authorize(Policy = "Administrator")]`; date range + four report tables | US7, FR-035 |
| `/tasks/{taskId:int}` | `Pages/TaskDetails.razor` | Task info (via existing `ITaskService.GetTaskByIdAsync`), attached documents, **Upload** and **Attach existing** | US6, FR-028/029 |
| `/projects/{projectId:int}` | `Pages/ProjectDetails.razor` (edit) | New "Project Documents" card: list + **Upload to project** | US3 |
| `/` | `Pages/Index.razor` (edit) | Fifth summary card "My Documents" (count) + "Recent Documents" widget (5 latest, links to details) | FR-030 |

`Tasks.razor`: `ViewTaskDetails(taskId)` navigates to `/tasks/{taskId}`.

## Navigation (`Shared/NavMenu.razor`)

Insert the following after "My Projects", using the existing `NavLink` + Bootstrap Icons format:

- `documents`: `bi-file-earmark-text`, "My Documents"
- `documents/shared`: `bi-share`, "Shared with Me"
- `documents/search`: `bi-search`, "Search Documents"
- `documents/reports`: `bi-bar-chart`, "Document Reports" (inside `<AuthorizeView Roles="Administrator">`)

## Shared components (`Shared/Documents/`)

| Component | Parameters | Behavior |
|---|---|---|
| `DocumentUploadDialog` | `int? FixedProjectId`, `int? TaskId`, `EventCallback OnUploaded` | Modal with `InputFile multiple` (`accept` = allowlist), shared metadata (title defaults to the file name per file and is editable; category; description; project dropdown limited to the user's projects unless `FixedProjectId`; comma-separated tags). The Upload button stays disabled until the required fields are valid. Files upload sequentially with a per-file progress bar and a per-file ✓/✗ result message. `InputFile` is re-keyed after completion. Opening the dialog, choosing files, and clicking Upload is ≤ 3 clicks (SC-001). |
| `DocumentTable` | `IReadOnlyList<DocumentListItem> Items`, `bool Sortable`, `EventCallback<DocumentSort> OnSort` | Displays the title (link to details), category, upload date (local time), human-readable size, project, Download link, and a Preview link when `CanPreview` |
| `DocumentAttachDialog` | `int TaskId`, `EventCallback OnAttached` | Search/select from the user's accessible documents and attach |

## Confirmation and messaging

- Delete uses an explicit confirmation modal ("Permanently delete '{title}'? This cannot be undone.").
- Results are shown with Bootstrap alerts; error text comes from `DocumentOperationResult.Error` and the [validation table](../data-model.md#validation-rules-consolidated).
- Download and preview are plain links to the [HTTP endpoints](http-endpoints.md), so the browser handles streaming.
