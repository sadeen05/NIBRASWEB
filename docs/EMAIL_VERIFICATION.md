# Email verification — configuration, deployment and security notes

Scope: the Phase-1 "F1" email verification feature for newly created Landlord, Investor and
Admin accounts. This document covers the **configuration** the feature needs, the requirements
a production host has to meet, the concurrency and uniqueness guarantees, the admin activation
and invitation-delivery flow, and the open items.

## 1. Delivery modes (`Email:DeliveryMode`)

| Mode | Where it may run | Behaviour |
|---|---|---|
| `Smtp` | **Everywhere** (the only mode allowed outside Development) | The message is handed to the configured SMTP server. A failure is reported, never swallowed. The SMTP timeout is bounded (30 s), so a mail server that does not answer cannot hold a request open. |
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
| `Email:FrontendBaseUrl` | absolute address of the frontend, used to build the activation and the verification link. Required whenever a delivery mode is configured, and **https is required outside Development** — the link carries a token, so it must never travel over an unencrypted connection. |
| `Email:ActivationPath` | the frontend activation page (default `/activate-admin`). It must be a **fixed local path**: it starts with a single `/` and carries no query, no fragment, no scheme and no whitespace, so the link can never be pointed somewhere else. Required whenever a delivery mode is configured. |
| `Email:VerificationPath` | the frontend registration verification page (default `/verify-email`). The same **fixed local path** rules apply, and it is required whenever a delivery mode is configured. |
| `Email:RegistrationTokenTtlHours` | lifetime of a registration token (default 24). |
| `Email:EmailChangeTokenTtlHours` | lifetime of an address-change token (default 1). |
| `Email:AdminActivationTokenTtlHours` | lifetime of an admin activation token (default **24**, and the invitation states the expiry). |
| `Email:ResendCooldownSeconds` | minimum gap between two resends for one account (default 60). It applies to an administrator invitation as well, so an invitation cannot be used to flood an address. |
| `Email:ResendDailyCap` | maximum number of verification mails per account per day (default 10). It applies to administrator invitations as well. |
| `RateLimit:ActivationPerMinute` | attempts per minute and address on the admin activation endpoint (default 5). It has its **own** budget, so a flood of activation attempts can never exhaust the sign-in and registration budget of the same address. |
| `RateLimit:VerificationPerMinute` | attempts per minute and address on the address-confirmation endpoint (default 5). It has its **own** budget, so a flood of confirmation attempts can never exhaust the sign-in budget. |
| `RateLimit:VerificationResendPerMinute` | requests per minute and address on the request for a new verification mail (default 5). It is the only anonymous endpoint that can cause mail to be sent, so it has its own budget on top of the per-account cooldown and the per-account daily cap. |

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
| `AdminActivation` | 24 h | Reserved for an Admin account created by a SuperAdmin. The token proves the address **and** lets the new Admin set their own password, so no password is ever chosen by somebody else. The purpose is refused for an account that does not hold the Admin role, and for an account that is **not explicitly unverified** — a legacy account (`EMAIL_VERIFIED IS NULL`) and an already activated account are both refused, so an invitation can never take over a legacy account nor reset the password of an account that is in use. |

Token handling: 32 bytes from a cryptographically secure random source, base64url encoded. Only
the **SHA-256 hash** is stored, so a leaked database row cannot be replayed. A token is bound to
one account and one purpose, expires, and can be consumed exactly once; issuing a new token for
the same purpose invalidates the earlier ones, so only the newest link works.

## 5. Concurrency, atomicity and uniqueness

- **One live token per account and purpose.** Every issuance locks the account row
  (`UPDLOCK, HOLDLOCK` on the primary key — an index seek) before it invalidates the earlier
  tokens of the same purpose and inserts the new one, so two concurrent issuances cannot
  interleave and leave two usable links. The invalidation, the new token and the audit events
  share one transaction, so a **failed issuance leaves the previous, still usable token untouched**
  and the trail can never disagree with the tokens.
- **One lock order everywhere.** Every path that touches both the account and its tokens locks
  the **account first** and the token table second — the issuance, the consumption, the resend,
  the administrative correction of an address and the admin activation. The administrative path
  takes the account lock through the token service (`LockAccountAsync`) before it retires the
  tokens, so it cannot deadlock against the token service.
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
  cannot both pass them: the first wins, the second is refused by the cooldown.
