# Sign in with Apple

The Account service supports native identity-token login and browser redirects with server-side
code exchange. Both use the existing server-generated, single-use Apple login challenge.
Identity validation checks Apple's RS256 signature, issuer, configured audience, expiry, subject
and the exact challenge nonce. Existing Apple users can sign in when Apple omits email on a
later authorization; creating an account requires a verified email.

## Configuration

Enable Sign in with Apple for the App ID in Apple Developer. For browser login, also configure a
Services ID, website domain and return URL. Account reads the `OAuthAppleOptions` section:

```json
{
  "OAuthAppleOptions": {
    "ClientId": "com.coffeepeek",
    "ServiceId": "YOUR_SERVICES_ID",
    "TeamId": "YOUR_TEAM_ID",
    "KeyId": "YOUR_KEY_ID",
    "RedirectUri": "https://api.coffeepeek.by/api/tokens/apple/callback"
  },
  "WebClientUrl": "https://coffeepeek.by"
}
```

`ClientId` retains its existing default, `com.coffeepeek`. Set it to the application's Bundle ID
if different. Native login needs only `ClientId`; the web settings are optional and are checked
when browser login is used. `ServiceId` adds the configured Services ID as an accepted identity-token
audience alongside `ClientId`.

Supply the `.p8` private key through `OAuthAppleOptions__PrivateKeyPem`. Both multiline PEM and
literal `\n` line breaks are accepted. The server creates a five-minute ES256 client secret for
each code exchange. Keep the private key in the deployment's secret environment.

Docker deployment maps the following variables to Account configuration:

| Environment variable | Account configuration | Purpose |
| --- | --- | --- |
| `APPLE_CLIENT_ID` | `OAuthAppleOptions:ClientId` | Native Bundle ID; defaults to `com.coffeepeek` |
| `APPLE_WEB_CLIENT_ID` | `OAuthAppleOptions:ServiceId` | Optional browser Services ID |
| `APPLE_TEAM_ID` | `OAuthAppleOptions:TeamId` | Apple developer team |
| `APPLE_KEY_ID` | `OAuthAppleOptions:KeyId` | Sign in with Apple key ID |
| `APPLE_PRIVATE_KEY_PEM` | `OAuthAppleOptions:PrivateKeyPem` | Private `.p8` key contents |
| `APPLE_REDIRECT_URI` | `OAuthAppleOptions:RedirectUri` | Registered HTTPS callback URL |
| `WEB_CLIENT_URL` | `WebClientUrl` | Fixed destination after successful browser login |

The callback must be a public HTTPS DNS URL with the path `/api/tokens/apple/callback`, without
query, fragment or credentials. Register exactly the same URL in Apple Developer. Redis must be
available for challenges and browser state. Leave the optional Apple web variables (`APPLE_WEB_CLIENT_ID`, `APPLE_TEAM_ID`, `APPLE_KEY_ID`,
`APPLE_PRIVATE_KEY_PEM`, `APPLE_REDIRECT_URI`) empty if browser login is unused.

## Browser flow

1. Navigate the browser to `GET https://api.coffeepeek.by/api/tokens/apple/authorize`.
   The server creates the existing login challenge, stores its ID under a random browser state
   in Redis for at most five minutes, and sends the challenge nonce unchanged to Apple. A Secure,
   HttpOnly, SameSite=None correlation cookie binds the attempt to that browser.
2. Apple submits `code` and `state` to `POST /api/tokens/apple/callback` using
   `application/x-www-form-urlencoded`. The server compares the correlation cookie and atomically
   consumes Redis state with GETDEL before exchanging the authorization code. Cancelled and failed
   exchanges also consume state; a replay cannot create another session.
3. The existing login handler validates the returned identity token against the original challenge
   and atomically consumes the challenge before issuing a CoffeePeek session. On success the callback
   sets the existing Secure, HttpOnly, SameSite=Strict refresh cookie at `/api/tokens` and redirects
   to `WebClientUrl`. Tokens are absent from the redirect URL and body.
4. After navigation finishes, the website calls `PUT https://api.coffeepeek.by/api/tokens` with
   `credentials: "include"` to obtain an access token, then requests `GET /api/users/me`.
   The gateway's allowed CORS origins must include the configured website origin.

The Strict refresh cookie requires the API and website to share the same HTTPS site, such as
`api.coffeepeek.by` and `coffeepeek.by`. The return destination comes from configuration;
caller-supplied return URLs are ignored. Errors return an HTTP error response and require a fresh
login attempt. Apple browser login needs no Apple JavaScript SDK.

## Native identity-token flow

The existing `/api/tokens/apple/login` contract is preserved. `/api/tokens/apple/native` is an
alias with the same request and response. Use a fresh challenge for every attempt:

1. `POST /api/tokens/apple/challenge` returns `data.challengeId`, `data.nonce` and `data.expiresAt`.
2. Send **the returned nonce unchanged** in the Apple authorization request and request the email
   scope. The signed identity token's `nonce` claim must equal this server-provided value.
3. Submit the identity token and challenge ID over HTTPS:

```http
POST /api/tokens/apple/login
Content-Type: application/json

{"challengeId":"<server challenge UUID>","idToken":"<Apple identity token>"}
```

On success the existing response contains `data.accessToken`, `data.refreshToken` and
`data.accessTokenExpiresAt`. Native login does not set a browser refresh cookie. Store the refresh
token in the native platform's protected storage. Refresh and logout use `PUT` and `DELETE
/api/tokens/native` respectively. Device and IP metadata come from the HTTP request.

Invalid or replayed credentials return 401; malformed requests return 400. Challenges expire
after five minutes. Accounts are resolved by Apple's stable `sub`; private relay email works.
New users require the seeded `User` role. Matching email addresses do not automatically link
an existing CoffeePeek account. Blocked and deleted accounts cannot sign in.

Apple authorization codes are accepted only by the browser callback. Apple refresh-token storage,
revocation and server-to-server account-change notifications are not implemented.
