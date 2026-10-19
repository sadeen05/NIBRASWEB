# Email verification — configuration, deployment and security notes

Scope: the Phase-1 "F1" email verification feature for newly created Landlord, Investor and
Admin accounts. This document covers the **configuration** the feature needs, the requirements
a production host has to meet, the concurrency and uniqueness guarantees, and what is still
open. It is deliberately implementation-agnostic about the HTTP surface, which is added in a
later increment.

## 1. Delivery modes (`Email:DeliveryMode`)

| Mode | Where it may run | Behaviour |
|---|---|---|
| `Smtp` | **Everywhere** (the only mode allowed outside Development) | The message is handed to the configured SMTP server. A failure is reported, never swallowed. |
| `FileOutbox` | **Development only** | The message is written as one JSON file into a private per-run directory, so a test can pick the activation link up without a mail server. The sink refuses to run outside Development and its path may not sit inside a Git working tree. |
| `Disabled` | Development only | Every send fails closed with a controlled "not delivered" answer. This is the shipped default: a host that is not configured for mail can never create an account that pretends to have been verified. |

The mode is validated **at startup** (`EmailOptionsValidator`). Outside Development a mode other
than `Smtp` makes the host refuse to start, so email verification can neither be switched off nor
diverted into the development outbox in a real deployment.

## 2. Production requirements (SMTP)

A production host must provide, through configuration or user-secrets (never in the repository):

| Key | Requirement |
|---|---|
| `Email:DeliveryMode` | must be `Smtp`. `Disabled` and `FileOutbox` are refused outside Development. |
| `Email:SmtpHost` | the SMTP server name. Required in `Smtp` mode. |
| `Email:SmtpPort` | 1–65535 (587 with STARTTLS is the default). |
| `Email:SmtpEnableSsl` | keep `true`; TLS is negotiated with the server. |
| `Email:FromAddress` | the sender address. Required in `Smtp` mode, and it must look like an address — a misconfigured sender is refused at startup rather than producing mail that is rejected later. |
| `Email:SmtpUser` / `Email:SmtpPassword` | optional basic authentication. When a user is configured, a password is required as well. **The password belongs in user-secrets or an environment variable** (`Email__SmtpPassword`), never in `appsettings.json` and never in a commit. |
| `Email:RegistrationTokenTtlHours` | lifetime of a registration token (default 24). |
| `Email:EmailChangeTokenTtlHours` | lifetime of an address-change token (default 1). |
| `Email:AdminActivationTokenTtlHours` | lifetime of an admin activation token (default 24). |
| `Email:ResendCooldownSeconds` | minimum gap between two resends for one account (default 60). |
| `Email:ResendDailyCap` | maximum number of verification mails per account per day (default 10). |

Secrets handling: the password is read from configuration into the options object and handed to
the SMTP client only. It is never written to a log, never returned in a result, never included
in an audit event and never included in an error message. The delivery log records a **masked
recipient** and the outcome only.

## 3. Development outbox

- The path (`Email:OutboxPath`) must be **absolute** and must **not** be inside a Git working
  tree — the outbox holds real activation links and would otherwise be committed by accident.
  The check runs at startup and again at send time, and an unresolvable path is treated as
  unsafe.
- The directory is created with owner-only access where the platform supports it
  (`File.SetUnixFileMode` on Unix). On Windows the directory inherits the access control of the
  user's temporary path — verified on this host: the directory and the file grant access only to
  `NT AUTHORITY\SYSTEM`, `BUILTIN\Administrators` and the owning user, so no other account can
  read an activation link. Windows offers no portable way to narrow it further, so the outbox
  must stay inside that per-user temporary path.
- One file per message, written to a temporary name and renamed, so a reader never sees a half
  message. The file name carries a timestamp and a random id — never the address or the subject.
- The message body is never written to the application log.

## 4. Token lifetimes and purposes

