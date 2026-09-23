# SPEC: Catalog ingest + admin moderation queue

**Status:** ready to implement  
**Audience:** backend agent (this repo) + frontend/admin-UI agent (separate client)  
**Spike source:** `.planning/spikes/001-osm-overpass-minsk-cafes/` (do not copy the HTML; copy the *workflow*)  
**Repo fact:** CoffeePeek-NET is backend-only. Admin UI is a **new client** talking to Gateway (`http://localhost:5000`).

---

## What we already proved (spike)

- OSM Overpass for Minsk ≈ 1580 cafe/coffee/vending points.
- Drop OSM objects not edited in **5+ years** (~162). Queue ≈ 1419.
- OSM is not a specialty catalog. Human looks at Instagram + map photos.
- Google Places (New) Text Search works for **open / closed / not found**. Do not persist Google/Yandex org payloads.
- Yandex Places HTTP is blocked (no quota). Yandex is **links only** (maps card + image search).
- First labeled sample (browser localStorage, not in git): **6 specialty, 2 good coffee, 4 cafe** → **12 in feed**. The rest of the queue is still pending.

Export the spike file `import-decisions.json` from the spike UI and hand it to the backend agent with the OSM `candidates.json`.

---

## Why the spike categories felt like nonsense

The spike mixed four different ideas on one row of buttons:

| Spike button | What it actually was | Keep? |
|--------------|----------------------|--------|
| Specialty / good coffee / decent cafe | **Coffee focus** (who is this place for?) | Yes — 3 values, required to publish |
| С собой / сеть (takeaway / chain) | **Format** (how you use it) | Yes — optional **tag**, not a rival of specialty |
| Не в ленту (not in feed) | **Visibility** (publish vs reject) | Yes — queue action, not a type of shop |
| Позже (later) | **Workflow** (skip) | Yes — queue action, not a type of shop |

A specialty bar can be takeaway. A cafe can be laptop-friendly. Reject is not a kind of coffee.

---

## Locked taxonomy (use this, do not invent a 4th “kind”)

### 1. Queue status — moderator only, not a public filter

`Pending | Skipped | Published | Rejected`

- **Published** → shop is in the user feed.
- **Rejected** → not in the feed; keep the row so OSM re-fetch does not resurrect it.
- **Skipped** → back of the queue (“позже” / later).
- **Pending** → not decided.

### 2. Coffee focus — exactly one, required to publish, **public filter**

This is the only “category” the end user cares about for coffee quality.

| Slug | UI (ru) | UI (en) | Meaning |
|------|---------|---------|---------|
| `specialty` | Specialty | Specialty | Third wave / roastery / origin-focused. Also assign existing ShopTag `specialty`. |
| `coffee_bar` | Кофейня | Coffee bar | Coffee is the product. Not claiming specialty. Spike “хороший кофе” (good coffee). |
| `cafe` | Кафе | Cafe | Food/atmosphere first; coffee still worth listing. Spike “кафе с достойным кофе” (cafe with decent coffee). |

Store as `CoffeeFocus` enum on the **published** `CoffeeShop` (and on the candidate after decide).  
Do **not** make these three mutually exclusive ShopTags only — tags are many, focus is one.  
Implementation: `CoffeeShop.CoffeeFocus` + keep ShopTag `specialty` in sync when focus = Specialty (existing public `?tags=` keeps working).

### 3. Tags — many, optional, **public filters** (already exist)

Amenity (already seeded): `laptop_friendly`, `pet_friendly`, `pour_over`, `quiet_work`, `specialty`.

Add format tags (seed migration):

| Slug | UI (ru) | When moderator ticks |
|------|---------|----------------------|
| `to_go` | С собой | Window / chain / takeaway-first |
| `roastery` | Обжарка | They roast |
| `bakery` | Пекарня | Bakery + coffee |

Moderator can add more later via existing `PUT /api/admin/shop-tags` + `PUT /api/admin/shops/{id}/tags`.

### 4. What the user sees in the app

- Feed = `CoffeeShop` with `Status = Active` (not hidden, not permanently closed).
- Filters: `coffeeFocus=specialty|coffee_bar|cafe` **plus** existing `tags=`.
- Computed chips stay as today: open / new / visited.

---

## Do not reuse `ModerationShop` as the OSM inbox

