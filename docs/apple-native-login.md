# Native Apple login

## Configuration

AccountService accepts `OAuthAppleOptions__ClientId` (default `com.coffeepeek`).
This must match the signed iOS app bundle ID. Apple signing keys are retrieved
over HTTPS from `https://appleid.apple.com/.well-known/openid-configuration` and
cached by IdentityModel. The existing Redis connection stores login challenges
with a five-minute TTL. Gateway's `auth-endpoints` rate limit covers both routes.
There is no database migration: Apple identities use the existing external
provider credentials with provider `AppleProvider` and Apple's stable `sub`.

## Contract

1. `POST /api/Tokens/apple/challenge` (no body, anonymous):
   `{ "isSuccess": true, "data": { "challengeId": "uuid", "nonce": "random", "expiresAt": "ISO-8601" } }`
2. The native ASAuthorizationAppleIDRequest sets `nonce` to the returned nonce
   and `state` to `challengeId`, requests email scope, and verifies returned state.
3. `POST /api/Tokens/apple/login` with `{ "challengeId": "uuid", "idToken": "Apple JWT" }`:
   `{ "isSuccess": true, "data": { "accessToken": "CoffeePeek JWT", "refreshToken": "opaque", "accessTokenExpiresAt": "ISO-8601" } }`.

The native session store must securely persist both CoffeePeek tokens. Apple ID
tokens must never be used as CoffeePeek access tokens or logged. The server
validates RS256 signature, issuer, audience, expiry, subject and nonce, then
atomically consumes the challenge. An invalid token does not consume it. Replays
and expired challenges return 401; restart the native sign-in flow with a new
challenge. Device/IP metadata is derived from the HTTP request.

New accounts require a verified email (including Apple Private Email Relay).
Returning accounts resolve by Apple subject even when email is absent. Existing
accounts with matching email are not silently linked to a new provider. Deleted
or blocked accounts cannot get a session. Configure the CoffeePeek mail sender
with Apple's Private Email Relay for transactional emails to relay addresses.

The native flow validates ID tokens directly; it does not exchange authorization
codes for Apple refresh tokens. CoffeePeek refresh-token logout/revocation remains
the existing session lifecycle. Server-side Apple grant revocation on account
deletion requires a separate Apple signing key/token-exchange integration.

## Verification

`AppleAuthServiceTests` validates locally signed JWTs, relay email claims, invalid
issuer/audience/nonce/expiry, unsigned tokens, foreign keys, malformed tokens and
missing subject. `AppleLoginHandlerTests` covers session creation, missing email
on repeat login, unverified email, expiry/replay, bad-token non-consumption and
deleted-account rejection. These tests require no live Apple credentials.