- **Address uniqueness.** The database collation is case-insensitive
  (`SQL_Latin1_General_CP1_CI_AS`), the unique index on `USERS.EMAIL` has no filter (so a
  soft-deleted account still owns its address), and every check reads the rows with
  `IgnoreQueryFilters`, so it agrees with the index. A claim also has to be free as the
  **pending** address of another account, not only as an active one. The uniqueness of the
  pending address and of the live token is additionally enforced by the two filtered unique
  indexes listed in §9.

## 6. Every path that writes an address

| Path | What it writes | Status |
|---|---|---|
| `AuthService.RegisterAsync` | the address of a new account | **Not yet verified.** The account is created today **without** a verification flag, so it keeps the legacy value (`NULL`, treated as verified); the flag flip and the verification mail belong to the later increments (§8, §11.1). The intended side effect of the flip: an unverified registration **reserves** the address (the unique index covers it) until the account is removed, so a third party can squat an address — the recovery policy is in §8. |
| `UserService.CreateAsync` | the address of an admin-created account | For a **new Admin** the account starts unverified, with a password hash nobody can match, and the activation invitation is delivered in process (see §7). For an **admin-created Landlord or Investor** the account keeps the legacy value (`EMAIL_VERIFIED = NULL`, treated as verified) — see the release-blocking item in §11.1. |
| `UserService.UpdateAsync` | the address of an **existing** account | **Changed (security).** A direct address change of a **verified** account (including a legacy account, whose `EMAIL_VERIFIED` is NULL and therefore counts as verified) is refused with a controlled **409**: the address may only be replaced by a successful verification of the new one. For an **unverified** account the correction is allowed, because no address was ever proven — and it is complete in one transaction: the account stays unverified (`EMAIL_VERIFIED = false`, `EMAIL_VERIFIED_AT` cleared), an outstanding address change of the owner is **dropped** (`PENDING_EMAIL` cleared), and **every** live link of the account is retired, so a link sent to the previous address or for the pending address can no longer confirm anything. The conflict check covers the active **and** the pending address of every account, soft-deleted ones included, and a unique-index violation is answered with the same 409. |
| `EmailVerificationService` (verified swap) | the active address, only after a successful verification | Correct: the pending address must be the one the token is bound to, and the current password must have been confirmed when the change was requested. |

### Unusable password hashes
A stored hash that is not a valid BCrypt hash — the placeholder of the seeded system account
(`"N/A"`), an empty value, or a value written by an older tool — can never authenticate
anybody. It is therefore treated as a **failed login (401)**, never as a server error: the
verification helper catches an unreadable hash and reports a mismatch. The generic "incorrect
email or password" answer and the timing equaliser for an unknown address are unchanged.

## 7. Admin onboarding, activation and invitation delivery

**Creation (SuperAdmin only).** `POST /api/users` with `roleId = 3` is restricted to a
SuperAdmin by the existing role check (a regular Admin may only create Landlords and
Investors), and public registration stays Landlord/Investor only. The account is created:
- **unverified** (`EMAIL_VERIFIED = false` — explicitly, not NULL),
- with a password hash that **nobody can match**: 32 random bytes are hashed with BCrypt and
  thrown away, so the account has no usable password and **cannot be signed in before the
  activation**,
- with the password field of the request **ignored** — a super administrator can neither know
  nor choose the final password,
- with the audit event `AdminAccountCreated`, naming the acting super administrator,
- and with the activation invitation **delivered in process** (below).

**Who may be activated.** Only an account that is **explicitly unverified** (`EMAIL_VERIFIED =
false`) and holds the **Admin** role, is not deleted, and has never been activated. A legacy
account (`EMAIL_VERIFIED = NULL`) and an already activated account are refused at issuance, at
reissue **and** at redemption, so an invitation can never take over a legacy account and can
never reset the password of an account that is in use. A soft-deleted account and a non-Admin
role are refused as well.

**Invitation delivery.** The invitation service issues (or reissues) the activation token and
delivers it by mail:
- the **token never leaves the service**: it is handed to the mailer in process, it is never
  returned to the caller, never logged and never part of a response or an audit event;
- the message is **bilingual** (an English and an Arabic block) and states that the link expires
  after 24 hours;
