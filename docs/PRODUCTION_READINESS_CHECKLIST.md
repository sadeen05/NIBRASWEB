# NIBRAS production readiness checklist

Status: **READ-ONLY PLAN — NOT EXECUTED**. This checklist records the gates that must be met
before a production deployment can be approved. It does not describe work that has already been
performed, and it is not authorization to deploy, migrate, seed or create an account.

Date: 22 October 2026
Branch: `step-7-hardening`
Release verdict until every gate is signed off: **F1 HOLD; production release not declared**.

## 1. Non-negotiable safety gates

- [ ] Written deployment approval names the environment, host, database, storage volume and
      maintenance window.
- [ ] A restorable backup of the database **and** the document storage volume exists; a restore
      test has passed.
- [ ] The target is explicitly configured; `DatabaseTargetGuard` passes. `NEBRAS` and
      `NEBRASDB_Step1` are always refused. `NEBRASDB` is refused unless the exact name is
      separately and deliberately listed in `Database:ApprovedProductionLikeTargets`.
- [ ] `ASPNETCORE_ENVIRONMENT` is a non-Development value. Swagger, the Development outbox, the
      Development legacy-document shim and the seeders are therefore disabled.
- [ ] No `Database.Migrate()` or automatic migration runs in the web host. Migrations are applied
      only by the approved deployment step.
- [ ] No `Seed:SuperAdminEmail` or `Seed:SuperAdminPassword` value is present. The host now refuses
      to start outside Development when either key is populated.
- [ ] No privileged account is created by an API route, a startup seeder or this checklist.
- [ ] The historical document rows and bytes remain untouched pending a separate decision.

## 2. Required production configuration

All values below come from the deployment secret/configuration store, never from a committed
file. The application's startup checks fail closed where noted.

| Key | Production requirement | Current fail-safe behavior |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | Explicit target; least-privilege application login; no shared database | Missing or malformed value stops startup; `NEBRAS`/`NEBRASDB_Step1` are always denied; `NEBRASDB` is denied unless exactly approved |
| `Jwt__Key` | Base64, decodes to at least 32 bytes, stored as a secret | Missing, invalid or short key stops startup |
| `Jwt__Issuer`, `Jwt__Audience` | Explicit deployment values | Token validation uses them |
| `Documents__StorageRoot` | **Required outside Development**, fully qualified absolute path, outside `wwwroot`, private and writable | Empty, relative or `wwwroot`-contained paths stop startup (startup validator, not first upload) |
| `Documents__MaxFileSizeBytes` | Greater than 0 and at most `10485760` | Invalid value stops startup |
| `Email__DeliveryMode` | `Smtp` outside Development | Any other mode stops startup |
| `Email__SmtpHost`, `Email__SmtpPort` | Real SMTP endpoint and port | Missing/invalid values stop startup |
| `Email__SmtpEnableSsl` | `true` outside Development | Non-TLS SMTP stops startup |
| `Email__FromAddress` | Valid sender address | Missing/invalid value stops startup |
| `Email__SmtpUser` / `Email__SmtpPassword` | Optional pair; password only when a user is configured; secret-store value | Startup validation refuses an incomplete pair |
| `Email__FrontendBaseUrl` | Absolute `https` address outside Development | Plain HTTP stops startup |
| `Email__ActivationPath`, `Email__VerificationPath`, `Email__EmailChangePath` | Local paths only; no scheme, query, fragment or whitespace | Malformed paths stop startup |
| `ReverseProxy__Enabled` | `false` unless TLS is terminated by a trusted proxy | Disabled means forwarded headers are ignored |
| `ReverseProxy__KnownProxies` | Exact proxy IPs, for example `10.0.0.10` | Enabling without a proxy or network stops startup |
| `ReverseProxy__KnownNetworks` | Explicit CIDR networks, for example `10.0.0.0/24` | Wildcard, any-address and prefix `0` networks are refused |
| `ReverseProxy__ForwardLimit` | Number of trusted proxy hops (1–10) | Invalid value stops startup |
| `AllowedHosts` | Exact public host names, semicolon-separated; no wildcard outside Development | Empty or wildcard value stops startup in non-Development |
| `RateLimit__*` | Values approved for production traffic | Defaults are finite; review them rather than raising them blindly |
| `Database__ApprovedProductionLikeTargets` | Empty unless the exact production-like name is intentionally approved | Missing/blank approves nothing |

The repository's `appsettings.json` is a local-development default. It contains no usable
production database, SMTP password, storage volume or host name. `AllowedHosts` is intentionally
limited to loopback names there; production must supply the real host names.

## 3. HTTPS, reverse proxy, CORS and hosts

### 3.1 Choose exactly one TLS termination model

- **Kestrel terminates TLS:** configure a certificate through the deployment secret store. Keep
  `ReverseProxy:Enabled=false`; do not trust forwarding headers from arbitrary clients.
