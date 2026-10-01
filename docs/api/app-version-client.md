# Client: app version check (in-app and mandatory updates)

How the mobile app finds out whether it is outdated, and whether the update is optional or mandatory.

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

---

## Endpoint

```http
GET /api/v1/app-version?platform={platform}&channel={channel}
```

| Query | Required | Values | Notes |
|-------|----------|--------|-------|
| `platform` | yes | `android`, `ios` | Case-insensitive. |
| `channel` | no, Android only | `play`, `apk` | Where the app was installed from (see [Android](#android)). Omitted = Play when it is enabled, otherwise APK. Sending a channel with `ios` returns 400. |

- Auth: none. Do not send a token; outdated clients must be able to check before login.
- Caching: `Cache-Control: no-store`. The policy can change at any moment, so do not cache over HTTP.
- Rate limit: `global` gateway policy (300 requests/minute per client).

---

## Types

```ts
type ApiResponse<T> = {
  isSuccess: boolean;
  message: string;
  data: T | null;
  statusCode: number | null;   // set on errors, null on success
};

type AppVersion = {
  versionCode: number;     // latest build available from `url`
  minVersionCode: number;  // builds below this MUST update; 0 = no forced update
  url: string;             // absolute HTTPS: Play / App Store page, or the APK file
};
```

Guarantees:

- `minVersionCode <= versionCode`. The server caps the floor at the build that is actually downloadable, so a forced update can never loop.
- `versionCode` and `url` always come from the same channel. The build at `url` is the build in `versionCode`.
- `versionCode` can go **down** after a rollback. Only compare numbers; never assume it only increases.

---

## Responses

**200**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "versionCode": 140,
    "minVersionCode": 120,
    "url": "https://play.google.com/store/apps/details?id=by.coffeepeek.app"
  },
  "statusCode": null
}
```

**Errors.** The body is the same envelope with `data: null`.

| Status | `message` | Meaning |
|--------|-----------|---------|
| 400 | `Platform must be 'android' or 'ios'.` | Missing or unknown `platform`. |
| 400 | `Channel must be 'play' or 'apk' and is only supported on android.` | Unknown `channel`, or `channel` sent with `ios`. |
| 404 | `App version is not configured for this platform.` | No version policy or no download link for this platform/channel yet. |
| 429 | — | Gateway rate limit. |

---

## Client algorithm

Run on cold start and each time the app returns to the foreground.

```ts
async function checkForUpdate(current: number, platform: "android" | "ios", channel?: "play" | "apk") {
  let policy: AppVersion;
  try {
    const qs = new URLSearchParams({ platform, ...(channel ? { channel } : {}) });
    const res = await fetch(`${baseUrl}/api/v1/app-version?${qs}`);
    if (res.status !== 200) return;               // fail open: 4xx/5xx/429 → let the user in
    policy = (await res.json()).data;
  } catch {
    return;                                       // fail open: offline/timeout → let the user in
  }

  if (current < policy.minVersionCode) {
    showForcedUpdate(policy.url);                 // blocking, not dismissible
  } else if (current < policy.versionCode && dismissedVersionCode !== policy.versionCode) {
    showSoftUpdate(policy.url, policy.versionCode); // dismissible; store dismissedVersionCode on dismiss
  }
}
```

| Condition | UI |
|-----------|----|
| `current < minVersionCode` | **Forced update.** Full-screen, no close or back. Only action: open `url`. Keep it up on resume until the app is updated. |
| `minVersionCode <= current < versionCode` | **Soft update.** Dismissible prompt. After a dismissal, don't show it again for the same `versionCode`. |
| `current >= versionCode` | Nothing. |
| Request failed / non-200 | Nothing. **Never block on errors.** Otherwise offline users and a backend outage would lock everyone out. |

Run the check in parallel with startup. Don't make app launch wait on it.

---

## Android

- **`current`**: `PackageInfo.longVersionCode` (`versionCode` in Gradle).
- **`channel`**: the installer package name.
  - API 30+: `packageManager.getInstallSourceInfo(packageName).installingPackageName`
  - Older: `packageManager.getInstallerPackageName(packageName)`
  - `"com.android.vending"` → `play`; anything else (including `null`) → `apk`.
  - Always send it. A sideloaded build can't update from Play, and the two channels can report different versions.
- **`play`**: `url` is the Play Store page; open it with an `ACTION_VIEW` intent. You can optionally use the Play Core In-App Updates API (`AppUpdateType.IMMEDIATE` for forced updates, `FLEXIBLE` for soft ones). The decision to force still comes from this endpoint.
- **`apk`**: `url` is the APK file itself (HTTPS).
  - Download it, then install with `PackageInstaller`. This needs `REQUEST_INSTALL_PACKAGES` and the user's "install unknown apps" consent.
  - Optional integrity check: `GET /api/v1/app-downloads` → `data.android.apk.sha256`. Use it only when `data.android.apk.versionCode` equals the `versionCode` you are installing.

## iOS

- **`current`**: `Bundle.main.infoDictionary["CFBundleVersion"]`, parsed as an integer.
  - The build number must be a plain, increasing integer (`"45"`), not `"1.2.3"`. A build number that doesn't parse as an integer can't be compared.
- Don't send `channel`.
- `url` is the App Store page; open it with `UIApplication.shared.open(url)`. iOS has no in-app install, so a forced update is a blocking screen with a button to the App Store.

---

## Notes

- One request per platform. The server holds one shared `minVersionCode` for Android (both channels) and one for iOS.
- A 404 is expected for Play and iOS until an admin sets the version policy once after deploy. Treat it as "no update info".
- Older app builds that don't include this check can't be forced to update. Forced updates only apply to builds that ship this check.