- the activation link carries the token in the **URL fragment**
  (`{FrontendBaseUrl}{ActivationPath}#token=…`): a fragment is never transmitted to a server, so
  the token cannot appear in an access log, a proxy log or a referrer. The activation page reads
  it, redeems it and strips it from the address bar and from the history before the first paint
  (see §10.2);
- issuing an invitation **retires the previous one** in the same transaction, so only the
  newest link works;
- the **resend cooldown (60 s) and the daily cap (10 per account and day) apply** to an
  administrator invitation as well, and they are evaluated under the account lock, so two parallel
  requests cannot both pass them: the first wins and the second is refused by the cooldown;
- on a **delivery failure** the account stays unverified and unusable, the audit records
  `VerificationEmailDeliveryFailed`, and the SuperAdmin can send the invitation again — the retry
  issues a new link and retires the failed one, so a retry never leaves two usable invitations
  and never creates a second account;
- a failing audit write does not change the delivery outcome; it is logged instead;
- a delivery failure never marks the account activated or verified.

**Redemption.** The invitation token is redeemed together with the password the new Admin
chooses. The redemption requires, in one transaction: the token exists and is **unused**, its
**purpose** is `AdminActivation`, it has **not expired**, the account exists, is **not deleted**
and holds the **Admin** role, the account is **not already activated**, no newer invitation
exists, and the atomic consumption update wins the row. The password is validated with the
existing policy (at least 10 characters, at most 128, at most 72 UTF-8 bytes). The commit sets
the new password hash, marks the address verified and appends the audit events `AdminActivated`
and `AdminPasswordSet`. A failure rolls everything back, and two concurrent redemptions have
exactly one winner.

### Endpoint contracts
```
POST /api/users                      (SuperAdmin only for roleId = 3)
201  ->  { "user": { ... }, "invitation": "not_applicable" | "handed_over" | "failed",
                  "invitationMessage": "... (only when it needs attention)" }
        The account exists. The invitation status reports what happened at the mailer - it
        never claims that a message was received. A failed invitation leaves the account
        inactive and can be sent again; it never creates a second account.

POST /api/users/{id}/admin-invitation      (SuperAdmin only, no body)
202  ->  the invitation was handed over for delivery; the token is never in the response
409  ->  the account cannot receive an invitation (not an active Admin, or already activated)
503  ->  the delivery failed; the account stays inactive and the invitation can be sent again

POST /api/auth/activate-admin
Authorization: anonymous - the single-use token is the only credential
Rate limit:     the dedicated "activation" policy (RateLimit:ActivationPerMinute, default 5)
Body:           { "token": "<from the invitation link>", "password": "<the new Admin's own password>" }

204  ->  activated; the account can sign in
400  ->  the activation link is not valid (unknown, expired, already used, wrong purpose,
          superseded) - always the same generic message, so the endpoint is not an oracle;
          a password that does not meet the policy is answered with the policy message
409  ->  the account cannot be activated (already active, not an Admin, deleted) or the
          address is already in use
429  ->  rate limited
```
No endpoint returns the token, the password or the hash, and none issues a session: the new Admin
signs in with their own password afterwards, so the JWT behaviour is unchanged.

## 8. Registration verification of new Landlord and Investor accounts

The **delivery layer** and the **endpoints** of the registration verification exist (the
bilingual mail, the token lifecycle, the audit trail, the confirmation endpoint, the anonymous
resend and the administrative resend). What is **not** part of it yet is the sign-in gate and
the flag flip: no new account is required to verify anything yet, because no new account is
created unverified yet — see §11.1 for the remaining work and the order it has to follow.

**Who may receive a verification.** A registration token is only issued for an account that is
**explicitly unverified** (`EMAIL_VERIFIED = false`) and that holds the **Landlord** or the
**Investor** role. The rules sit inside the token service, under the account lock, so every
issuance path is covered — the first verification and every resend:

- a **legacy account** (`EMAIL_VERIFIED IS NULL`) counts as verified: it can never receive a
  registration token, its state is never changed, and it can never be pushed into the
  unverified state by a mail;
