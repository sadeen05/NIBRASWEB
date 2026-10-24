# Land-document storage — private files, validation and serving

Scope: the real land-document upload introduced in the F1 increment (21 Oct 2026). It replaces
the historical metadata-only contract for new uploads while keeping that contract only in
Development so the accepted step suites can still run.

> **Status (22 Oct 2026):** implemented and acceptance-tested against the disposable target. The
> additive migration `20261021163426_AddLandDocumentStorageMetadata` is applied to
> `SADEEN\SQLEXPRESS / NIBRAS_F1_Verify` (migration history 20). The focused runtime suite passed
> its real-upload/download/validation/cleanup cases (225 PASS / 0 FAIL / 3 documented SKIPs).
> Startup hardening was added after that run: outside Development the host now validates
> `Documents:StorageRoot` before it serves a request, and a missing, relative or `wwwroot`-contained
> root stops startup. No production storage root has been configured and no deployment has been
> performed. Historical
> dangling rows 100 and 102-104 remain preserved without physical bytes; row 101 is legacy
> metadata-only. See §9.

## 1. What is stored where

- The binary is written below `Documents:StorageRoot`, in a per-land directory:
  `land-{landId}/{random-guid}.{pdf|jpg|png}`.
- Outside Development, `StorageRoot` must be explicitly configured as a fully qualified absolute
  path. An empty value uses the `App_Data/land-documents` fallback **only in Development**; a
  relative path is also accepted only in Development for local compatibility.
- `App_Data/` is ignored by Git (`.gitignore`) and is **not** a static-content directory. The
  application does not call `UseStaticFiles` for it; ASP.NET Core serves only `wwwroot`.
- The original file name is **display metadata only**. It is reduced to its base name, control
  characters are removed and it is capped at 255 characters. It is never used to build the
  storage path.
- The database row stores the private relative storage path, the sanitized original name, the
  server-detected content type, the exact byte count and the SHA-256. The storage path is not
  returned by any API response.

The document DTO exposes `originalFileName`, `contentType`, `fileSizeBytes` and `hasFile`; a real
upload returns `filePath: null`. A legacy metadata row keeps its historical `filePath` string, but
that string is never opened.

## 2. Configuration

```jsonc
"Documents": {
  "StorageRoot": "",              // Development fallback only; required and absolute outside Development
  "MaxFileSizeBytes": 10485760    // 10 MiB, hard upper bound
}
```

- `MaxFileSizeBytes` must be greater than zero and may not exceed **10 MiB**. Startup validation
  (`DocumentStorageOptionsValidator`, registered with `ValidateOnStart`) rejects an invalid value.
- Outside Development, an empty or relative `StorageRoot` stops startup. A configured root must be
  fully qualified and must not resolve inside `wwwroot`. The same checks are repeated by the
  storage service as defence in depth.
- In Development, an empty value uses `App_Data/land-documents`; a relative value is resolved
  against the content root. A root inside `wwwroot` is rejected in every environment.
- The directory is created on the first upload, not at startup.
- In a container or IIS deployment the root must be a writable, backed-up volume that is separate
  from the application binaries and not web-served.
- The global Kestrel limit remains **1 MiB**
  (`builder.WebHost.ConfigureKestrel(... MaxRequestBodySize = 1_048_576)`). The multipart upload
  action alone opts into **11 MiB** with `[RequestSizeLimit]` and `[RequestFormLimits]`, i.e. the
  10 MiB file plus a 1 MiB envelope for the boundary, form fields and headers. No other action
  gets the larger cap.

## 3. Validation

The upload path is `POST /api/lands/{id}/documents` with `multipart/form-data`:

| Field | Rule |
|---|---|
| `documentTypeId` | Required, `>= 1`; the document type must exist. |
| `file` | Required, non-empty, `<= Documents:MaxFileSizeBytes`. |
| signature | The **content** must start with `%PDF-`, JPEG `FF D8 FF`, or the 8-byte PNG signature. The browser-supplied content type and extension are ignored. |
| stored type | Derived from the signature: `application/pdf`, `image/jpeg` or `image/png`; the extension is derived from the same value. |
| storage name | `Guid.NewGuid():N` plus the derived extension; no user input. |
| hash | SHA-256 over the exact bytes, lower-case hex, stored in `SHA256`. |
| original name | Base name only; control characters removed; at most 255 characters; fallback `document`. |

The file is streamed once into a temporary file below the private root, hashing as it goes. The
size is checked both from the `IFormFile` metadata and again on the actual bytes, so a lying
`Content-Length` or a growing stream cannot exceed the cap. A failed validation deletes the
temporary file. The content type and extension used for the response and for the stored path come
from the signature, never from the client.

## 4. Authorization and serving

`GET /api/lands/{id}/documents/{documentId}/download` is authenticated. The service re-loads the
land and applies the same visibility rules as the document list:

- the **landlord owner** may download;
- an **Admin/SuperAdmin** may download;
- an approved, verified investor may download only if the land is visible to investors under the
  normal land-status rules; a draft or foreign land is rejected (403/404);
- an anonymous caller gets **401**.

The response is an attachment with the server-detected content type and the sanitized original
name, plus `X-Content-Type-Options: nosniff` and `Cache-Control: private, no-store`. Range
requests are enabled by the MVC file result. The response never contains the private storage path.

`GET /api/lands/{id}/documents` returns the metadata list; it does not expose the local path for a
real upload.

## 5. Transactions and failure handling

The file is validated and moved into its final private location **before** the database
transaction starts. The database insert then runs in a transaction:

- if the insert fails (for example the document type was deleted concurrently and SQL Server
  reports FK 547), the transaction rolls back and the just-written file is deleted on a
  best-effort basis; the duplicate-document guard and version allocation run under the same land
  row lock;
