# Account email templates

Account emails use published templates hosted in Resend. Text, subject, sender and
HTML are managed in the Resend dashboard; AccountService only selects the template
and supplies its recipient and variables. Support requests still use plain text.

| Event | Template alias | Link path |
| --- | --- | --- |
| `UserRegisteredInternalEvent` (including confirmation resends and email changes) | [`confirm-email`](https://resend.com/templates/fc97dc63-1e04-434b-9754-c3e79a9369dc) | `/confirm-email` |
| `PasswordResetRequestedInternalEvent` | [`reset-password`](https://resend.com/templates/f988efe3-86a7-40fb-9d35-4855562f0824) | `/reset-password` |
| `AccountDeletionRequestedInternalEvent` | [`confirm-account-deletion`](https://resend.com/templates/9d467820-b11e-482d-89f0-fa439adc0358) | `/confirm-account-deletion` |

All three templates require these variables without fallback values:

| Variable | Resend type | Value supplied by AccountService |
| --- | --- | --- |
| `USERNAME` | string | HTML-encoded username |
| `CONFIRMATION_URL` | string | HTML-encoded action URL, including its URL-encoded token |
| `YEAR` | number | Current UTC year |

The existing HTML was imported with `{{{USERNAME}}}`, `{{{CONFIRMATION_URL}}}` and
`{{{YEAR}}}` placeholders. Use the URL placeholder for both the button and the
fallback link. Username and URL values are already HTML-encoded for triple-brace
substitution; use them in HTML content, not the subject or sender.

Token generation, expiration, validation, resend limits and Wolverine retries stay
in the backend. The imported text describes the current lifetimes: email confirmation
10 minutes, password reset 30 minutes, and account deletion 24 hours. Update the last
notice if `AccountDeletionOptions:TokenLifetimeHours` changes.

Publish all three templates in the Resend account used by `ResendClientOptions:ApiToken`
before deploying AccountService. Keep the aliases and variable keys unchanged when
editing content, and publish edits to make them live. A missing or unpublished template,
missing required variable or rejected send fails the handler so Wolverine can retry.
There is no local HTML fallback.

No database migration or new service configuration is required. The handler class
names are unchanged, preserving existing RabbitMQ queues. Wolverine adapters were
regenerated for the updated constructors. The unused local welcome-back renderer was removed; this migration
does not add a welcome email.
