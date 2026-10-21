# Device push notifications

Account owns device registrations, preferences, activity timestamps and the persistent push delivery queue.
Android and iOS use Firebase Cloud Messaging (FCM HTTP v1), project `coffeepeek`. There are no mobile projects in this repository.

## Configuration

Apply Account migrations before starting the service. Delivery is disabled by default:

```text
PushNotifications__Enabled=true
PushNotifications__ProjectId=coffeepeek
PushNotifications__InactiveDays=7
PushNotifications__ReminderRepeatDays=7
GOOGLE_APPLICATION_CREDENTIALS=/absolute/path/to/service-account.json
```

Use a service account authorized to send FCM messages in `coffeepeek` (Firebase Cloud Messaging API enabled,
and the Firebase Cloud Messaging API Admin role or an equivalent narrow custom role). Credentials are loaded
through Google Application Default Credentials and the existing Google.Apis.Auth package; no Firebase database is needed.
Keep the JSON key outside the checkout and images. Never send it to mobile clients.

For the supported local helper, put machine-specific environment overrides in the ignored
`dev/dev.local.env`; `dev/run-services.sh` loads this after `dev/dev.env`:

```sh
export GOOGLE_APPLICATION_CREDENTIALS='/absolute/path/to/service-account.json'
export PushNotifications__Enabled=true
export PushNotifications__ProjectId=coffeepeek
```

On Windows the credential path can use forward slashes (`C:/Users/.../service-account.json`).
The local setup stores the key under `%LOCALAPPDATA%/CoffeePeek/firebase/`, outside the checkout.

For Docker Compose, place the credential in `/etc/coffeepeek/firebase/service-account.json`, readable by
the application container's non-root user. `FIREBASE_CREDENTIALS_DIR` changes the host directory; the mount is read-only.
Set `PUSH_NOTIFICATIONS_ENABLED=true` in the deployment environment after configuring it. With delivery disabled,
the service still accepts registrations and queues events; pending messages retain their original expiry.

FCM must be configured in both mobile applications. iOS additionally needs an APNs authentication key or certificate
in the Firebase project and the application's Push Notifications capability.

## Mobile contract

All endpoints go through Gateway with `Authorization: Bearer <access-token>`. User identity always comes
from the authenticated context; these endpoints do not accept another user's ID.

| Method | Gateway path | Body / behavior |
| --- | --- | --- |
| PUT | `/api/v1/me/notifications/devices/{installationId}` | `{"token":"FCM_REGISTRATION_TOKEN"}`; returns 204 |
| DELETE | `/api/v1/me/notifications/devices/{installationId}` | Unregister this account's installation; idempotent 204 |
| GET | `/api/v1/me/notifications/preferences` | Returns `{"moderation":true,"inactivity":false,"custom":true}` by default |
| PUT | `/api/v1/me/notifications/preferences` | Same object; replaces category preferences, returns 204 |
| POST | `/api/v1/me/notifications/activity` | No body; records an application visit using server time, returns 204 |

Generate a random UUID `installationId` once per app installation and keep it in local app storage.
Ask for the OS notification permission, obtain the **FCM registration token** (also on iOS), and register it
after login. Repeat registration when FCM refreshes its token and on foreground entry (at most once per day is sufficient).
One installation belongs to one account; registering after an account switch replaces the previous binding.
An account can have up to ten installations. Never log device tokens.

Call the activity endpoint whenever the authenticated application enters the foreground, even when OS notifications
are denied or disabled. A token refresh alone is not a reliable app activity signal. Device registrations expire after
30 days without renewal. **Unregister before logout**. Native clients also disable automatic FCM initialization and
revoke the provider token on logout/account switching, retrying failed revocation before registering another token.
Offline logout remains local and cannot recall a push already accepted by the provider. Account deletion cascades
registrations and queued messages.

The push contains a visible title/body and string-valued data:

```json
{
  "notificationId": "uuid",
  "category": "Moderation",
  "type": "CheckInModeration",
  "userId": "recipient-user-uuid",
  "entityType": "CheckIn",
  "entityId": "moderation-submission-uuid",
  "status": "Approved"
}
```

`entityType`, `entityId`, and `status` are present for moderation notifications. Entity types are `CheckIn`, `Shop`,
`Roaster`, `ShopChange`, `ShopIssue`, `CheckInReport`; IDs refer to the respective submission/report/change request.
Treat this as a navigation hint and reload the current state from the API. On account switching, discard data
for another `userId`; the OS can still display a previously accepted push after logout.
Deduplicate foreground handling by `notificationId`; Android notification tags and APNs collapse IDs also use this ID.