| Purpose | Lifetime | Notes |
|---|---|---|
| `Registration` | 24 h | A newly registered Landlord or Investor must prove the address before the account can be used. |
| `EmailChange` | 1 h | The active address is only replaced by a successful verification of the new one. The new address must be free everywhere (also for a soft-deleted account and as another account's pending address). The token is **bound to the requested address**; the consumption refuses it when the account's pending address is no longer that address. Issuing one requires the **current password** of the account. |
| `AdminActivation` | 24 h | Reserved for an Admin account created by a SuperAdmin. The token proves the address **and** lets the new Admin set their own password, so no password is ever chosen by somebody else. The purpose is refused for an account that does not hold the Admin role, at issuance *and* at consumption. |

Token handling: 32 bytes from a cryptographically secure random source, base64url encoded. Only
the **SHA-256 hash** is stored, so a leaked database row cannot be replayed. A token is bound to
one account and one purpose, expires, and can be consumed exactly once; issuing a new token for
the same purpose invalidates the earlier ones, so only the newest link works.

## 5. Concurrency, atomicity and uniqueness

- **One live token per account and purpose.** Every issuance locks the account row
  (`UPDLOCK, HOLDLOCK` on the primary key — an index seek) before it invalidates the earlier
  tokens of the same purpose and inserts the new one, so two concurrent issuances cannot
  interleave and leave two usable links. The invalidation and the insertion share one
  transaction, so a **failed issuance leaves the previous, still usable token untouched**.
- **One lock order everywhere.** Every path that touches both the account and its tokens locks
  the **account first** and the token table second — the issuance, the consumption, the resend
  and the administrative correction of an address. The administrative path therefore takes the
  account lock through the token service (`LockAccountAsync`) before it retires the tokens, so
  it cannot deadlock against the token service.
- **Consumption is atomic.** The token is read to learn the account, the account row is then
  locked, and the consumption is one conditional update — exactly one caller can win the row. A
  token issued for another purpose is refused and stays usable for its own purpose; an expired
  or already-used token is refused, and a token whose request was superseded by a newer one is
  refused as stale.
- **A token confirms only the address it is bound to.** An address-change token stores the
  requested address, and the consumption compares it with the account's current pending address;
  a mismatch means the request was replaced, and nothing is applied.
- **An address change requires the current password.** A hijacked session alone cannot move the
  address of an account: the password is confirmed before a new address is claimed, and an
  account whose stored hash is unusable can never confirm one.
- **The activation token is never consumed by a plain address verification**: completing it
  requires the password setup, so an account can never end up "verified" without a password.
- A consumed token, the account state change and the security audit event are committed in one
  transaction, and the consumption itself is one conditional database update, so two concurrent
  uses of the same token cannot both succeed. A pending address that was taken by another
  account in the meantime is reported as a conflict instead of surfacing as a server error.
- **The plaintext token never leaves the process**: it is excluded from serialization, redacted
  in every `ToString`, never logged, never stored (only its SHA-256 hash is), and never part of
  an audit event or an error message. The delivery step receives it once, in process.
- **Resend limits count the account, not the purpose**, and count every issuance attempt — the
  token rows are never deleted, so a resend can neither multiply the mail volume of one account
  nor bypass the daily cap. The limits are read under the account lock, so two parallel resends
  cannot both pass them.
- **Address uniqueness.** The database collation is case-insensitive
  (`SQL_Latin1_General_CP1_CI_AS`), the unique index on `USERS.EMAIL` has no filter (so a
  soft-deleted account still owns its address), and every check reads the rows with
  `IgnoreQueryFilters`, so it agrees with the index. A claim also has to be free as the
  **pending** address of another account, not only as an active one. The uniqueness of the
  pending address and of the live token is additionally enforced by the two filtered unique
  indexes listed in §7.

## 6. Every path that writes an address

| Path | What it writes | Status |
|---|---|---|
| `AuthService.RegisterAsync` | the address of a new, **unverified** account | By design. Note the side effect: an unverified registration **reserves** the address (the unique index covers it) until the account is deleted, so a third party can squat an address — the smallest safe follow-up is a release policy for unverified accounts (delete on expiry, or an administrative "release the address" action). |
| `UserService.CreateAsync` | the address of an admin-created account | By design; the account stays unusable until it is verified. |
| `UserService.UpdateAsync` | the address of an **existing** account | **Changed (security).** A direct address change of a **verified** account (including a legacy account, whose `EMAIL_VERIFIED` is NULL and therefore counts as verified) is refused with a controlled **409**: the address may only be replaced by a successful verification of the new one. For an **unverified** account the correction is allowed, because no address was ever proven — and it is complete in one transaction: the account stays unverified (`EMAIL_VERIFIED = false`, `EMAIL_VERIFIED_AT` cleared), an outstanding address change of the owner is **dropped** (`PENDING_EMAIL` cleared), and **every** live link of the account is retired, so a link sent to the previous address or for the pending address can no longer confirm anything. The conflict check covers the active **and** the pending address of every account, soft-deleted ones included, and a unique-index violation is answered with the same 409. |
| `EmailVerificationService` (verified swap) | the active address, only after a successful verification | Correct: the pending address must be the one the token is bound to, and the current password must have been confirmed when the change was requested. |

### Unusable password hashes
A stored hash that is not a valid BCrypt hash — the placeholder of the seeded system account
(`"N/A"`), an empty value, or a value written by an older tool — can never authenticate
anybody. It is therefore treated as a **failed login (401)**, never as a server error: the
verification helper catches an unreadable hash and reports a mismatch. The generic "incorrect
email or password" answer and the timing equaliser for an unknown address are unchanged.

## 7. The database-level guarantees (applied)

```sql
-- One account may hold at most one pending address, and one address may be pending for at
-- most one account. Created without a data change: every existing row has
-- PENDING_EMAIL = NULL and the filter excludes NULLs.
CREATE UNIQUE INDEX UX_USERS_PENDING_EMAIL
    ON USERS (PENDING_EMAIL)
    WHERE PENDING_EMAIL IS NOT NULL;

-- At most one live token per account and purpose. The issuance retires every earlier token
-- of that purpose (consumed or expired) in the same transaction before it inserts the new
-- one, so the slot is always free; a filtered index may not use GETDATE(), so expiry is
-- handled by that retirement and not by the filter.
CREATE UNIQUE INDEX UX_EMAIL_VERIFICATION_TOKENS_LIVE
    ON EMAIL_VERIFICATION_TOKENS (USER_ID, PURPOSE)
    WHERE CONSUMED_AT IS NULL;
```

Both are in migration `20261019174547_EmailVerificationUniqueness`, which is applied on the
confirmed test target. Rolling it back only drops the two indexes — no data is touched — and
merely removes the database-level backstop; the application-level checks keep working.

**No `CHECK` constraint is proposed for `PURPOSE` or `EVENT_TYPE`:** the schema carries no
`CHECK` constraint anywhere, and the values are validated in the application against the closed
sets in `Enums\EmailVerificationPurposes` and `Enums\UserSecurityEventTypes`, exactly like
`LAND_CHANGE_REQUESTS.STATUS` and `LAND_DOCUMENTS.STATUS`.

## 8. Audit trail

`USER_SECURITY_EVENTS` is append-only: the service offers no way to change or remove a recorded
event, the event type must come from the closed set in `Enums\UserSecurityEventTypes`, and the
free-text context is stripped of control characters and truncated.

## 9. What exists today

- **Schema (applied):** `USERS.EMAIL_VERIFIED` / `EMAIL_VERIFIED_AT` / `PENDING_EMAIL`,
  `EMAIL_VERIFICATION_TOKENS`, `USER_SECURITY_EVENTS` (migration
  `20261019165813_EmailVerification`), plus the two uniqueness indexes (migration
  `20261019174547_EmailVerificationUniqueness`).
- **Infrastructure:** the mailer abstraction with the SMTP, development-outbox and fail-closed
  senders, the configuration and its startup validation, and the append-only audit service.
- **Token service:** issuance (registration, address change with current-password
  confirmation, admin activation), atomic consumption, resend invalidation with cooldown and
  daily cap, and the account-bound, purpose-checked, expiring, single-use rules.
- **Account safety:** a direct address change of a verified account is refused with a 409, a
  correction of an unverified account keeps it unverified and retires every stale link and the
  pending change, and an unusable password hash answers a generic 401 instead of a server error.

**Not implemented yet (later increments):** the HTTP endpoints, the mail bodies and the
bilingual message templates, the login and JWT gates, the frontend surfaces, the admin
onboarding with password setup, and the owner-facing address change with current-password
confirmation (the service rule exists, the endpoint does not).