`ModerationShop` = **owner-submitted** shop (`UserId`, photos, beans, schedules). 1400 OSM stubs will drown real owner applications.

**New aggregate** in Moderation: `ShopImportCandidate`  
**Publish** creates a real `CoffeeShop` in Shops (system/admin as `CreatorId`, `OwnerUserId = null`).  
Admin “published shops” (`/api/admin/shops`) already exists for after-publish edits.

Owner-moderation queue (`/api/ModerationShops`) stays unchanged.

---

## Backend (this repo)

### Domain

`CoffeePeek.Moderation.Domain` — `ShopImportCandidate`:

- `Source` (`Osm`), `ExternalId` (`node/123`) — unique
- Name, address, lat, lon, phone, website, instagram, openingHours, cuisine, brand
- `OsmUpdatedAt`, `OsmAgeDays`, `CheckDate`, `Signals[]`, `CollectorBucket` (priority / review / noise / vending — **inbox sort only**)
- `QueueStatus`, `CoffeeFocus?`, `TagSlugs[]` (chosen on publish)
- `GoogleBusinessStatus?` (`Operational|ClosedPermanently|ClosedTemporarily|NotFound|Far`) — **our** verdict cache, not a Google dump
- `ReviewedByUserId`, `ReviewedAtUtc`, `ResultingShopId?`
- Research URLs computed, not stored as Yandex/Google JSON: Instagram, Google Maps, Yandex maps, Yandex images, OSM history

`CoffeePeek.Shops.Domain` — `CoffeeShop`:

- Add `CoffeeFocus` (nullable only for legacy shops; required for import-published)
- Existing `SetTags`, `Status` (`Active` / `TemporarilyClosed` / `PermanentlyClosed`)

### Ingest

1. Admin-only command: load OSM snapshot (reuse spike Overpass query + 5-year drop). Upsert by `(Osm, ExternalId)`.
2. Optional: apply `import-decisions.json` from the spike (map old keys: `specialty` → focus+Published, `good_coffee` → `coffee_bar`, `cafe` → `cafe`, `to_go` → Published + tag `to_go`, `reject` → Rejected, `yes` → specialty).
3. Do not auto-publish. Decisions file is the only bulk publish.

### Live checks (no catalog theft)

- `GET` candidate detail may **refresh** Google Places Text Search (same field mask as spike: name, address, `businessStatus`, location, `googleMapsUri`). Cache status + maps URL + fetchedAt (≤ 30 days). Do not store full Places JSON in Postgres if avoidable; a small status row is enough.
- If Google = `CLOSED_PERMANENTLY`, default the UI to Reject (moderator can override).
- Yandex Places HTTP: **do not call**. Links only.
- API keys: user-secrets / env (`GooglePlaces:ApiKey`). Never commit.

### HTTP (Gateway → Moderation)

All `Authorize: Admin` or `Moderator`. Suggest:

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/admin/import/osm/refresh` | Pull/upsert OSM Minsk snapshot |
| POST | `/api/admin/import/decisions` | Apply spike JSON |
| GET | `/api/admin/import/candidates` | Queue: `status`, `bucket`, `focus`, search, page. Default `status=Pending`, sort `bucket=priority` first |
| GET | `/api/admin/import/candidates/{id}` | Dossier + research links + last Google status |
| POST | `/api/admin/import/candidates/{id}/google-refresh` | Live Google check |
| POST | `/api/admin/import/candidates/{id}/decide` | Body: `{ status: Published\|Rejected\|Skipped, coffeeFocus?, tagSlugs? }` |
| GET | `/api/admin/import/stats` | Counts by status / focus / bucket |

`decide Published` requires `coffeeFocus`. Creates `CoffeeShop` (name, location, contact, hours if parseable, focus, tags). `CreatorId` = moderator. If Google closed and they still publish → set shop `TemporarilyClosed` or reject — prefer reject unless they override.

Gateway: add route `/api/admin/import/{**remainder}` → moderation-cluster (today `/api/admin/shops` goes to **Shops**).

### Tests

- Unique `(Source, ExternalId)`
- Stale >5y not in default pending queue
- Publish without focus fails
- Publish assigns `specialty` tag iff focus=Specialty
- Reject + OSM refresh does not recreate pending
- Google closed → suggested reject; override still works
- Decisions JSON mapping from spike keys

### Out of scope (backend v1)

- Beans / roasters / brew methods on import
- Owner claim of imported shop
- Autofilter ML
- Persisting Yandex org payloads
- Changing public API contracts without versioning (`CoffeePeek.Contract` — add fields with defaults)

---

## Frontend (admin client — other agent)

CoffeePeek has **no in-repo admin UI**. Build an admin page that a moderator can use for 1500 cards without opening ten tabs by hand.

### Information architecture

```
Admin
  ├── Moderation (existing owner submissions)     ← do not dump OSM here
  ├── Catalog ingest                              ← THIS FEATURE
  │     ├── Queue (one dossier at a time)
  │     ├── Inbox list (table, filters)
  │     └── Stats
  └── Published shops (existing /api/admin/shops)