- an account that is already verified can never receive one either;
- an **administrator account** can never receive one: it has its own activation flow, which
  also sets the password. A registration token would mark it verified without that password
  setup, and would then block its activation (an activation token is only issued for an
  account that is not verified yet). The role is re-checked when the token is redeemed, so a
  token that was issued before a role change is not honoured afterwards.

⇒ **The account has to be created unverified before its first verification is issued.** The
order is enforced by the server: an issuance for an account that is not explicitly unverified
is refused, so the mistake cannot pass silently.

**What the redemption re-checks.** A registration token may only perform the transition it was
issued for, so the consumption re-reads the account under its lock and refuses the token when

- the account does not exist any more or is **deleted**;
- the account does not hold the **Landlord** or **Investor** role any more;
- the account is **not explicitly unverified** any more (it was verified in the meantime, or it
  is a legacy account, whose null flag counts as verified and which is therefore never
  changed);
- the **address of the account differs** from the address the token was **bound to** when it
  was issued — a token proves exactly one address, so it can never confirm a corrected one.

The token is bound to its address in the token row itself (`PREVIOUS_EMAIL`, the column the
address-change binding already uses), so no schema change was needed.

**The mail.** Bilingual (an English and an Arabic block), it states that the link expires after
24 hours and carries the token in the **URL fragment** (`{FrontendBaseUrl}{VerificationPath}#token=…`).
A fragment is never transmitted to a server, so the token cannot appear in an access log, a
proxy log or a referrer. The token is handed to the mailer **in process**: it is never returned
to the caller, never logged, never stored (only its SHA-256 hash is) and never part of an audit
event.

**Delivery failure.** A failed delivery leaves the account **unverified** and unusable, records
`VerificationEmailDeliveryFailed` in the audit trail, and is recoverable: the next request
issues a new token and retires the failed one, so a retry never leaves two usable links and
never creates a second account. Nothing is ever verified automatically — a delivery failure
never flips the verification flag, and the account stays exactly as it was.

**Retry and throttling.** The resend cooldown (60 s) and the daily cap (10 mails per account
and day) apply to a registration verification as well, and they are read under the account
lock, so two parallel requests cannot both pass them. An **anonymous** resend must answer
**the same way in every case** — a missing account, an account that is already verified, an
active cooldown and a reached cap must not be distinguishable, or the endpoint would reveal
whether the address exists and whether the account is still unverified. The account-specific
throttle is therefore never visible in the answer.

**Address squatting — the recovery policy.** An unverified registration **reserves** its
address: the unique index on `USERS.EMAIL` has no filter, so the row owns the address for as
long as it exists, and a third party can register an address that somebody else wants. The
recovery is deliberately **not automatic** and **not destructive**:

- the application **never deletes** an unverified account on its own, and **never verifies**
  one automatically;
- the **soft delete of the application does not release the address** — the unique index has no
  filter, so a deleted row still owns it. A hard delete would release it, but it would also
  destroy the account row, its security events and the evidence of the squatting, so it is
  **not** the recovery mechanism and is not recommended;
- **the address belongs to whoever can prove control of the mailbox** — the verification link
  is that proof. The recovery is therefore an **ownership-based release**: the party that
  proves the mailbox takes the address, while the account that squatted it keeps its row, its
  security events and its whole history. Only the address is released (the row is re-addressed
  to a non-routable placeholder), the verification state is untouched, and the account can
  neither sign in nor claim the address again. The release is a **one-way, operator-driven,
  audited action** (recorded as a security event) — never automatic, never on a timer, and
  never performed by the account itself;
- the action that performs the release **does not exist yet**. It needs a deliberate decision
  (the placeholder form, the audit event, the authorization), so it belongs to a later
  increment. Until it exists a squatted address stays reserved and the case is an operational
  matter of the hosting organisation; the application itself offers no release, no deletion and
  no automatic verification. That is the safe interim state: an address that cannot be
  released is a nuisance, while an automatic release is a hole.

**A stale token can never confirm a corrected address.** An administrative correction retires
**every** live token of the account (all purposes) in the same transaction as the address
change, and a registration token is additionally **bound to the address it was issued for**: the
consumption compares the bound address with the account's current address and refuses the token
when they differ, so even a token that survived the retirement could not confirm the corrected
address.

