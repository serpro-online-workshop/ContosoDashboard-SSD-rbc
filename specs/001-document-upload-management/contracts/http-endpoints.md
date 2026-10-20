# Contract: HTTP Endpoints

Defined in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`, mapped in `Program.cs` via `app.MapDocumentEndpoints()` after `UseAuthorization()`. Both endpoints:

- require an authenticated cookie (`RequireAuthorization()`). Unauthenticated requests receive the existing cookie challenge, a redirect to `/login`;
- resolve the actor from `ClaimTypes.NameIdentifier` and delegate the access decision to `IDocumentService`;
- return **404** for missing **and** unauthorized documents (no existence disclosure);
- add `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`;
- record a `DocumentActivity` only on success;
- accept no request body or query parameters.

## GET `/api/documents/{documentId:int}/download`

Implements FR-018, FR-032, FR-033, FR-034.

| Response | When | Headers / body |
|---|---|---|
| 200 | Actor may view the document | `Content-Type`: stored `FileType`; `Content-Disposition: attachment; filename="…"; filename*=UTF-8''…` (original name); body: file bytes (range processing enabled) |
| 404 | Document missing, not accessible, or file missing from storage | Empty body |
| 302 → `/login` | Not authenticated | — |

Activity: `Downloaded`.

## GET `/api/documents/{documentId:int}/preview`

Implements FR-019, FR-032, FR-033, FR-034.

| Response | When | Headers / body |
|---|---|---|
| 200 | Actor may view **and** `FileType` ∈ {`application/pdf`, `image/jpeg`, `image/png`} | `Content-Type`: stored type; `Content-Disposition: inline; filename*=UTF-8''…`; `Content-Security-Policy: sandbox; default-src 'none'; img-src 'self'; object-src 'self'`; body: file bytes |
| 404 | Missing, not accessible, or type not previewable | Empty body |
| 302 → `/login` | Not authenticated | — |

Activity: `Previewed`. The UI opens preview links in a new tab (`target="_blank" rel="noopener"`), because the global `X-Frame-Options: DENY` header forbids embedding.

## Not exposed over HTTP

Upload, metadata edit, replace, delete, share, attach, search, and reports run inside the authenticated Blazor Server circuit through `IDocumentService` / `IDocumentActivityService` ([service-interfaces.md](service-interfaces.md)). No JSON API is added.
