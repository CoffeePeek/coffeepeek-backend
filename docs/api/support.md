# User support

The Account service exposes public support contact information and accepts requests from users who cannot sign in. All routes are available through the gateway without a token.

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/support` | HTML page with the contact email; use the public gateway URL for the App Store Connect **Support URL**. |
| `GET` | `/api/support/contact` | JSON contact information: `{ "email": "info@coffeepeek.by" }`. |
| `POST` | `/api/support/requests` | Send a message to support. |

Submit a request with JSON:

```json
{
  "email": "user@example.com",
  "message": "I cannot sign in to my account. Please help."
}
```

`email` must be valid and at most 254 characters. `message` must contain 10–4000 characters after trimming. Success returns `204 No Content`. Invalid input returns `400`; gateway throttling returns `429` after five submissions per client IP per hour. Delivery errors are surfaced as server errors, so clients must not show a success message unless they receive `204`.

The support inbox is `Support:Email` (default `info@coffeepeek.by`). The Account service uses the existing Resend configuration to send plain-text messages from `info@coffeepeek.by` with the user's address as `Reply-To`. A configured Resend API token and a verified sender domain are required for live delivery. Local `dev/dev.env` contains a placeholder token, so the send endpoint cannot deliver mail with the stock development configuration.

Before submitting the iOS app, set the Support URL in App Store Connect to the public gateway URL ending in `/support`, and expose a support entry in the app's UI. For example, if the gateway is at `https://api.coffeepeek.by`, the URL is `https://api.coffeepeek.by/support`. [Apple describes the Support URL as a required website with actual contact information.](https://developer.apple.com/help/app-store-connect/reference/app-information/platform-version-information/)