## Inbox and master push switch

Apply Account migration AddNotificationInbox before deploying the updated mobile app.
Deploy Account before the updated Shops producers and mobile clients so the new durable
consumers are bound before shop publication events are emitted.
Existing notification rows and category preferences are preserved; existing accounts default
to PushEnabled=true. ReadAtUtc starts null. The existing purge policy (30 days after push expiry)
also limits inbox retention. OS permission and the account switch are independent.

All paths below require the current user's authenticated context, return no-store responses,
and never accept a recipient user ID:

| Method | Path under /api/v1/me/notifications | Behavior |
| --- | --- | --- |
| GET | /?pageSize=30&before={id} | items, nextCursor, unreadCount; newest first; page size 1–100 |
| PUT | /{id}/read | Mark one owned notification read; idempotent 204 |
| PUT | /read | {"throughId":"newest-visible-id"}; mark that snapshot and older rows read |
| GET | /push | {"enabled":true} |
| PUT | /push | Explicit required enabled boolean; returns 204 |

Inbox DTOs contain id, type, title, body, createdAtUtc, readAtUtc, entityType, entityId and status.
The before cursor must identify a row belonging to the same user. Time/ID ordering makes tied
timestamps stable; a read-all snapshot leaves notifications arriving afterward unread.
Foreign notification IDs return 404 for pagination and read operations.

Types are ShopApproved, ShopRejected, RoasterApproved, RoasterRejected, CheckInModeration,
CorrectionModeration, NewShop, Inactivity and General. Existing category/entity/status fields
determine the type, so previously queued moderation decisions also have specific types without
rewriting their text. The same type is included in the FCM data payload.

The master switch gates every category at enqueue and claim time. Disabling also cancels
pending deliveries so re-enabling does not resend the old queue; an already accepted/in-flight
FCM message cannot be recalled. Inbox rows are saved even with push disabled or no devices.
Legacy category PUTs do not reset the master switch. They remain available for older web clients.
Reading history or changing read state does not change push delivery eligibility.

Shops emits ShopPublished only after a new moderated/imported shop has map coordinates.
The event shares the existing publication transaction/outbox and is skipped for duplicate
publication attempts and drafts without coordinates. Account fans out to all active accounts
in durable batches of 200. Per-shop/per-recipient deterministic IDs make retries idempotent;
blocked/deleted accounts are excluded. Every recipient gets an inbox entry; push delivery
also respects the master and existing custom-category preferences.

## Triggers

- Moderation: actual decision changes for check-ins, shops, roasters, shop corrections, shop issue reports and
  check-in complaints. The submitter/reporter receives the notification. Identical no-op decisions do not emit one.
  An approved correction that later fails to apply emits a further status change. Check-in notifications are
  emitted after Shops accepts the current decision, so obsolete revisions do not generate pushes.
- Inactivity: after explicit `inactivity=true`, both the last app visit and the last newly created check-in
  must be at least seven days old. Home and coffee-shop check-ins count. Repeat at most every seven days;
  both intervals are configurable. A worker checks eligible accounts every 15 minutes. Recent activity or an
  opt-out suppresses an already queued reminder before sending. Existing activity is not reconstructed from
  session refreshes or old statistics: tracking starts with this API and new check-in events.
- Custom: an administrator queues a message to one account using the endpoint below.

Moderation producers use Wolverine's transactional RabbitMQ outbox. Account deduplicates notifications by ID and
persists one delivery per registered installation. A leased PostgreSQL queue retries transient failures, honors
FCM `Retry-After`, and survives process restarts. Retry delays grow to one hour; at most eight attempts are made.
Expired, disabled, removed or rebound devices are skipped. Only an FCM `UNREGISTERED` error removes a token;
malformed payloads and authentication failures do not erase registrations. Raw provider responses and credentials
are not logged. Delivery outcomes are stored in `PushDeliveries` (`Accepted`, `Skipped`, `Rejected`, `Exhausted`,
or a pending `Retry`); notification rows are purged 30 days after expiry.

Moderation messages expire after seven days; custom messages and reminders after one day. Successful FCM acceptance
does not prove that the OS displayed a message. A crash between FCM acceptance and saving the outcome can cause
a repeated send; platform collapse IDs and client deduplication reduce duplicates. A send already in flight cannot
be recalled by an opt-out or device reassignment.

## Custom messages

Admin role required:

```http
PUT /api/admin/notifications/89a0506b-11dc-4bb8-826c-a9e210ba3e86
Authorization: Bearer <admin-access-token>
Content-Type: application/json

{"userId":"recipient-uuid","title":"Новости CoffeePeek","body":"У нас появились новые кофейни!"}
```

