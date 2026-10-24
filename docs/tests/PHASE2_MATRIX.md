# NIBRAS — Phase-2 Functional Test Matrix (prepared, not executed)

Status: **PREPARED ONLY.** Nothing in this document has been executed. It is the
Phase-D (post-approval) execution plan for
`20261018090000_Phase2_LandCorrectionAndChangeRequests` (change requests, the
hidden-from-investors flag, derived evidence visibility and the re-review surface).

Target: `SADEEN\SQLEXPRESS / NEBRASDB_Step1` **only** (confirmed identity required
before every run). Never `NEBRAS` / `NEBRASDB`.

## Q. Test isolation strategy (applies to every case)

1. **Disposable fixtures with returned IDs.** Every case creates its own landlord,
   investor and land(s) via the API and uses the **returned ids**; it never assumes
   numeric ids and never edits or deletes pre-existing business rows (the 213 demo
   lands, users, offers, contracts, criteria or document-type rows).
2. **Unique stamps.** Emails, land numbers and parcels carry the suite's
   `$stamp` (`HHmmss` + random), so reruns never collide and never match a previous
   run's rows. File paths are stamped the same way.
3. **Append-only tables.** Change requests, documents versions, status history and
   land assessments are append-only by design; fixtures therefore leave rows
   behind. That is expected and is part of the audit evidence — there is no
   destructive cleanup.
4. **Fixture-local DB surgery (only where the API cannot express the fault).** The
   malformed-snapshot and unrecorded-pin cases need corrupt/inconsistent *stored*
   state. Those `UPDATE`s are allowed **only** on rows the same script created in
   that run (the script reads the row id from its own `201`/`SELECT` and updates
   exactly that row; it is clearly commented `FIXTURE-LOCAL`). No pre-existing row
   is ever modified.
5. **One fresh app start per suite** (`s4_app.ps1` kills the port listener first),
   `-AuthPerMinute 1000`; restart between suites (README rule 1–3).
6. **Secrets.** SuperAdmin credentials are read at run time from user-secrets and
   never printed; tokens and passwords stay in memory (existing house rule).
7. **Regression snapshot.** Before the whole Phase-D run, record (read-only):
   `SELECT LAND_STATUS_ID, COUNT(*) FROM LANDS GROUP BY LAND_STATUS_ID` and the
   outstanding-evidence count (must be 0). After the run the only changes may be
   the fixture lands; the pre-existing counts may only stay equal or grow by the
   fixture lands. Any status change of a pre-existing land, any deletion, or any
   outstanding evidence left on a pre-existing land is a failure signal.

## Case matrix

Case ids: `P2-<area>-nn`. Existing suites cover the regression surface and are
referenced where applicable (run them in the documented order first).

### A. Landlord registration / authentication / ownership
| Case | Steps | Expected |
| --- | --- | --- |
| P2-A-01..04 (existing) | register/login landlord + investor, unapproved-investor gate | `step3_suite.ps1` + `s4_tests.ps1` A2 setup |
| P2-A-05 | non-owner landlord submits a change request on a foreign land | `403` |
| P2-A-06 | investor submits a change request | `403` |
| P2-A-07 | change request for a missing land id | `404` |
| P2-A-08 | `GET /api/land-change-requests?landId=` by another landlord / investor | `403` |
| P2-A-09 | anonymous `GET /api/land-change-requests/pending` | `401` |

### B. Draft creation and initial submission
| Case | Steps | Expected |
| --- | --- | --- |
| P2-B-01 (existing) | create (governorate only) → upload 2 required types → submit | `s4_tests.ps1` A2-04..07; status 2 |
| P2-B-02 | change request submitted for a Draft land | `409` "approved land" |
| P2-B-03 | change request submitted for a PendingVerification land | `409` |

### C. Regular Admin review / approve / reject (existing surface)
| Case | Steps | Expected |
| --- | --- | --- |
| P2-C-01 (existing) | verify with pending doc `409`; approve docs; verify `200`; reject without reason `400`; reject with reason `200` | `s4_tests.ps1` S4-50..52, `s4_admin.ps1` A-06b..S4-52e |

### D. Rejected land correction + SuperAdmin resubmission
| Case | Steps | Expected |
| --- | --- | --- |
| P2-D-01 (existing) | reject → revise → edit → resubmit → status `6` (PendingSuperAdminReview) → Admin `403` → SuperAdmin reviews/verifies | `s4_tests.ps1` S4-52a..i |