### Endpoint contracts
```
POST /api/auth/verify-email
Authorization: anonymous - the single-use token is the only credential
Rate limit:     the dedicated "verification" policy (RateLimit:VerificationPerMinute, default 5)
Body:           { "token": "<from the verification link>" }

204  ->  the address is confirmed; the account may sign in
400  ->  the verification link is not usable (unknown, expired, already used, wrong purpose,
          superseded, the address changed, or the account is not eligible) - always the same
          generic message, so the endpoint is not an oracle
409  ->  the address was claimed by another account in the meantime
429  ->  rate limited

POST /api/auth/resend-verification
Authorization: anonymous - the address in the body is the only input
Rate limit:     the dedicated "verification-resend" policy
                 (RateLimit:VerificationResendPerMinute, default 5)
Body:           { "email": "<the address the account was created with>" }

202  ->  always. A missing account, an account that is already verified, an ineligible
         account, an active cooldown, a reached daily cap, a refused request and a failed
         delivery all produce the same answer, so the endpoint never reveals whether an
         address belongs to an account. The per-account cooldown and the daily cap are
         enforced by the token service under the account lock; the external limit may answer
         429.
         The work is handed to the managed delivery worker, so neither the answer nor its
         duration depends on the account state or on the mail hand-off: the endpoint is not a
         timing oracle. A delivery that the **mailer** refuses is recorded in the audit trail
         (`VerificationEmailDeliveryFailed`). A request whose work is **lost** - because the
         host stopped, or because the process ended - is **not** audited: a graceful stop
         counts and logs it, a sudden crash cannot even count it reliably, and a cancelled
         delivery is not a confirmed non-delivery at all. The recipient then finds no mail and
         requests another one once the cooldown has elapsed.

POST /api/users/{id}/verification      (SuperAdmin only, no body)
202  ->  the verification mail was handed over for delivery; the token is never in the response
409  ->  the account cannot receive a verification mail (not a Landlord or an Investor, already
         verified, deleted, or not found)
503  ->  the delivery failed; the account stays unverified and the verification can be sent again
```
No endpoint returns the token, and no endpoint verifies an account directly: the only way to
confirm an address is a valid, single-use registration token. The administrative resend can
never activate an administrator account — that flow has its own invitation — and it never
verifies anything by itself.

### The delivery worker

The anonymous resend hands its work to **one managed worker** with a **bounded queue** instead
of starting a task per request:

- the queue holds at most **100** pending requests, and a request that does not fit is
  **refused** instead of waiting: the request that offers the work is never blocked, and no
  unlimited number of tasks is created. A refused request is counted and logged (the first
  refusal and every fiftieth one), and the anonymous caller is not told anything;
- the queue carries **the minimum**: the address of the account and the remote address of the
  request. The **verification token is never part of it** — it is created inside the worker,
  in the worker's own scope, so no secret rests in the queue and no secret passes between two
  components;
- the worker performs one delivery at a time, each in its own scope with its own database
  context, so the amount of work, the number of database contexts and the number of mail
  hand-offs are all limited. The per-account cooldown, the daily cap, the atomic issuance and
  the delivery audits are the ones of the token service and the delivery service — unchanged;
- a failure of **one** delivery stays isolated: it is counted and the worker continues. A
  failure that is **not** one delivery is fatal for the worker: the host is stopped
  (`BackgroundServiceExceptionBehavior.StopHost`, set explicitly) instead of leaving a worker
  that is gone behind a host that still answers — the endpoint would then accept requests
  that can never be delivered. The application does not restart the worker itself; the
  deployment does;
- a **cancelled** delivery is not a confirmed non-delivery: the message may or may not have
  reached the mailer, the cancellation is passed on, and **no** failed delivery is recorded
  for it. Only a delivery the mailer actually refused is audited as
  `VerificationEmailDeliveryFailed`;
- the **lifecycle is controlled**: the queue stops accepting requests when the host stops, the
  accepted work gets a **bounded drain period** (15 s) to finish, and what is left is counted
  as interrupted. A single delivery may take at most 60 s, so a stuck mail server is reported
  as a failed delivery instead of stalling the worker;
- the worker counts **accepted, refused, processed, failed and interrupted** work, and the
  summary it writes on shutdown carries counts only — never an address and never a token. A
  failed item is reported by the **type** of its exception only, because a database error can
  carry the address of the account. "Processed" does not mean that a mail was sent: a request
  for an address that belongs to no account is processed as well and produces no mail — whether
  a mail was handed over is recorded in the audit trail, not in the counters;
