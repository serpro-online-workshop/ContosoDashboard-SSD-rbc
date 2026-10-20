# Quickstart: Validate Document Upload and Management

Use this guide to run the feature locally and confirm it end to end. Design details live in [data-model.md](data-model.md) and [contracts/](contracts/). Commands assume a bash shell at the repository root; PowerShell equivalents are noted where they differ.

## Prerequisites

- .NET SDK 8.0+ (the project targets `net8.0`). If only a newer runtime is installed, prefix run commands with `DOTNET_ROLL_FORWARD=Major` (PowerShell: `$env:DOTNET_ROLL_FORWARD="Major"`).
- **ClamAV daemon on `127.0.0.1:3310`**. Uploads are rejected while it is unavailable, by design. Choose one option:
  - Linux (Debian/Ubuntu): `sudo apt install clamav clamav-daemon`, then `sudo freshclam` and `sudo systemctl start clamav-daemon`. Confirm that `TCPSocket 3310` and `TCPAddr 127.0.0.1` are set in `/etc/clamav/clamd.conf`.
  - macOS: `brew install clamav`, configure `clamd.conf` with `TCPSocket 3310`, then run `freshclam` and `clamd`.
  - Windows: install ClamAV from clamav.net, enable `TCPSocket 3310` in `clamd.conf`, then run `freshclam.exe` and `clamd.exe`.
  - Container: `docker run -d -p 127.0.0.1:3310:3310 clamav/clamav`.

  `freshclam` downloads signatures once. After that, scanning works offline.
- Check the daemon: `printf 'zPING\0' | nc 127.0.0.1 3310` prints `PONG`.
- Sample files: a PDF under 25 MB, a PNG, a `.docx`, a file over 25 MB (`truncate -s 26M big.pdf`), an unsupported file (`echo x > test.exe`), a text file renamed to `.pdf` (signature mismatch), and an EICAR test file (`printf '%s' 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*' > eicar.txt`).

## 1. Reset the training database

The schema changes and `EnsureCreated` does not alter an existing database, so start clean. Stop the app first.

```bash
rm -f ContosoDashboard/ContosoDashboard.db ContosoDashboard/ContosoDashboard.db-shm ContosoDashboard/ContosoDashboard.db-wal
rm -rf ContosoDashboard/AppData/uploads
```

These remove only local, git-ignored training data. Both are recreated on the next start, with seed users and projects.

## 2. Build and run automated tests

```bash
dotnet build ContosoDashboard/ContosoDashboard.csproj
dotnet test ContosoDashboard.Tests/ContosoDashboard.Tests.csproj
```

Expected: the build succeeds and all tests pass. The tests use a fake scanner and do not need ClamAV. They cover the service authorization matrix, validation failures, the no-orphan rollback, scanner `Infected`/`Error` rejection, search access filtering, and endpoint 200/404 behavior.

## 3. Run the app

```bash
cd ContosoDashboard && dotnet run
```

Open the URL printed in the console (for example `http://localhost:5000`). Log in from the user dropdown.

## 4. Validation scenarios

Each row maps to spec acceptance scenarios. **Seed data**: Camille Nicole (PM of project 1), Floris Kregel (Team Lead, Engineering, member), Ni Kang (Employee, Engineering, member), Admin (IT).

| # | Steps | Expected | Spec |
|---|---|---|---|
| 1 | As Ni Kang: **My Documents → Upload**, pick the PDF, Title "Test Document", Category "Personal Files", Upload | Progress bar → success; the row shows title, category, date, size, no project. `ContosoDashboard/AppData/uploads/4/personal/` contains one GUID-named file | US1-1 |
| 2 | Upload the PNG with project "ContosoDashboard Development" | The row shows the project; the file is under `uploads/4/1/`. Camille and Floris get a "DocumentAdded" notification; Ni does not | US1-2, US3-3 |
| 3 | Upload `big.pdf`, `test.exe`, the renamed `.pdf`, and the PDF together | Only the valid PDF is stored. The others show the 25 MB, unsupported-type, and unsupported-type messages respectively | US1-3/4/6 |
| 4 | Upload without a title | The Upload button is disabled and the title field is flagged | US1-5 |
| 5 | Upload `eicar.txt` | "Rejected for security reasons"; nothing is listed and no file is written | US1-7 |
| 6 | Stop clamd, upload the PDF, restart clamd | "Security scan unavailable"; nothing is stored | Edge case |
| 7 | Sort by each column; filter by category, project, and date range | The list reorders and filters correctly | US1-8 |
| 8 | Open the PDF document → **Preview**, then **Download** | Preview opens inline in a new tab; download keeps the original file name. The `.docx` shows Download only | US2-1/2/3 |
| 9 | Edit the title and tags, save; **Replace** with another PDF | The new metadata shows everywhere; the size and type update; the old GUID file is gone and a new one exists | US2-4/5 |
| 10 | As Camille: open Project 1 details | The Project Documents card lists Ni's PNG; Download works; Delete is available (PM) | US3-1/4 |
| 11 | As Ni: copy the personal PDF's Download URL (`/api/documents/{id}/download`). Log in as Camille and open it; then log in as Admin and open it | Camille gets 404 (personal document, not shared); Admin gets 200 | FR-032, FR-024 |
| 12 | As Ni: **Search** "test", then a term matching only Camille's personal document | The first search finds Ni's document; the second shows "No documents found" | US4 |
| 13 | As Ni: share the personal PDF with Camille; share another document with department "Engineering" | Camille is notified and sees it in **Shared with Me**, with Download but no Edit, Delete, or Share. Floris and Camille (Engineering) are notified for the department share; Ni is not | US5-1/2/3 |
| 14 | As Ni: remove Camille's share | Camille's Shared with Me entry disappears and her download URL returns 404 | US5-4 |
| 15 | As Ni: **My Tasks → View** on a project task; **Upload** a document; **Attach existing** the personal PDF | Both appear on the task; both now show project 1 | US6-1/2/3 |
| 16 | As Ni: open the Dashboard | The "My Documents" card shows the count; Recent Documents lists the 5 latest | US6-4 |
| 17 | As Ni: delete a document → Cancel, then delete → Confirm | Cancel: unchanged. Confirm: removed from all lists, tasks, and shares; the file is deleted | US2-6/7 |
| 18 | As Admin: **Document Reports** for today | Shows top types, top uploaders, and access by day/document, matching the actions above, including the deleted document | US7-1/2 |
| 19 | As Ni: open `/documents/reports` | Access denied | US7-3 |

## 5. Performance check (SC-002…SC-005)

Run the test category that seeds 500 documents and times list and search queries:

```bash
dotnet test ContosoDashboard.Tests/ContosoDashboard.Tests.csproj --filter Category=Performance
```

Expected: My Documents and search complete in under 2 s. Manually, a 25 MB upload completes in under 30 s and a PDF preview opens in under 3 s.

## 6. Cleanup

Repeat [step 1](#1-reset-the-training-database) to remove the training data. Stop clamd if you started it only for this exercise.