```

### Screen A — Queue (primary, copy spike 001)

**One shop per screen.** Layout:

1. **Research first** (auto-open or one-click, large):
   - **Открыть в Google** (Open in Google) — `googleMapsUri` if we have a match, else maps search `name + coords`
   - **Instagram** — OSM handle or search fallback
   - **Яндекс · карточка** (Yandex place card) — photos / closed flag with their eyes
   - **Яндекс · картинки** (Yandex images)
   - OSM history
2. **Dossier** (from our API, not from Google dump): name, address, cuisine, hours, phone, website, OSM last edit + age, collector bucket, Google status badge (работает / закрыто / не найдено = open / closed / not found).
3. **Optional preview** of own website `og:image` if website exists (nice-to-have).
4. **Then tags / focus** — only after they looked:
   - Required: Coffee focus (3 radios): Specialty · Кофейня (coffee bar) · Кафе (cafe)
   - Optional chips: to_go, roastery, bakery, laptop_friendly, pet_friendly, pour_over, quiet_work
5. **Actions** (not categories):
   - **В ленту** (to feed; publish) — disabled until focus chosen
   - **Не в ленту** (not in feed; reject)
   - **Позже** (later; skip)
6. Keyboard: `1/2/3` focus, `Enter` publish, `R` reject, `S` skip.

Do **not** show “хороший кофе / кафе с достойным / с собой / не в ленте / позже” (good coffee / decent cafe / takeaway / not in feed / later) as one equal row. Split **focus** vs **actions** vs **tags**.

Default inbox filter: Pending + collector bucket `priority` (coffee signal). Do not force all 1419.

### Screen B — Inbox table

Columns: name, focus (if set), Google status, OSM age, bucket, status.  
Filters: status, bucket, focus, search.  
Click row → Screen A.

### Screen C — Stats

Pending / skipped / published (by focus) / rejected.  
“In feed” = published count only.

### Mapping the first 12

If decisions JSON is applied, those 12 already appear under **Published shops** with the right focus. Frontend should still allow retagging via existing admin shop tags + new focus editor on published shop (backend: PATCH shop focus).

### Visual / UX notes for the drawing agent

- Dark coffee dossier is fine; keep research buttons **above** the fold.
- Opening Google + Instagram + Yandex in new tabs on “Исследовать” (Research) is OK; do not iframe Instagram (blocked).
- Google/Yandex map widgets are optional; **links are mandatory**.
- Empty OSM name `(unnamed)`: show brand or “без имени” (unnamed) + force Google/Yandex first; publishing without a real name is blocked.
- Russian UI copy.

### Frontend does not

- Call Overpass, Google, or Yandex directly (CORS + keys). Always our admin API.
- Store decisions only in localStorage (that was the spike). Server is source of truth.
- Mix this queue with owner `ModerationShops`.

---

## Seed / first deploy

1. Backend: migration `CoffeeFocus` + tags `to_go`, `roastery`, `bakery`.
2. `POST /admin/import/osm/refresh` (Minsk bbox from spike).
3. `POST /admin/import/decisions` with exported JSON → 12 published, rest pending.
4. Frontend: queue starts on remaining pending / priority.

---

## Success

- Moderator can finish a card without hunting URLs.
- User can filter feed by coffee focus (Specialty / Coffee bar / Cafe) and amenity/format tags.
- Rejected and stale OSM rows never hit the public feed.
- Owner shop moderation stays a separate list.
- Google/Yandex are validators and research tabs, not our database of record.