### E. Approved land change requests (reason / supporting documents)
| Case | Steps | Expected |
| --- | --- | --- |
| P2-E-01 | valid submit (reason + supporting document of an existing type) | `201`, `Location: /api/land-change-requests?landId=…`; row `STATUS='Pending'`, `ISJSON(OLD_SNAPSHOT)=1`, `ISJSON(NEW_SNAPSHOT)=1`, `ISJSON(SUPPORTING_DOCUMENTS)=1`, `REQUESTED_BY_ID` = owner |
| P2-E-02 | second submit while one is pending | `409` "already has a change request" (pre-check; F3 race in §P) |
| P2-E-03 | empty reason; area `0`; area `100000000`; unknown supporting `documentTypeId` | `400` each |
| P2-E-04 | owner/Admin/SA read `?landId=`; SuperAdmin reads `/pending` | `200`; queue contains the id |
| P2-E-05 | reject by SuperAdmin with a decision reason | `200`; `REVIEWED_BY_ID`/`REVIEWED_AT`/`DECISION_REASON` set; land fields unchanged |
| P2-E-06 | approve by SuperAdmin (identity/area change) | `200`; proposed fields applied; `VERIFIED_AGAINST_CRITERION_ID` and `DATA_VERIFIED_BY_ADMIN` preserved; description-only change keeps document approvals |
| P2-E-07 | decision on an already-decided request | `409` "already been decided" |
| P2-E-08 | decision on a missing request id | `404` |
| P2-E-09 | decision with empty `decisionReason` | `400` |

### F. Immediate investor visibility restriction
| Case | Steps | Expected |
| --- | --- | --- |
| P2-F-01 | pending request (default fixture) | DB `LANDS.IS_HIDDEN_FROM_INVESTORS = 1`; status stays `3` (Verified) |
| P2-F-02 | investor list / investor `GET /api/lands/{id}` | id absent from list; detail `403` |
| P2-F-03 | owner and Admin/SA views | unchanged (`200`) |
| P2-F-04 | investor offer create against the hidden land | `409` "not available for offers while a change request is under review" |
| P2-F-05 | after the decision | `IS_HIDDEN_FROM_INVESTORS = 0` in both outcomes |

### G. SuperAdmin-only decisions and technical edits
| Case | Steps | Expected |
| --- | --- | --- |
| P2-G-01 (existing) | regular Admin `PUT /assessment` | `403` (`s4_tests.ps1` A2-07d) |
| P2-G-02 | landlord / regular Admin `POST /{id}/decision` | `403` |
| P2-G-03 | regular Admin `GET /pending`; owner `GET /pending` | `403` |
| P2-G-04 (existing) | reassessment of a Verified land | `409` (`s4_admin.ps1` A-06b analog) |

### H. Snapshot validation and malformed-JSON recovery (fixture-local)
| Case | Steps | Expected |
| --- | --- | --- |
| P2-H-01 | valid request; FIXTURE-LOCAL set its `OLD_SNAPSHOT='{}'`; SuperAdmin approve | `409` "before-change snapshot … cannot be read"; land fields unchanged |
| P2-H-02 | same row: SuperAdmin **reject** | `200` (a rejection never deserializes; the request is never stuck); stored row not repaired (`OLD_SNAPSHOT` still `'{}'`) |
| P2-H-03 | new request; FIXTURE-LOCAL set `NEW_SNAPSHOT` to JSON with a blank `landNumber` (and one case with `areaDonum: 0`) | `409` "proposed-change snapshot … cannot be read"; then reject `200` |
| P2-H-04 | read endpoints after H-01/H-03 | DTO `originalFields`/`proposedFields` = `null` (never an invented repair) |

### I. Missing / unrecorded pinned criteria
| Case | Steps | Expected |
| --- | --- | --- |
| P2-I-01 | propose an area below the pinned version's minimum | approve `409` "below the minimum of the criteria version…"; reject `200` (not stuck) |
| P2-I-02 | FIXTURE-LOCAL cleared `VERIFIED_AGAINST_CRITERION_ID` to `NULL` (an unavailable basis: `FK_LANDS_VERIFIED_CRITERION` makes a nonexistent id impossible, and the guard fails closed on a missing pin exactly as on a missing row); approve | `409` "criteria version this land was approved against is unavailable"; reject `200` |
| P2-I-03 | unrecorded pin + outstanding evidence; SuperAdmin re-approves the latest required document | `409` "required documents cannot be approved … stays hidden"; **reject** of that document `200`; restoring the pin (FIXTURE-LOCAL) then re-upload + approve `200`; land visible again |
| P2-I-04 | criteria rows | append-only; no in-app delete (existing S4-57f/g `405`) |

