# Authentication configuration

Provide `ConnectionStrings:DefaultConnection` and `Jwt:Key` through .NET user-secrets or environment variables (`ConnectionStrings__DefaultConnection` and `Jwt__Key`). The JWT signing key must contain at least 32 bytes; use a randomly generated secret. Credentials are intentionally absent from tracked application settings.

Development seeding accepts `BUILDWISE_DEMO_PASSWORD` (at least eight characters). If omitted, it uses a random process-local password that is not logged or returned. Demo account buttons only fill the email; enter the configured password yourself. Seeding never changes an existing account's password, active status, or roles.

Previously tracked development credentials remain in Git history. Replace any deployed signing key, database password, or existing demo-account password that used those values. Removing defaults does not reset existing database accounts or erase Git history.

Every validated bearer token is checked against the current account and roles in the database. Deleted or inactive accounts and tokens with outdated roles are rejected with 401. Issuer, audience, signing key, and lifetime validation remain enabled.