- **the delivery is not durable and not crash-safe.** There is no outbox, no broker and no
  persistent queue, and no exactly-once delivery: a mail can be handed over and the work can
  still be lost, or a new mail can retire an earlier one. A **graceful** stop lets the
  accepted work finish for a bounded period; what is left is counted as interrupted and
  logged, but it is **not audited** — the audit trail only holds what the delivery service
  itself recorded. A **sudden crash** loses the queued work *and* the counters, because they
  live in the process: such a loss can neither be counted reliably nor be audited. A
  recipient that receives no mail asks for another one, and the per-account cooldown and daily
  cap keep that bounded. A delivery that survives a restart would need a durable outbox, and
  the plaintext token would have to be protected there — that is a separate decision, not
  part of this increment.

### The sign-in gate

An account whose address has not been confirmed may not sign in. The gate is deliberately
narrow, and it is applied **after** the password was confirmed:

- only an account with `EMAIL_VERIFIED = false` is refused — a **legacy account**
  (`EMAIL_VERIFIED IS NULL`) counts as verified and keeps its access, and an account whose
  address was confirmed is not affected;
- a wrong address, a wrong password and an unusable stored hash still answer the same generic
  **401**, so the verification state of an account is never revealed before the password was
  confirmed;
- a refusal answers **403** with the stable code `EMAIL_NOT_VERIFIED` and a static message.
  The message carries no credential, no token and no detail about the account;
- the refusal is recorded in the audit trail as `LoginBlockedUnverified`, with the account and
  the remote address — never with the password, the hash or a token;
- **the investor approval stays independent**: a confirmed account that still waits for the
  approval keeps exactly the restrictions it had before, and a verification never implies an
  approval.

**An existing session cannot bypass the gate.** Every request re-reads the account — the token
revalidation that already refuses a deleted account or a changed role — and the session is
rejected when the account is explicitly unverified. The rejection is deliberately not audited:
it would repeat on every request of a session, while the sign-in attempt itself is recorded.
The sign-in is the only place where a session is issued, so there is no other path that could
hand one out.

```
POST /api/auth/login
401  ->  the address or the password is wrong - the same generic answer for both, and for an
          account whose stored hash is unusable
403  ->  the address of the account has not been confirmed yet (errorCode EMAIL_NOT_VERIFIED)
200  ->  a session, as before
```

## 9. The database-level guarantees (applied)

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

## 10. Audit trail

`USER_SECURITY_EVENTS` is append-only: the service offers no way to change or remove a recorded
event, the event type must come from the closed set in `Enums\UserSecurityEventTypes`, and the
free-text context is stripped of control characters and truncated.

## 11. Open items

1. **RELEASE-BLOCKING — every new Landlord and Investor account must be
   `EMAIL_VERIFIED = false` and must prove its address.** Today `AuthService.RegisterAsync`
   and `UserService.CreateAsync` give them the legacy value (`NULL`, treated as verified), so
   a new account can be used without proving its address. The delivery layer, the endpoints and
   the sign-in gate of the registration verification now exist (§8) and the issuance guard
   refuses any account that is not explicitly unverified, so the server enforces the order.
   The remaining work, in this order:
   (a) set `EMAIL_VERIFIED = false` for new self-registered and admin-created Landlord and
   Investor accounts, and deliver the verification mail,
   (b) then re-point the account list so an administrator can see that an account is still
   awaiting verification,
   (c) then the frontend confirmation page and the "check your email" step of the registration
   (the verification link points at `/verify-email`, which does not exist yet).
   The order matters: flipping the flag before the confirmation page exists would lock those
   accounts out with no way to verify them. The existing 230 accounts keep `NULL` (= verified)
   and are not touched.
   The order matters: flipping the flag before (a) and (b) exist would lock those accounts
   out with no way to verify them and no way to sign in. The existing 230 accounts keep
   `NULL` (= verified) and are not touched.