### J. Document versioning / latest required evidence / SuperAdmin re-review
| Case | Steps | Expected |
| --- | --- | --- |
| P2-J-01 (existing) | version increments per (land, type); freeze while PendingVerification | `s4_tests.ps1` S4-10/11/23, R-34..37 races |
| P2-J-02 | approved change invalidates approvals (identity/location/area changed) | approved rows become `Pending` (same rows, no deletion); land hidden until re-approved |
| P2-J-03 | owner correction upload: outstanding type | `201`, new `Pending` version (never overwrites) |
| P2-J-04 | owner correction upload: type whose latest is approved | `409` "only the document types whose latest version is not approved can be corrected" |
| P2-J-05 | regular Admin re-review on an approved land with outstanding evidence | `403` (SuperAdmin-only) |
| P2-J-06 | re-review with complete evidence (nothing outstanding) | `409` "Documents of a verified land cannot be reviewed again." **before** the authority check |
| P2-J-07 | SuperAdmin approves the latest version of the outstanding type; repeat per type until complete | `200` each; investor visibility restored only at the end |

### K. Criteria-version pinning / historical integrity
| Case | Steps | Expected |
| --- | --- | --- |
| P2-K-01 (existing) | new criteria version does not move an approved land's pin | `s4_tests.ps1` S4-58a; `s4_admin.ps1` S4-58a |
| P2-K-02 | decision approves against the pinned version, not the current one | §I cases; pin unchanged after approve |

### L. Offers / contracts blocked while hidden or outstanding
| Case | Steps | Expected |
| --- | --- | --- |
| P2-L-01 | investor offer create on a hidden land | `409` (change-request message) — no offer row |
| P2-L-02 | investor offer create on an approved land with outstanding evidence (post-invalidation) | `409` ("documents are being corrected") |
| P2-L-03 | Admin-approve of an existing offer while the land is hidden / outstanding | `409` (both messages); offer and negotiation history untouched |
| P2-L-04 | contract create from an Accepted offer while hidden/outstanding | `409` (same gates in the contract path; existing `s6_contracts.ps1` covers the surface) |
| P2-L-05 | after re-approval completes | normal offer/contract flow resumes (existing `s5_offers.ps1` / `s6_contracts.ps1`) |

### M. Rejection restores visibility only when still eligible
| Case | Steps | Expected |
| --- | --- | --- |
| P2-M-01 | reject a pending request on a land whose evidence was already complete | flag cleared; investor sees the land again immediately |
| P2-M-02 | reject while evidence is still outstanding (e.g. after a previous invalidation and before re-approval) | flag cleared but the land **stays invisible** (derived evidence rule) until the documents are approved |
| P2-M-03 | approve with a description-only change | no invalidation; land visible after the decision |

### N. Audit trail
| Case | Steps | Expected |
| --- | --- | --- |
| P2-N-01 | submit + decide | `LAND_STATUS_HISTORIES` gains a "Change request … submitted/hidden" row at submit and one decision row; reviewer identity, `REVIEWED_AT`, `DECISION_REASON` recorded; `ROW_VERSION` changed at decide |
| P2-N-02 | `OLD_SNAPSHOT`/`NEW_SNAPSHOT` | stored verbatim (never rewritten by a decision); `GET` returns them as structured `originalFields`/`proposedFields` |
| P2-N-03 | document correction | previous versions and their reviews remain on their rows |

### O. Status-code surface (summary)
| Route | 401 | 403 | 400 | 404 | 409 |
| --- | --- | --- | --- | --- | --- |
| `POST /api/land-change-requests` | anon | non-owner | validation | missing land | not Verified / already pending / F3 |
| `GET /api/land-change-requests?landId=` | anon | non-owner non-admin | bad query | missing land | — |
| `GET /api/land-change-requests/pending` | anon | non-SA | — | — | — |
| `POST /api/land-change-requests/{id}/decision` | anon | non-SA | empty reason | missing request | decided / not-Verified / stale / duplicate / pinned criteria / snapshots / rowversion |

### P. Concurrency
| Case | Steps | Expected |
| --- | --- | --- |
| P2-P-01 | two parallel submits on the same land | exactly one `201`, one `409`; exactly one pending row; no SQL text in any body |
| P2-P-02 | two parallel decisions on the same request (two approve calls) | exactly one `200`, one `409` ("already decided by another reviewer" or "already been decided"); final status decided once; exactly one decision history row; no 500 |
| P2-P-03 (existing races) | verify race, parcel race, upload versioning race, reservation races | `docs/tests/scripts/parallel-approval.ps1`, `s4_review.ps1`, `s4_regression.ps1`, `s7_reservations.ps1` |
| P2-P-04 | upload racing the re-review of the same required type (SuperAdmin) | no lost approval: after the race the latest version's approval state and the land visibility are consistent (assert final visible state matches the latest version's status) |

