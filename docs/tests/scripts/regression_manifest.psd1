@{
    Version                = 1
    ApprovedServer         = 'SADEEN\SQLEXPRESS'
    ApprovedDatabase       = 'NIBRAS_F1_Verify'
    ApiPort                = 5199
    DefaultTimeoutSeconds  = 900
    TargetGuardSha256      = '41CA72D5D4F7F7AC8BBDAD35D890A8311E2B98A1DD91E343031520006C138EDE'
    ProtectedPreflightSha256 = '80167FF8FDAC34000F63F122B5A7A7741EF041F4BF1C0CCFDF1E0883F9758575'

    # Documented order of the remaining 12 suites. The runner refuses a manifest whose
    # names or order differ, so a later reordering cannot silently change the run.
    Suites                 = @(
        @{
            Order             = 1
            Name              = 's4_tests'
            File              = 's4_tests.ps1'
            SummaryPattern    = '(?m)^TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'Reliable: exit 1 when calculated FAIL>0 or BLOCKED>0; exit 2/3 for early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Stop-S4Api in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: BLOCKED rows are counted and the suite exits 1.'
            JwtConcurrency    = 'No Start-Job. Session tokens stay in the suite process.'
            SqlWrites         = 'Creates USERS/LANDS/LAND_DOCUMENTS and related rows; toggles DOCUMENT_TYPES.IS_REQUIRED_FOR_VERIFICATION and restores it in finally; deactivates a disposable admin. Business fixtures remain.'
            FixtureCleanup    = 'Required document-type flags are restored by the suite; business rows are intentionally left.'
            Acceptance        = 'Child exit 0; one TOTAL summary; PASS>0, FAIL=0, BLOCKED=0; no BLOCKED/CRITICAL/REQUIRED-SQL-FAILURES marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The suite has an explicit cleanup path and the exit handler rechecks PID/port ownership before stopping. The runner still verifies cleanup independently and must not infer PASS from exit 0 alone.'
        },
        @{
            Order             = 2
            Name              = 's4_admin'
            File              = 's4_admin.ps1'
            SummaryPattern    = '(?m)^ADMIN TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'No Start-Job. Session tokens stay in the suite process.'
            SqlWrites         = 'Creates USERS/LANDS/LAND_DOCUMENTS and a criteria version; toggles required document-type flags and restores them in finally. Fixtures remain.'
            FixtureCleanup    = 'Required flags restored; business rows intentionally left.'
            Acceptance        = 'Child exit 0; one ADMIN TOTAL summary; FAIL=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The finally cleanup reports a remaining PID, listener or identity file as a WARNING and throws, so exit 0 cannot hide failed cleanup.'
        },
        @{
            Order             = 3
            Name              = 's4_regression'
            File              = 's4_regression.ps1'
            SummaryPattern    = '(?m)^REGRESSION TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks; tokens are passed only in memory and never serialized to a job or child process.'
            SqlWrites         = 'Creates regression fixtures (users, lands, documents, offers/contracts as needed); rows remain after the run.'
            FixtureCleanup    = 'No automatic business-row cleanup.'
            Acceptance        = 'Child exit 0; one REGRESSION TOTAL summary; FAIL=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The suite uses concurrent HttpClient requests for race cases; the runner must not wrap it in a job or split the token from the process. Cleanup is explicit and independent of the exit handler.'
        },
        @{
            Order             = 4
            Name              = 's4_review'
            File              = 's4_review.ps1'
            SummaryPattern    = '(?m)^REVIEW TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: an early BLOCKED exits 2/3 and is detected from the marker; parsed FAIL is authoritative.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks for the document race; tokens never leave the process.'
            SqlWrites         = 'Creates review fixtures (users, lands, documents, race rows); no cleanup.'
            FixtureCleanup    = 'No automatic cleanup.'
            Acceptance        = 'Child exit 0; one REVIEW TOTAL summary with FAIL=0; no hidden FAIL result line, BLOCKED/CRITICAL/SQL marker, or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The suite now exits 1 on a calculated FAIL, so exit 0 no longer masks the summary; the runner still treats parsed totals as authoritative.'
        },
        @{
            Order             = 5
            Name              = 's4_review2'
            File              = 's4_review2.ps1'
            SummaryPattern    = '(?m)^REVIEW2 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: an early BLOCKED exits 2/3 and is detected from the marker; parsed FAIL is authoritative.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks; tokens never leave the process.'
            SqlWrites         = 'Creates review2 fixtures (users, lands, documents, race rows); no cleanup.'
            FixtureCleanup    = 'No automatic cleanup.'
            Acceptance        = 'Child exit 0; one REVIEW2 TOTAL summary with FAIL=0; no hidden FAIL result line, BLOCKED/CRITICAL/SQL marker, or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The suite now exits 1 on a calculated FAIL; parsed totals remain authoritative and no BLOCKED branch is silently green.'
        },
        @{
            Order             = 6
            Name              = 's4_checkpoint_a'
            File              = 's4_checkpoint_a.ps1'
            SummaryPattern    = ''
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $false
            EvidenceOnly      = $true
            ExitSemantics     = 'Evidence-only and deliberately non-green: exits 2 while C4 is BLOCKED (the known disposition); 0 only after a future explicit acceptance disposition.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: C4 is printed as BLOCKED and the suite exits 2; the runner also classifies any BLOCKED marker as NOT PASS.'
            JwtConcurrency    = 'No concurrency. The suite locally masks JWT-shaped values in printed response bodies.'
            SqlWrites         = 'Creates checkpoint users and a land; no cleanup.'
            FixtureCleanup    = 'No automatic cleanup.'
            Acceptance        = 'Evidence-only. Manual review must confirm C1=201, C2=409, C3=403, C5=403, and the own-lands list=200; C4 is known BLOCKED because no approved Investor fixture exists. Never automatic PASS. Separate approval is required for any acceptance decision.'
            Notes             = 'C4 cannot be resolved safely by a direct role/status update: it needs an Investor approved through the real approval flow and an explicit acceptance disposition. The runner reports EVIDENCE only for a clean evidence-only run; the current C4 BLOCKED line plus exit 2 is always NOT PASS.'
        },
        @{
            Order             = 7
            Name              = 's5_offers'
            File              = 's5_offers.ps1'
            SummaryPattern    = '(?m)^S5 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)(?:[ \t]*\(auth calls used: \d+\))?[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2 when the S5-55 BLOCKED branch is taken; exit 2/3 for other early BLOCKED conditions; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: S5-55 increments BLOCKED, the summary carries BLOCKED=1, and the suite exits 2.'
            JwtConcurrency    = 'Session tokens stay in the suite process; no Start-Job. Concurrent/request work must remain in-process.'
            SqlWrites         = 'Creates offers, negotiation history, and status changes; no cleanup. S5-55 is blocked when UX_OFFERS_ACTIVE_INVESTOR_LAND is absent.'
            FixtureCleanup    = 'No automatic cleanup.'
            Acceptance        = 'Child exit 0; one S5 TOTAL summary with FAIL=0 and BLOCKED=0; no BLOCKED S5-55 or any other BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left. A blocked marker or BLOCKED>0 is NOT PASS.'
            Notes             = 'If the unique index migration is absent, the suite is NOT PASS, never a green skip. The suite counts the blocked branch and exits 2; fix the environment or the suite separately.'
        },
        @{
            Order             = 8
            Name              = 's6_contracts'
            File              = 's6_contracts.ps1'
            SummaryPattern    = '(?m)^S6 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED. With FAIL=0 the script exits 0 after the finally cleanup.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler. Cleanup errors are reported and thrown, not suppressed.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks; tokens never leave the process.'
            SqlWrites         = 'Reads DB_NAME before the app start; creates contracts, reservations, offers and related fixtures; no business-row cleanup. The forced-expiry UPDATE is restored in finally and independently re-read.'
            FixtureCleanup    = 'No automatic business-row cleanup; the temporary forced-expiry value is restored.'
            Acceptance        = 'Child exit 0; one S6 TOTAL summary with FAIL=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; independent cleanup confirms port 5199 free, identity file absent, no runner-owned suite/API process.'
            Notes             = 'Cleanup is explicit and independently verified. The forced-expiry SQL mutation is test-only and exception-safe: the original EXPIRES_AT is restored in finally and checked again.'
        },
        @{
            Order             = 9
            Name              = 's7_reservations'
            File              = 's7_reservations.ps1'
            SummaryPattern    = '(?m)^S7R RESERVATIONS TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'Explicit exit 1 when calculated FAIL>0; exit 0 otherwise; exit 2/3 for early BLOCKED.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler. Cleanup errors are reported and thrown, not suppressed.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks; tokens never leave the process.'
            SqlWrites         = 'Reads DB_NAME before the app start; creates reservations, contracts and related fixtures; no business-row cleanup. A test-only release-state UPDATE is restored if the manual-release attempt does not reach its expected state.'
            FixtureCleanup    = 'No automatic business-row cleanup; the temporary release-state mutation has exception-safe restoration and final-state verification.'
            Acceptance        = 'Child exit 0; one S7R RESERVATIONS TOTAL summary with FAIL=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; independent cleanup confirms port 5199 free, identity file absent, no runner-owned suite/API process.'
            Notes             = 'Cleanup is explicit and independently verified. The ended-contract release-state SQL setup restores the workflow evidence if the manual release does not reach the expected terminal state, then verifies the final row.'
        },
        @{
            Order             = 10
            Name              = 'p2_change_requests'
            File              = 'p2_change_requests.ps1'
            SummaryPattern    = '(?m)^PHASE2 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when calculated FAIL>0; exit 2/3 for early BLOCKED; 0 otherwise.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'In-process System.Net.Http.HttpClient tasks; tokens never leave the process.'
            SqlWrites         = 'Uses fixture-local UPDATEs on rows it created: LAND_CHANGE_REQUESTS snapshot fields and LANDS.VERIFIED_AGAINST_CRITERION_ID (NULL then restored). Creates change requests and history rows; no business-row cleanup.'
            FixtureCleanup    = 'The criterion pin is restored in exception-safe finally blocks and independently re-read after each block; deliberately malformed snapshot fixtures remain on the run-owned rows.'
            Acceptance        = 'Child exit 0; one PHASE2 TOTAL summary with FAIL=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'The pin is the only temporary mutable fixture state and is now restored even on exception, with an independent final read. Snapshot corruption is intentional permanent test data on the run-owned rows; it is never applied to pre-existing rows.'
        },
        @{
            Order             = 11
            Name              = 'parallel-approval'
            File              = 'parallel-approval.ps1'
            SummaryPattern    = ''
            SuccessPattern    = '(?m)^PARALLEL APPROVAL PASS\b'
            FailurePattern    = '(?m)^FAILURES:'
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 1 when FAILURES is printed; exit 2/3 for early BLOCKED; on success prints PARALLEL APPROVAL PASS and exits 0 after the finally cleanup.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: early BLOCKED exits 2/3 and is detected from the marker.'
            JwtConcurrency    = 'Two in-process System.Net.Http.HttpClient POST tasks fired concurrently; the admin token is held only in memory and never serialized.'
            SqlWrites         = 'Creates a grid, lands, offers and contracts; the suite explicitly leaves business fixtures for inspection and performs no business-row cleanup.'
            FixtureCleanup    = 'Business fixtures intentionally left in the database; the harness API/outbox is cleaned explicitly.'
            Acceptance        = 'Child exit 0; exactly the PARALLEL APPROVAL PASS marker and no FAILURES marker; no BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'No numeric total exists. The runner requires the success marker and rejects a missing/ambiguous marker; cleanup failure is reported as a WARNING and throws.'
        },
        @{
            Order             = 12
            Name              = 's4_c4_acceptance'
            File              = 's4_c4_acceptance.ps1'
            SummaryPattern    = '(?m)^C4 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)[ \t\r]*$'
            SuccessPattern    = ''
            FailurePattern    = ''
            RequireSummary    = $true
            EvidenceOnly      = $false
            ExitSemantics     = 'exit 0 only when every C4 assertion passes and at least one known eligible verified land is visible; exit 1 on any FAIL; exit 2 when the known-verified-land prerequisite is BLOCKED; exit 3 on cleanup failure.'
            CleanupOwner      = 'Explicit Complete-TestApiCleanup in finally (identity/port verified) plus the identity-aware PowerShell.Exiting handler.'
            MaskedBlocked     = 'No: a missing eligible Verified land is printed as BLOCKED with BLOCKED=1 and exit 2; the runner treats it as NOT PASS.'
            JwtConcurrency    = 'No concurrency and no Start-Job. Session tokens and the disposable SuperAdmin password stay in the suite process and are never printed.'
            SqlWrites         = 'No SQL. Through the API it creates two fresh users, one run-owned Draft land and one approval write on the new Investor; it never creates or approves a Verified land.'
            FixtureCleanup    = 'Business rows are intentionally left for inspection; the harness API/outbox is cleaned explicitly.'
            Acceptance        = 'Child exit 0; one C4 TOTAL summary with PASS>0, FAIL=0 and BLOCKED=0; no BLOCKED/CRITICAL/SQL marker or cleanup warning; port 5199 free, identity file absent, no runner-owned suite/API process left.'
            Notes             = 'Deliberately separate from the evidence-only s4_checkpoint_a entry and selected explicitly with -SuiteNames s4_c4_acceptance; it does not reinterpret or bypass the checkpoint. It requires a pre-existing investor-visible Verified land and refuses to pass an empty list. The approval actor is the retained disposable SuperAdmin; only the Investor id captured from this run is approved.'
        }
    )
}