2. **RELEASE-BLOCKING (until an end-to-end verification) — the frontend activation route is
   implemented but untested.** The page `/activate-admin`
   (`frontend/src/pages/auth/ActivateAdminPage.tsx`) reads the token from the URL fragment, removes
   the fragment from the address bar and from the history **before the first paint** (keeping the
   router's own history state), validates the password and its confirmation against the server's
   policy, and redeems the token through `POST /api/auth/activate-admin`. The copy is bilingual
   (EN/AR) and distinguishes the answers 204 / 400 / 409 / 429; the token is never written to a
   URL, to storage or to a log. **No dynamic or end-to-end test has run against it** (no
   data-creating tests are permitted at this stage) — the type check, the lint and the production
   build are the only evidence — so the item stays release-blocking until an end-to-end
   verification of the whole invitation flow has passed. The route path must also match the
   configured `Email:ActivationPath` (the default is `/activate-admin`).
3. **RELEASE-BLOCKING — the committed Swagger export is five operations behind**:
   `POST /api/auth/activate-admin`, `POST /api/Users/{id}/admin-invitation`,
   `POST /api/auth/verify-email`, `POST /api/auth/resend-verification` and
   `POST /api/Users/{id}/verification` are declared by the controllers but not in
   `docs/api/swagger.json`. The export can only be produced by a
   running instance, so the refresh is a separately approved step: run
   `docs/tests/verify-swagger-export.ps1` (it starts its own instance with both seeders forced to
   a no-op and performs no database writes) — or, with a Development instance already running,
   fetch `GET /swagger/v1/swagger.json` and write it to `docs/api/swagger.json` — then re-run
   `docs/tests/scripts/static_contract_checks.ps1`. The live API surface is already correct; only
   the published contract lags.
4. An unverified registration still **reserves** its address (squattoing) until an
   ownership-based release exists; the recovery policy is in §8. An admin-created
   Landlord/Investor still counts as verified (item 1).
5. The seeded `"N/A"` account still exists — it can never sign in (now a clean 401); removing
   it is an operator decision.
6. No dynamic test has been executed for any of this (no data-creating tests are permitted
   yet), so the flows rest on compilation, guard analysis and code review. The activation
   page (item 2) has the same status.

## 12. What exists today

- **Schema (applied):** `USERS.EMAIL_VERIFIED` / `EMAIL_VERIFIED_AT` / `PENDING_EMAIL`,
  `EMAIL_VERIFICATION_TOKENS`, `USER_SECURITY_EVENTS` (migration
  `20261019165813_EmailVerification`), plus the two uniqueness indexes (migration
  `20261019174547_EmailVerificationUniqueness`).
- **Infrastructure:** the mailer abstraction with the SMTP, development-outbox and fail-closed
  senders, the configuration and its startup validation, and the append-only audit service.
- **Token service:** issuance (registration, address change with current-password
  confirmation, admin activation), atomic consumption, admin activation redemption, invitation
  reissue with the resend limits, and the account-bound, purpose-checked, expiring, single-use
  rules.
- **Admin onboarding:** SuperAdmin-only creation, an unusable placeholder password, a bilingual
  invitation with a fragment link, in-process delivery that never exposes the token, a
  SuperAdmin-only (re)send under the resend limits, a single-use activation that the new Admin
  redeems with their own password, and the `POST /api/auth/activate-admin` endpoint with its own
  rate-limit budget.
- **Account safety:** a direct address change of a verified account is refused with a 409, a
  correction of an unverified account keeps it unverified and retires every stale link and the
  pending change, and an unusable password hash answers a generic 401 instead of a server error.
- **Registration verification (delivery, endpoints and gate):** the bilingual EN/AR verification
  mail with a fragment link, the issuance guard that reserves the purpose for an explicitly
  unverified Landlord or Investor account, the address binding of the token, the resend limits
  and the in-process handover of the token, the anonymous confirmation endpoint with its own
  rate-limit budget, the anonymous resend that answers the same in every case and whose work
  runs in a managed worker with a bounded queue, the SuperAdmin-only resend, and the sign-in
  gate that refuses an unverified account without touching the legacy accounts. The flag flip
  is **not** part of it yet (§11.1).
- **Activation UI (implemented, untested):** the public page `/activate-admin` reads the token
  from the URL fragment, drops the fragment from the address bar and the history before the first
  paint, mirrors the server's password policy, redeems the token and shows the 204 / 400 / 409 /
  429 answers in English and Arabic. It creates no session: the new administrator signs in with
  the password they just chose.