### R. Rollback criteria (abort the Phase-D run immediately)
- any `5xx` on an L1/L2 path (submit/decide) or any unhandled `500` in a race;
- an unexplained deadlock or lock timeout;
- any investor-visible response that leaks a hidden land/offer/contract;
- any modification of a pre-existing land/user/document row;
- any snapshot accepted as readable while structurally unusable (fields blanked
  after approval);
- migration application output not matching the reviewed script.
On any trigger: stop, collect the app log + `sqlcmd` snapshots, and restore the
pre-migration backup if a schema/data change is suspected (see §S).

## S. Backup / restore checklist (operator, Phase D)

1. Confirm identity: `SELECT DB_NAME(), @@SERVERNAME` → `NEBRASDB_Step1` /
   `SADEEN\SQLEXPRESS` (record ProductVersion 16.0.1000.6, compat 160).
2. Record baseline: `__EFMigrationsHistory` (13 rows), status counts
   `1/2/3/5 = 44/12/85/72`, `LANDS=213`, `LAND_DOCUMENTS=394` (315 Approved /
   79 Pending), outstanding-evidence count `0`.
3. Full backup (choose one):
   - `BACKUP DATABASE [NEBRASDB_Step1] TO DISK = '<safe path>\NEBRASDB_Step1_pre_phase2.bak' WITH COPY_ONLY, CHECKSUM, STATS = 5;`
   - or a VM/disk snapshot of the instance data directory (SQL Express).
4. Verify the backup: `RESTORE VERIFYONLY FROM DISK = '<…>.bak' WITH CHECKSUM;`
5. Note the rollback target and the app build (the rollback also needs the code
   revision that matches the old schema; current tree requires the Phase-2 schema).
6. After the migration: re-run step 2 (now 14 migrations, +1 new table, +1 column)
   and compare all other counts unchanged.
7. Restore procedure (only on a rollback decision):
   `RESTORE DATABASE [NIBRASDB_Step1] FROM DISK='<…>.bak' WITH REPLACE, RECOVERY;`
   (single-user / stop the app first), then revert the code to the matching build.

## T. Migration execution plan (Phase D, after explicit approval)

Reviewed Up script (generated, byte-for-byte what `dotnet ef database update`
executes for this step): add `LANDS.IS_HIDDEN_FROM_INVESTORS bit NOT NULL
DEFAULT 0`, create `LAND_CHANGE_REQUESTS` (13 columns, 3 FKs NO ACTION, 4 indexes
incl. the F3 filtered unique), insert the history row — all inside one
transaction. The Down script drops the table, drops the default constraint, drops
the column and the history row.

```powershell
# 1) generate/verify the script again (must match the reviewed copy)
dotnet ef migrations script 20261017140348_Phase1_GovernoratesAndLandAssessment `
  20261018090000_Phase2_LandCorrectionAndChangeRequests --output phase2_up.sql
# 2) apply exactly the reviewed script (single transaction, stop on error).
#    -I (QUOTED_IDENTIFIER ON) is REQUIRED: sqlcmd defaults it OFF, and LANDS
#    carries filtered indexes, so ALTER TABLE fails with Msg 1934 without it.
sqlcmd -S "SADEEN\SQLEXPRESS" -d NEBRASDB_Step1 -E -C -b -f 65001 -I -i phase2_up.sql
# alternative (equivalent; requires user-secrets/env for design time):
#   dotnet ef database update 20261018090000_Phase2_LandCorrectionAndChangeRequests
# 3) verify schema + history (read-only)
#    - __EFMigrationsHistory = 14 rows, last = 20261018090000_Phase2_…
#    - LAND_CHANGE_REQUESTS exists with PK/FK/index names; IS_HIDDEN_FROM_INVESTORS exists
#    - counts from §S step 2 unchanged
```

Then build (`dotnet build -c Release --no-incremental`), start the app with
`s4_app.ps1` (Development, `-AuthPerMinute 1000`), run the suites in the README
order, then `p2_change_requests.ps1` (this matrix's §E–§P cases), then re-verify
the §S step-2 invariants plus the regression snapshot (§Q item 7).

## Evidence collection

- Script stdout (PASS/FAIL lines) + `$LASTEXITCODE`.
- `sqlcmd` result sets for every DB assertion in this document.
- App log (`%TEMP%\nibrasweb-test-logs\app_5199_*.log`) for any failure.
- After the run: the §S step-2 comparison and the outstanding-evidence count
  (must return to 0 after the fixtures are fully re-approved).