- **A trusted proxy terminates TLS:** set `ReverseProxy:Enabled=true` and list only the proxy
  addresses/networks that connect to the application. The proxy must overwrite, not append to,
  client-supplied `X-Forwarded-For` and `X-Forwarded-Proto` values. Set `ForwardLimit` to the real
  number of trusted hops.
- The application accepts only `X-Forwarded-For` and `X-Forwarded-Proto`; it does not trust
  `X-Forwarded-Host`. The proxy must preserve the original `Host`, or the deployment must account
  for the internal host in its redirect configuration.
- Never use `*`, `0.0.0.0/0`, `::/0`, `0.0.0.0`, `::` or an empty trust list. These values are
  rejected at startup.
- `UseForwardedHeaders` runs before HSTS, HTTPS redirection and the rate limiter, so the trusted
  scheme and client address are used consistently. If the feature is disabled, no forwarded
  header is trusted.

Example shape only (replace every placeholder; never commit real addresses):

```json
"ReverseProxy": {
  "Enabled": true,
  "KnownProxies": [ "10.0.0.10" ],
  "KnownNetworks": [ "10.0.0.0/24" ],
  "ForwardLimit": 1
}
```

### 3.2 HTTPS behavior

- [ ] `UseHsts` is active outside Development and `UseHttpsRedirection` is active.
- [ ] A request over HTTP receives the intended HTTPS redirect, with no proxy redirect loop.
- [ ] HSTS is sent only over HTTPS and the certificate chain is valid.
- [ ] The proxy forwards the real scheme; verify with a request that reaches the API over HTTPS.
- [ ] WebSocket, gRPC and long-running requests are not required by this API; no extra proxy
      configuration is assumed.

### 3.3 Hosts and CORS

- [ ] `AllowedHosts` lists every public API host name (and any approved health-check host), with no
      wildcard outside Development. Replace the loopback defaults in production.
- [ ] Requests with an unknown `Host` are rejected by host filtering.
- [ ] The frontend and API are served from the **same origin** (the default and recommended
      topology). Vite development proxying or a production reverse proxy preserves this.
- [ ] No CORS policy is enabled by default. If cross-origin hosting is later approved, use an
      explicit exact-origin allow-list; never `AllowAnyOrigin`, and never a wildcard with
      credentials. CORS is not a substitute for `AllowedHosts`.

## 4. Database and migration preflight

- [ ] Confirm the effective connection string through the application's own sanitized log line
      (server/database only, never credentials).
- [ ] Run a read-only migration-history check and confirm the expected **21** migrations. Review
      every pending migration script; do not use `--force`, reset, drop or a rewritten history.
- [ ] Confirm the deployment database login has only the permissions the application needs. Do not
      grant `dbcreator`, `sysadmin` or broad DDL rights to the runtime login merely to apply
      migrations; use a separate approved migration identity.
- [ ] Back up the database before applying an additive migration. Apply the reviewed migrations
      explicitly, then verify history and schema.
- [ ] Do not run `docs/tests/scripts/s4_preflight.ps1` and do not run the disposable-suite scripts
      against production.
- [ ] Run the application with no automatic seeding. `DemoDataSeeder` and `SuperAdminSeeder` are
      Development-only and must not be invoked by the production process.
- [ ] After migration, verify that no unexpected rows, accounts or document bytes were created.

## 5. Document storage

- [ ] `Documents__StorageRoot` is an absolute, fully qualified path on a private volume, outside
      the application's `wwwroot` and outside the repository.
- [ ] The directory is writable by the application identity only; OS ACLs prevent interactive
      users and the web server from reading it as static content.
- [ ] The volume is backed up together with the database, and a restore of both has been tested.
- [ ] Confirm the startup fail-fast behavior: with an empty or relative root outside Development, the
      host exits before serving; with a valid root, upload/download succeeds.
- [ ] Decide and record the malware-scan/content-disarm policy before documents are downloaded by
      browsers. The current signature check proves only the file container type.
- [ ] Set volume capacity monitoring and an orphan-file reconciliation decision.
- [ ] Do not copy, recreate, delete or relink historical document bytes as part of deployment
      without separate written approval.

## 6. Email delivery

- [ ] Use a controlled production mailbox and a real SMTP endpoint; verify STARTTLS, sender
      identity, delivery, timeout and failure behavior.
- [ ] Confirm no `Disabled` or `FileOutbox` mode reaches production.
- [ ] Confirm `Email__FrontendBaseUrl` is HTTPS and the verification/activation paths resolve on the
      real frontend.
- [ ] Confirm logs contain no tokens, links, passwords or full recipient addresses.
- [ ] Keep SMTP credentials in the secret store and rotate them through the approved procedure.

External SMTP acceptance is still an open release gate in this repository; do not treat the local
fake-SMTP run as production delivery evidence.