- if the process crashes between the move and the commit, an orphan private file can remain; it is
  unreferenced and never served, but it consumes disk. A periodic reconciliation job is **not**
  implemented (residual risk, §8);
- a failed cleanup is logged by exception type only. It is never allowed to hide the original
  database failure.

## 6. Legacy metadata-only upload

`POST /api/lands/{id}/documents/metadata` with `application/json` and

```json
{ "documentTypeId": 1, "filePath": "legacy/contract.pdf" }
```

is accepted **only when `IHostEnvironment.IsDevelopment()`**. In every other environment it
returns **415** with a message directing the caller to upload the file.

The legacy body keeps its historical validation: required, maximum 500 characters, no control
characters, and `SafeRelativePath` rejects a scheme, a leading slash, a drive letter and any `..`
segment. The string is stored as text. It is never opened, fetched, redirected or concatenated
into a download path. The row has `hasFile: false`, has no size/hash, and its download endpoint
returns **404**. This compatibility path exists only because the accepted regression scripts still
use it; new clients must use multipart.

## 7. Security traceability deviations

The historical SEC-13 text said "`FilePath` is stored text only; no `File`/`Directory` use". That
statement was true before this increment and is no longer the whole truth. The deliberate change
is:

- new uploads store real bytes in a **private, non-static** root;
- only `DocumentStorageService` performs **document** file I/O, and every path is resolved and
  checked to stay inside the configured root;
- the legacy text field remains text-only and is never opened;
- there is still **no outbound HTTP**;
- the download route is authenticated and uses the existing land visibility rules.

SEC-14's global 1 MiB Kestrel limit is unchanged. The only exception is the single multipart
upload action's 11 MiB request cap, sized as the 10 MiB file limit plus a 1 MiB envelope; the
configured file limit is validated at startup and cannot exceed 10 MiB.

## 8. Residual risks and operational notes

- **No malware scan or content disarm.** Signature validation proves the container type, not that
  the PDF/image is safe for a viewer. Serve as attachment with `nosniff`; add an AV/CDR pipeline
  before any in-browser rendering is considered.
- **No encryption at rest in the application.** Use volume/disk encryption or a database-backed
  object store if the deployment requires it; the current store inherits the host volume's
  protection.
- **Orphan window on crash.** A crash after the file move and before the row commit can leave a
  private orphan. Add a reconciliation job keyed by the stored relative path if disk growth
  matters.
- **Symlinks/junctions.** The root is resolved once at startup; the path containment check is
  lexical. A privileged attacker who can replace directories below the root is outside the
  threat model. Do not make the root writable by untrusted OS users.
- **Backups and retention.** Back up the storage root and the database together so rows and files
  stay consistent. There is no automatic retention/deletion policy for superseded document
  versions; that is a product/operations decision.
- **Disk capacity.** The 10 MiB per-file cap does not cap the number of versions or lands. Monitor
  the volume and set a per-land/tenant quota if needed.
- **Legacy mode.** The JSON path is a Development compatibility shim. Ensure production runs with
  `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT` set to Production (or another non-Development
  value); otherwise the shim is reachable by an authenticated landlord.

## 9. Migration and verification status

- Migration: `Migrations/20261021163426_AddLandDocumentStorageMetadata.cs` adds nullable
  `CONTENT_TYPE`, `FILE_SIZE_BYTES`, `ORIGINAL_FILE_NAME` and `SHA256` to `LAND_DOCUMENTS`. It is
  **applied** to the disposable target `SADEEN\SQLEXPRESS / NIBRAS_F1_Verify`; migration history is
  20. Do not reapply or reset it.
- Static verification: Release build, EF model-drift check, PowerShell parsing, Swagger export
  verification and the focused suite's static review are part of the F1 verification record.
- Startup-hardening verification (22 Oct 2026): the offline `--static` acceptance runner passed
  **140/0**, including the storage validator cases (missing, relative, `wwwroot`, valid absolute,
  Development fallback and size bounds). A real Production-mode host start with no storage root
  exited with `OptionsValidationException` before listening or accessing the database. This is
  startup-path evidence, not production-volume durability evidence.
- Runtime verification: the focused suite
  `docs/tests/scripts/f1_email_change_approval_documents.ps1` passed 225 checks with 0 failures
  and 3 documented SKIPs. It exercised real PDF upload, metadata, authenticated download,
  anonymous/non-owner denial, bad signature, oversize at the suite's 1 KiB cap, unknown type,
  legacy JSON, traversal rejection, storage cleanup, retained-file size/hash verification and the
  Development-only legacy guard. It uses a suite-owned temporary `Documents__StorageRoot`; the
  production default remains 10 MiB and was not deployment-tested.
- Browser UI verification (22 Oct 2026): the real frontend uploaded a 69-byte PDF to a Draft land
  through `LandDocumentsPanel`; the table showed version 1 / Pending, and the Download action
  returned the same 69 bytes. The stored file and the `LAND_DOCUMENTS` row carried SHA-256
  `cfa3181c1ee36e8bce5e39f84959f4558ea7ba32c0e4539a8ab3c8ce8c716ec6`, the private
  `FILE_PATH` was never returned to the client, and the download was served as an attachment.
  The detailed F1 release evidence is historical and retained outside the delivery repository;
  see the completed cleanup record in `NIBRAS_FILE_GUIDE.md`.
- Historical rows 100 and 102-104 have no physical bytes, and row 101 is legacy metadata-only.
  They were preserved and were not used to fabricate acceptance evidence. Remediation requires a
  separately authorized data-recovery decision.
- The historical regression suites that use the JSON metadata body remain accepted. Their rows
  have `hasFile: false` and no downloadable bytes.
