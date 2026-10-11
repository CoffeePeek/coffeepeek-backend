# Telegram notifications for moderation submissions

The Moderation service sends a Telegram message when a new user submission enters a moderation queue: coffee shops, roasters, shop changes, shop issue reports, check-ins (including a new content revision), and complaints about check-ins. Anonymous shop and roaster submissions are included. Admin imports and moderator decisions do not create notifications.

Set `TELEGRAM_MODERATION_BOT_TOKEN` and `TELEGRAM_MODERATION_CHAT_ID` in `deploy/.env` before deploying. Docker Compose maps them to `TelegramModeration__BotToken` and `TelegramModeration__ChatId` on the Moderation service. For local runs, set those two .NET environment variables directly. Create a bot using [BotFather](https://core.telegram.org/bots/tutorial), open a conversation with the bot (or add it to the intended group), and use that chat's ID. Keep the token only in the environment file or secret store; do not commit it.

Each submission emits a `ModerationSubmissionCreated` message through Wolverine's transactional outbox. The Moderation service consumes it and calls Telegram's [`sendMessage`](https://core.telegram.org/bots/api#sendmessage). A failed send throws so Wolverine retains the failure for retry/error-queue handling; it does not roll back the user's accepted submission. If delivery is retried after Telegram has accepted a message but before Wolverine records success, a duplicate Telegram message is possible. The submission ID in each message lets the moderator identify it.

Both settings may be empty during development; in that state notifications cannot be delivered. Set both before relying on the feature. Setting only one is rejected at startup.
