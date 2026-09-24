---
quick_id: 260924-hek
slug: account-security-group1-fixes
date: 2026-09-24
branch: dev
---

# Account security fixes — group 1 (from AccountService review 2026-09-24)

1. **Token-reuse revocation is persisted.** `User.RotateRefreshToken` returns `false` instead of throwing, so Wolverine's
   transaction commits `RevokeAllSessions`. The handler also revokes access tokens and returns a 401 `Response`.
   Both refresh endpoints in TokensController map the non-success response to 401 and delete the cookie.
2. **Access-token revocation by timestamp.** Access tokens get an `iat` claim. The Redis marker stores revoked-at unix
   seconds, and the gateway rejects tokens issued before that time, so a user who logs in again is not locked out.
   Legacy `"1"` markers and tokens without `iat` are treated as revoked.
3. **Revoke on:** admin block, admin delete, role change, revoke-all-sessions, password change, password reset.
   Shared helper `RevokeAccessTokensAsync` (throws if Redis rejects the write).
4. **Admin self-guard.** Block, delete and role change refuse `actor == target`. This also covers "last admin": an admin
   acting on another admin means ≥2 admins exist.
5. **Cache validator.** The segment before the first `:` must be literal (no glob chars) and not `revoked-user`.
   Keys under `revoked-user:` are refused.

Each item has a regression test.