Returns 202 with `notificationId`. Reuse the same UUID for HTTP retries; reusing it with different content returns 409.
Title and body limits are 120 and 500 characters. The recipient's custom preference is respected. The creator's
user ID is stored for audit. A 202 means the request is recorded, even if the account has no devices or has opted out.
New shop publications broadcast to all active accounts. There is no campaign editor or delivery analytics.

## Editable automatic templates

Admin role required:

- `GET /api/admin/notifications/templates` returns the Moderation, Inactivity and NewShop templates, with defaults when not overridden.
- `PUT /api/admin/notifications/templates/Moderation`, `/Inactivity` or `/NewShop` accepts `{"title":"…","body":"…"}` and returns 204.
- NewShop supports `{name}` for the published shop name (up to 300 characters). Validation reserves that maximum length before saving. Changes affect new notifications; replays preserve already queued text.
- Moderation supports `{subject}` (чекина, кофейни, обжарщика, исправления, жалобы) and `{status}` (localized decision).
  Inactivity has no placeholders. Unknown placeholders and text exceeding 120/500 characters after expansion are rejected.
- Changes affect newly queued notifications. Queued messages and retries of the same event keep their original text.
  Template updates store the administrator's user ID and UTC update time.

The web admin has **System → Уведомления** (`/notifications`), with a preview, template forms and a custom message
form. **Уведомление** on a user row opens this form with the recipient prefilled. After an ambiguous send failure,
retry uses the original ID and payload. It reports queue acceptance rather than claiming delivery.

The customer website has account-wide category switches in Settings and reports foreground activity. It does not
register a browser for push; delivery targets Android/iOS app installations. The updated mobile client exposes one master switch and the notification inbox instead of category switches.

## Native clients and verification

The companion `coffeepeek-cross-mobile` project contains shared session/preferences/device coordination, Android
Firebase Messaging integration and an iOS native delegate. The feed notification panel requests permission on explicit enabling (opens OS settings after a denial), displays foreground
notifications and uses system FCM display in the background. Tapping opens CoffeePeek; there is no per-submission
deep link yet. iOS source activation additionally requires FirebaseCore/FirebaseMessaging in Xcode, the configuration
plist in application resources, Push Notifications entitlement and an APNs credential uploaded to Firebase.
See that repository's `docs/push-notifications.md` for setup and device checks.

Firebase CLI login permits project management and downloading client configuration; it does **not** configure
backend Application Default Credentials. Delivery stays disabled until a server credential is installed and
`PushNotifications__Enabled=true` is set. Never put a server credential into client configuration.

Verified locally: Account notification tests including fresh PostgreSQL migrations, concurrent registration,
enqueueing/claims/reminders, template changes and retention; admin form validation, previews, template save and
idempotent send retry; both web production builds; Android Direct APK/Play compilation and shared notification tests.
On 2026-10-20, actual Android delivery was verified on a Pixel 9 (API 37), using an isolated
`com.coffeepeek.pushdebug` app so the differently signed existing installation and its data were preserved.
Two custom messages were submitted from the real local web admin through Gateway → Account → PostgreSQL queue → FCM.
Both were accepted on attempt one and appeared as Android system notifications: one while the app was open
(also confirmed by the device owner), one after the owner sent it to the background. The automatic moderation
template was also edited and saved from the admin UI against the real local API. No production deployment was performed.
APNs delivery and an Xcode build still require the macOS/APNs environment.

The supplied service-account credential was checked against `coffeepeek` using an authenticated FCM HTTP v1
`validate_only=true` request: HTTP 200. This confirms server authorization and API access without sending a
notification, and does not prove device registration, OS display or APNs delivery.

The local test infrastructure includes PostgreSQL, RabbitMQ, Redis, Account (5353) and Gateway (8080).
The dev launcher supplies service discovery endpoints so YARP resolves those host-run services correctly.
Shops/Moderation/Media are not running in that limited test setup; their unrelated pages can return 502.

The earlier full .NET suite had two existing Gateway Dockerfile assertions failing (Account/Media explicit
`ASPNETCORE_URLS` expectations); these files were unchanged. The focused notification tests and Release build pass.

Sources: [FCM HTTP v1 authorization and sending](https://firebase.google.com/docs/cloud-messaging/send/v1-api),
[FCM error codes](https://firebase.google.com/docs/cloud-messaging/error-codes),
[Apple client setup](https://firebase.google.com/docs/cloud-messaging/ios/get-started).
