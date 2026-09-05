# Secrets and keys

## Where secrets come from

Secrets are supplied as **files**, not environment variables. A process listing, a crash dump of the
environment block, or a child process inheriting the environment all disclose variables; a file read
once at start-up does not. Docker and Kubernetes both mount secrets as files natively.

```
/run/secrets/
  Database__ConnectionString
  Notifications__Provider__ApiKey
```

The key-per-file provider maps the file name to a configuration key, so `Database__ConnectionString`
becomes `Database:ConnectionString`. `Secrets:Directory` says where to look; when the directory does
not exist the provider is skipped, which is what lets a developer machine run without it.

Vault or a cloud key manager can be added behind the same configuration interface without changing any
consumer.

## What must never be logged

`LogRedaction` holds the list of property names whose values are replaced before a log event reaches a
sink: passwords and hashes, API keys and tokens, authorization and cookie headers, multi-factor secrets
and recovery codes, connection strings, private keys and certificates, backup and Data Protection keys,
webhook signing secrets, and identity numbers. Matching is on a normalised form, so `api_key`, `ApiKey`
and `APIKEY` all match.

Personal data is masked rather than redacted where an operator needs to correlate: a phone number keeps
its last four digits, an email keeps its first character and its domain.

A test asserts that sentinel values written into each secret file never appear in logs, exception
output, problem details, health responses or exporter output — including when start-up validation fails,
which is the moment a configuration value is most likely to be echoed.

## Data Protection key ring

ASP.NET Core Data Protection signs anti-forgery tokens and session data. The keys live in the database
(`platform.data_protection_keys`) rather than on a container's disk, because a key ring on disk is lost
on every deployment and a token issued before a rolling restart would then be rejected after it. The
ring itself is encrypted with a certificate or a key-management service, so a database backup does not
carry usable keys.

Staging keeps its own ring. Sharing one with production would mean a token minted in staging is
accepted in production.

## Rotation

| Secret | Rotation | Notes |
| --- | --- | --- |
| Database passwords | Yearly, and on any suspicion | The application reads the file at start-up; rotation is a restart |
| Provider API keys | Yearly, and on staff change | Adapters read them through configuration, so no code change |
| Data Protection ring | Automatic, 90-day keys with overlap | Old keys are retained long enough to validate outstanding tokens |
| Backup encryption keys | Yearly | Kept outside the backup destination; a key stored with the backup protects nothing |

Rotation dates and owners are tracked with the operational decisions in `docs/nfr/security-operations-targets.md`.