## 7. SuperAdmin bootstrap (implemented as the out-of-band maintenance command)

The current code creates a SuperAdmin only through the **Development-only** seeder. Production
must not use `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword`; the host refuses those keys
outside Development. It also refuses to serve while carrying `Bootstrap:SuperAdminEmail` /
`Bootstrap:SuperAdminPassword` outside the bootstrap invocation itself.

The one-time mechanism is the **out-of-band maintenance command** `--bootstrap-superadmin`,
not a startup path and not an API route. It is implemented in
`Service/SuperAdminBootstrap.cs`; the operator procedure and exit codes are documented in
`PRODUCTION_BOOTSTRAP_AND_HEALTH.md`. The command:

1. refuses to run in Development (Development keeps its seed);
2. requires configured credentials plus a typed `--confirm <email>` that repeats the exact
   target address;
3. refuses if any login-capable SuperAdmin already exists (the model placeholder never counts),
   so the mechanism is disabled after its own first success;
4. refuses if the target email or the reserved phone is already used;
5. runs after `DatabaseTargetGuard` / `ProductionConfigurationGuard` exactly as any other start;
6. pins the SuperAdmin ROLE row with `UPDLOCK, HOLDLOCK`, re-checks the precondition inside the
   transaction and commits the account, its BCrypt-12 hash and the append-only
   `SuperAdminBootstrapped` audit event atomically; the process exits with a documented code
   instead of listening.

The operational runbook (approval, secret handling, verification, rotation) below still applies
when the command is ever executed against a real deployment:

1. Obtain separate written approval naming the database, environment and operator.
2. Generate a high-entropy temporary password in the secret manager. Never put it in a repository
   file, command history, log or chat.
3. Run the command through the documented invocation; confirm the exit code is 0.
4. Verify the account read-only: exactly one approved SuperAdmin, expected role and email, no
   second privileged account.
5. Remove the bootstrap configuration immediately after the run; a normal start refuses to boot
   while it is still present.
6. Record the operator, approval reference, timestamp and verification evidence.

No account was created and no bootstrap was executed during the implementation work; the tests
cover the decision logic with in-memory fakes and the refusals against the disposable database.

## 8. Secrets and observability

- [ ] JWT key, SMTP password, database credentials and any storage credentials are supplied by the
      deployment secret store; none is in `appsettings.json`, source, scripts, logs or build output.
- [ ] The runtime account is not an interactive administrator and does not have broad DDL rights.
- [ ] Logs are aggregated with access control; tokens, verification links, passwords, hashes and
      full email addresses are excluded.
- [ ] Alerts cover startup failure, SMTP failure, storage write/read failure, 5xx rate, database
      connectivity, disk capacity and authentication abuse.
- [ ] Log retention and incident-response ownership are recorded.

## 9. Staging and post-deployment verification

- [ ] Deploy the exact reviewed build to staging with production-like configuration.
- [ ] Verify startup with a missing storage root, missing proxy trust, wildcard `AllowedHosts` or
      populated Development-only SuperAdmin seed credentials: each must refuse to start.
- [ ] Verify a valid startup, then exercise: health/readiness, login, email verification, address
      change, document upload/download, authorization denials, rate limiting, HTTPS redirect/HSTS,
      host filtering and forwarded-header handling.
- [ ] Confirm Swagger is unavailable outside Development.
- [ ] Confirm migration history is unchanged after the application starts.
- [ ] Confirm the document volume persists across a restart and the database and volume remain
      consistent.
- [ ] Re-run the approved focused acceptance and security checks against staging; do not rerun
      unrelated accepted suites merely to inflate evidence.
- [ ] Record the rollback trigger, rollback owner and a tested restore procedure.

## 10. Historical document decision

Rows 100 and 102–104 are preserved in the database without physical bytes at their recorded paths;
row 101 is legacy metadata-only. The only authentic surviving copy identified for rows 100/103 is
the byte-identical row-116 file, and rows 102/104 have no authentic copy found.

- [ ] Decide whether these are disposable fixtures or data that must be retained.
- [ ] If retained, approve a separate recovery operation with exact source/target paths and SHA-256
      verification. Do not copy bytes as part of the deployment checklist.
- [ ] If not retained, record the decision and exclude them from any data migration; do not present
      regenerated test bytes as recovery.

## 11. Sign-off

| Gate | Owner | Evidence | Date |
| --- | --- | --- | --- |
| Safety approval and backups | | | |
| Database target and migrations | | | |
| Storage volume and restore | | | |
| SMTP and frontend links | | | |
| HTTPS/proxy/hosts/CORS | | | |
| SuperAdmin one-time bootstrap | | | |
| Staging smoke and rollback | | | |
| Historical document decision | | | |

Until every row is signed off, the release remains **HOLD**. This checklist itself performs no
deployment, migration, account creation or data change.
