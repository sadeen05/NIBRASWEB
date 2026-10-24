# F1 isolated acceptance preparation

This project is excluded from web compilation and published content. It does not start an API, run seeders, load user secrets or enable test controls in the production app.

## Allowed offline validation
- Build Release with existing cached packages.
- Run the compiled DLL with `--static`: deterministic clock, configuration and in-memory cancellation mocks only. This includes the production-hardening checks for the document-storage root, forwarded-header trust, `AllowedHosts`, and Development-only SuperAdmin seed credentials.
- Run `node approvalTime.mock.mjs`: UTC display mocks only.
- EF `migrations has-pending-model-changes` uses ModelOnlyFactory. Every connection attempt is rejected by NoConnections before opening.

## Separately gated modes — NOT EXECUTED during preparation
- `--sql-acceptance` connects only to SADEEN\\SQLEXPRESS / NIBRAS_F1_Verify using integrated authentication.
  It inserts uniquely prefixed users, tokens, events, land and document-type fixtures. It retains their history.
  It tests 24h/1h issuance and rejection at exact expiry and after expiry, valid redemption before expiry,
  actual registration/EmailChange service cancellation, preservation of newer requests and audit, and a
  two-context stale-user interleaving followed by an allowed same-email profile update.
  The FK scenario pauses a real upload after document-type lookup and before SaveChanges, deletes only
  its own unreferenced type from a second context, resumes, and checks controlled rejection with zero
  document rows/files. This is not the ordinary concurrent-upload test.
- `--fake-smtp` binds only to loopback and sends dummy messages to a local fake server. It prepares
  success, rejection, missing STARTTLS, 30s async deadline and caller cancellation acceptance.
  No real SMTP settings or credentials are used. This is not production delivery validation.

These modes require explicit approval before invocation. They do not migrate, reset, drop, seed or
clean historical data. No automatic retry is configured. Assertion failures exit nonzero; unexpected
exceptions are logged only by type. Fixture rows remain for inspection; no existing document is deleted.

Cancellation contract: an interrupted sender retires only its exact still-live issuance under the
account lock, clears only the matching pending email, preserves active identity and appends audit.
If a newer issuance retired it already, cancellation leaves the newer pending email/token untouched.
Cleanup has an independent 5s budget; uncertain cleanup propagates failure. Completed delivery cannot
be recalled; SMTP deadline failures are classified separately from caller cancellation.

A service-level controlled identity is used for the stale-profile test. Controller/JWT authorization
remains acceptance in the existing focused API suite; this project does not replace it.

## FK harness pre-run safety

The run records an immutable ID/name/UUID marker receipt only after creating its own type.
Deletion rereads that row with XLOCK/HOLDLOCK in a verified SERIALIZABLE transaction, rejects
any mismatch or required/shared state, counts all document references (ignoring query filters),
and repeats the zero-reference and ID/name predicates in the DELETE. Exactly one affected row
and a confirmed commit/disposal are required before a proof can release the upload barrier.
Failures roll back with independent bounded cleanup; no proof is returned on uncertain cleanup.

Every started upload is observed and joined before metadata/storage inspection or resource
release. Abort cancels late arrivals rather than allowing them to insert after a failed gate.
All terminal paths fail the batch. If cancellation cannot settle an operation within its deadline
and grace period, the isolated runtime test executable self-exits with code 74 without dumping
exceptions or touching another process. This is last-resort containment, not evidence of clean
SQL/storage restoration; integrity review is mandatory before any further run. Mocks inject a
simulated terminal policy that settles the fake upload and verifies it is joined; no process is
terminated during static validation. SQL and process-containment behavior remain runtime-gated.
## Focused-suite retained document storage

The focused PowerShell suite keeps each run under the current user's LocalApplicationData/
NIBRAS_F1_RetainedDocuments/run-<UUID>/documents, outside the repository and public web roots.
Directories are created with owner-only inheritable FullControl DACLs. The adjacent private
storage-receipt.json records the approved server/database, run/land ownership, relative file keys,
sizes and SHA-256 values. No JWT, verification link, password or file bytes enter the receipt.

After verified API shutdown, current-run document metadata is independently read using guarded,
parameterized SELECTs. Every committed real file must exist and match its size/hash. Legacy rows
with null storage metadata have no expected bytes. API shutdown never deletes committed files.
Only server-shaped .tmp/<UUID>.tmp uploads absent from all retained metadata references may be
removed after quiescence and integrity checks. Unknown, referenced, mismatched or unsafe files
are retained and cause failure. The manifest is marked Verified only after successful checks;
failure/uncertain cleanup never passes and never triggers an automatic retry.

Offline verification: Windows PowerShell 5.1 f1_storage_mock_checks.ps1 uses a virtual filesystem,
real in-memory ACL descriptors and mocked SELECT snapshots; it opens no database or API.
The fail-fast mocks exercise storage checkpoints before Disabled startup and after final shutdown.
Runtime invocation of the focused suite still requires separate approval.

Historical document IDs 100 and 102-104 remain known dangling real-file references from the old
temporary-root cleanup; ID 101 is legacy metadata-only. This change does not recover bytes or
alter those rows. Integrity evidence is scoped to the new run, not the historical database.
Run-specific receipts preserve the storage-root mapping for independent checks. An API configured
to a different run root cannot serve an earlier run's archived bytes automatically.

