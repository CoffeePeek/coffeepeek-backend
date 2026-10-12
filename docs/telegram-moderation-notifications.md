# Telegram notifications for moderation submissions

The Moderation service sends a Telegram message when a new user submission enters a moderation queue: coffee shops, roasters, shop changes, shop issue reports, check-ins (including a new content revision), and complaints about check-ins. Anonymous shop and roaster submissions are included. Admin imports and moderator decisions do not create notifications.

Set `TELEGRAM_MODERATION_BOT_TOKEN` and `TELEGRAM_MODERATION_CHAT_ID` in `deploy/.env` before deploying. Docker Compose maps them to `TelegramModeration__BotToken` and `TelegramModeration__ChatId` on the Moderation service. For local runs, set those two .NET environment variables directly. Create a bot using [BotFather](https://core.telegram.org/bots/tutorial), open a conversation with the bot (or add it to the intended group), and use that chat's ID. Keep the token only in the environment file or secret store; do not commit it.

Each submission emits a `ModerationSubmissionCreated` message through Wolverine's transactional outbox. The Moderation service consumes it and calls Telegram's [`sendMessage`](https://core.telegram.org/bots/api#sendmessage). A failed send throws so Wolverine retains the failure for retry/error-queue handling; it does not roll back the user's accepted submission. If delivery is retried after Telegram has accepted a message but before Wolverine records success, a duplicate Telegram message is possible. The submission ID in each message lets the moderator identify it.

Both settings may be empty during development; in that state notifications cannot be delivered. Set both before relying on the feature. Setting only one is rejected at startup.

## Optional Cloudflare relay

When the VPS cannot reach Telegram directly, deploy `deploy/cloudflare/coffeepeek-proxy.mjs` to the existing `coffeepeek-proxy` Worker. Its Gemini `/v1beta/models/...:generateContent` route is preserved. In Cloudflare, open the Worker's **Settings > Variables and Secrets**, add a **Secret** named `TELEGRAM_MODERATION_BOT_TOKEN` with the same value as on the VPS, and deploy. Do not put the token in source code.

Set this additional value in the VPS `deploy/.env` and recreate only Moderation after deploying the updated backend:

```dotenv
TELEGRAM_MODERATION_RELAY_URL=https://coffeepeek-proxy.goloologo.workers.dev/telegram/sendMessage
```

Compose maps it to `TelegramModeration__RelayUrl`. This must be a trusted HTTPS endpoint without user credentials, query, or fragment. Empty keeps direct delivery. The client sends the existing bot token in the `Authorization: Bearer` header and the normal `chat_id`/`text` JSON body. The Worker verifies the token against its secret and forwards only `sendMessage` to the fixed Telegram host. Redirects are disabled, upstream errors retain their status, and network errors return a generic 502 without the secret URL. Do not log Authorization headers or request bodies in the Worker. No IPv6 or host-network changes are required.

Test the Worker locally without credentials or external calls with `node --test deploy/cloudflare/coffeepeek-proxy.test.mjs`. The live route should return 401 to an unauthenticated POST once its secret is configured (503 means the secret is missing; 404 means the route has not been deployed). Successful live delivery still requires checking the authenticated request and the application's durable queue. Failed notifications already in Wolverine's dead-letter table require a deliberate replay; enabling the relay does not replay them automatically.

The Bot API request path starts with `/bot`: real bot tokens contain `:`, so a path without the leading slash is interpreted as a URI scheme instead of a path on `api.telegram.org`. HTTP failures and timeouts are retried through the durable queue after 10, 60 and 300 seconds; exhausted failures remain in the error queue.

New public check-in revisions without text (including whitespace) and without photos are approved automatically. The decision uses the normal revision-checked publication flow, and the Telegram notification identifies the automatic approval. Adding text or submitting photos still requires moderation. Existing submissions are not backfilled.
