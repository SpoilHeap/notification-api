# 0012. Store the API token hashed and encrypted

- Status: Accepted
- Date: 2026-11-27

## Context

The HA integration calls `/api/v1` with a bearer token that the add-on generates on first start and hands over through Supervisor discovery (spec §8.3, §9.4, [ADR 0003](0003-ingress-authentication-no-roles.md)). engineering_standards.md §6 requires the token to be stored **hashed** (SHA-256) and compared in constant time.

A hash alone isn't enough here:

- Discovery is **re-published on every add-on start**, because the add-on can't tell whether the Supervisor still holds an earlier message (and HA Core re-processes discovery when it restarts, spec §13). Every publication must carry the **same** token, or the token the integration has stored stops working. Identical messages also let the Supervisor de-duplicate them.
- The manual fallback (spec §9.4) shows the token in the add-on UI when discovery fails.

Both need the plaintext again after a restart, which a one-way hash can't give back.

## Decision

The `ApiClient` row stores the token twice:

- **`TokenHash`:** SHA-256 of the token's text. Verification uses only this, with `CryptographicOperations.FixedTimeEquals` (`ApiTokenHash.Matches`). Every stored client is compared, with no early exit.
- **`ProtectedToken`:** the plaintext encrypted with **ASP.NET Data Protection** (purpose `NotificationApi.Secrets.v1`), behind the Application port `ISecretProtector`. Keys are in `/data/keys`, next to the database. Only discovery and the Integration page's **Reveal** action decrypt it.

The token is 32 random bytes from `RandomNumberGenerator`, base64url-encoded without padding (43 characters). The `ApiToken` type redacts itself in `ToString()`, so it can't end up in a log line by accident. Persistence code never references the plaintext type, and an architecture test checks that.

When the protected copy can't be decrypted (for example, `/data/keys` was lost while the database survived), the add-on doesn't silently replace the token. The integration's existing token still verifies against the hash. Discovery and the Integration page report the problem, and recovery needs token rotation (not built yet).

## Alternatives considered

- **Hash only, and generate a new token on every start.** It breaks the integration on every restart, and the Supervisor can't de-duplicate.
- **Plaintext in the database or in a file in `/data`.** Simplest, but a copy of the database or an HA backup would expose a working credential with no extra step. It also breaks §6.
- **Encrypted only, with no hash.** Every request would need a decryption, and verification wouldn't be a constant-time hash comparison as §6 asks.
- **Derive the token from a key (HMAC of a fixed id).** Reproducible without storing it, but it's still a secret to protect, and rotation and revocation get awkward.

## Consequences

- The database holds no usable credential on its own. A full copy of `/data` (database **and** keys), or an unencrypted HA backup, does. That's why DOCS.md recommends encrypted backups. The Data Protection keys themselves aren't encrypted at rest (no XML encryptor on Linux without a certificate). The `/data` volume and HA's backup encryption protect them.
- Restoring a backup restores both the database and the keys, so the token, and the integration, keep working (AC96).
- Changing the Data Protection application name or the protector purpose makes every protected secret unreadable. Both are constants, and the purpose is versioned.
- Token rotation is a follow-up. It will replace both columns and re-publish discovery.
