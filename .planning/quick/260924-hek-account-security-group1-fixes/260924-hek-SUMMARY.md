---
quick_id: 260924-hek
status: complete
branch: dev
commits: [618ba76a, 9274c159, c92edde6, 1565e49b]
---

# Summary

- 618ba76a — iat claim plus revoked-at timestamp marker; the gateway rejects only tokens issued before the revocation.
- 9274c159 — Refresh-token reuse returns 401 instead of throwing, so the revocation commits. Native refresh no longer NREs on errors.
- c92edde6 — Access tokens are revoked on block, admin delete, role change, revoke-all-sessions, password change and password reset. Admins cannot act on their own account.
- 1565e49b — The cache pattern prefix must be literal and cannot be `revoked-user`.

Wolverine handlers regenerated with `dotnet run -- codegen write`. Full solution tests green.

## Known ceilings / follow-ups
- iat has second precision: a token issued in the same second as a revocation survives.
- Two admins demoting each other concurrently could still leave zero admins (no DB-level guard).
- Single-session admin revoke doesn't revoke that device's access token (tokens carry no session id).
- Deploy order: Gateway and Account should ship together. An old gateway reading the new timestamp marker still
  treats it as "revoked" (key exists), which only means extra re-logins until the gateway is updated.
- Group 2 from the review (concurrency token on User, email/username case normalisation) is still open.
