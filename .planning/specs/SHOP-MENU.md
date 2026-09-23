# SPEC: Coffee shop menu (catalog drinks + photo parse)

**Status:** ready to implement  
**Audience:** backend agent (this repo) + client/admin-UI (separate)  
**Created:** 2026-08-26  
**Parser:** Gemini vision (`generateContent` + JSON schema) — locked

---

## Goal

Every coffee shop has a **coffee drink menu** drawn from a shared catalog: a drink is either present (with a price), absent, or not yet known. Moderators (catalog queue + user submission) and users creating a coffee shop upload **menu photos**. Gemini recognizes the items, maps them to the catalog, and computes `priceRange` from the average price in BYN. In the coffee shop profile, the client sees the menu and the date it was uploaded/updated. The data can be edited manually.

## Background (code today)

- There is **no** menu. `CoffeeShopDetailsDto` returns beans, brew methods, equipment, `priceRange` — not a drink price list.
- `Beverages` in the Shops Domain is declared and **not used anywhere** — do not use it as a basis.
- `PriceRange` already exists: `Cheap | Moderate | Expensive`. Currently set by hand, not derived from prices.
- Two moderation queues (do not mix the entities):
  - `ShopImportCandidate` — catalog (OSM / CoffeeMap / file). `/api/admin/import/...`
  - `ModerationShop` — user submission. `POST /api/ModerationShops` already accepts `ShopPhotos`.
- Photos: Media `POST /api/Photos/shop` (presign) → confirm. Coffee shop gallery ≠ menu photos.
- Import publishing: `ImportCandidatePublishedEvent` → `CreateShopFromImportService` (the event carries no menu).
- Submission approval: `CoffeeShopApprovedEvent` → `CreateShopFromModerationService`.

**Product language:** the catalog queue is **moderation**; OSM is only one of the sources. HTTP `/api/admin/import` is **not renamed** in this phase (it would break clients). UI copy: «Модерация» (Moderation). Route rebranding is a separate follow-up.

---

## Locked decisions

1. **Menu = coffee drinks**, not food, desserts, or merch.
2. **The catalog is global** (seed). A coffee shop has availability/price per slug, not a free-form list in v1.
3. **Custom/signature drinks** — the schema is in place, but they are **not returned** in the v1 public response. Raw unmatched parser output is stored.
4. **Source of truth after publishing** — Shops (`ShopMenu` on `CoffeeShop`). Before publishing — a draft on the candidate / `ModerationShop`.
5. **Parser** — Gemini multimodal `generateContent`, `response_mime_type=application/json` + schema. Image bytes come from MinIO (`inlineData`). Not Yandex Vision, not Tesseract.
6. **Price range** from the arithmetic mean of **known prices of Present items** in BYN:
   - `< 7.00` → `Cheap`
   - `7.00 … 9.00` inclusive → `Moderate` ("around 8")
   - `> 9.00` → `Expensive`  
   Thresholds live in `MenuPriceRangeOptions`, not hardcoded in handlers. If there are no prices, `priceRange` is left untouched.
7. **Date for the client** — `capturedAtUtc` (when the photos the menu was built from were uploaded/attached), plus `updatedAtUtc` (last manual edit or re-parse).
8. **Editing in v1:** moderator + admin. The submission author can only **attach photos at creation**. After publishing — `PUT /api/admin/shops/{id}/menu`.
9. **Search/map** do **not** carry the full menu. Only the coffee shop details page.
10. Parsing is **asynchronous** (Wolverine). HTTP does not wait for Gemini.

---

## Catalog v1 (seed, slug immutable)

| slug | nameRu | nameEn | category | aliases (parser) |
|------|--------|--------|----------|------------------|
| espresso | Эспрессо | Espresso | espresso | эспрессо, espresso |
| doppio | Доппио | Doppio | espresso | доппио, doppio, double espresso, двойной эспрессо |
| americano | Американо | Americano | espresso | американо, americano, lungo |
| cappuccino | Капучино | Cappuccino | espresso | капучино, капуч, cappuccino, cappucino |
| latte | Латте | Latte | espresso | латте, latte, caffe latte |
| flat_white | Флэт уайт | Flat white | espresso | флэт уайт, флэт, flat white, flatwhite |
| cortado | Кортадо | Cortado | espresso | кортадо, cortado, piccolo, пикколо |
| macchiato | Макиато | Macchiato | espresso | макиато, macchiato |
| raf | Раф | Raf | espresso | раф, raf |
| batch_brew | Фильтр | Batch brew | filter | фильтр, капелька, batch brew, drip, batch |
| v60 | V60 / воронка | V60 | filter | v60, v-60, воронка, hario, pour over |
| kalita | Калита | Kalita | filter | калита, kalita, wave |
| chemex | Кемекс | Chemex | filter | кемекс, chemex |
| aeropress | Аэропресс | AeroPress | filter | аэропресс, aeropress, aero press |

New standard drinks = **a seed migration**, not an arbitrary insert from the UI in v1.

`kind` on the definition: `Standard` for now; `Custom` is reserved.

---

## Domain

### Shops (published)

`CoffeeDrinkDefinition` — reference table (Id, Slug unique, names, category, aliases[], sortOrder, kind, isActive).

`ShopMenu` (1:1 with `CoffeeShop`):

- `CapturedAtUtc` — upload date of the source photos (the client-facing "freshness")
- `UpdatedAtUtc`, `UpdatedByUserId?`
- `Currency` = `BYN`
- `ParseStatus`: `None | Pending | Running | Ready | Failed`
- `ParseError?`
- `SuggestedPriceRange?` — computed from the items; on Apply it is written to `CoffeeShop.PriceRange`

`ShopMenuItem`:

- `DrinkDefinitionId`
- `Availability`: `Unknown | Present | Absent`
- `Price?` (decimal, Present)
- `VolumeMl?` (nullable, may be left empty in v1)
- `Source`: `Parsed | Manual`
- unique `(ShopMenuId, DrinkDefinitionId)`

`ShopMenuPhoto` — storageKey + media photo id; **not** in the gallery's `ShopPhotos`.

Future (columns added now, not mapped publicly):

- `ShopMenuItem.Kind` (`Standard | Custom`)
- `CustomName?` when Kind=Custom
- `ShopMenu.UnmatchedJson` — raw parser output `{ rawName, price, confidence }`

### Moderation (draft)

On `ShopImportCandidate` and `ModerationShop` — an owned/jsonb `MenuDraft` with the same shape as the public DTO (items for every catalog slug + photos + parseStatus + capturedAtUtc). Do not create a second relational model before publish.

On `decide Published` / submission approval, the draft is copied into `ShopMenu`. If parsing is still `Pending/Running`, publishing is **allowed**; the menu arrives later via a follow-up `MenuDraftReady` event, **or** the moderator waits for Ready. **Locked:** publishing is allowed with an incomplete menu; after Ready, if the shop already exists, Shops applies the draft by `ResultingShopId` / `ModerationId`.

---

## Parse pipeline

1. The client gets a presign via `POST /api/Photos/menu`, uploads to MinIO, confirms.
2. The moderator/user attaches `UploadedPhotoDto[]` to the candidate / submission / published coffee shop.
3. The command sets `ParseStatus=Pending` and publishes the Wolverine `ParseShopMenuCommand`.
4. Handler: reads bytes from MinIO, sends them to Gemini (up to N photos per request, v1 max **4**), JSON:

```json
{
  "drinks": [
    { "rawName": "Капучино", "price": 9.0, "volumeMl": null, "confidence": 0.86 }
  ],
  "unmatched": [],
  "currencyGuess": "BYN"
}
```

5. Mapping: catalog aliases (case-insensitive, RU/EN). Several prices for one slug → take the **minimum** (usually the smaller size). Unmatched → `unmatched`; no Custom items are created in v1.
6. For each catalog slug: match → `Present`+price; not in the photos → **do not set Absent**, leave `Unknown` (missing from the photo ≠ "not served"). Absent is manual only.
7. Compute the suggested `priceRange`. The moderator sees it and can override it.
8. `Failed`: status + `parseError`; the draft is not overwritten.

**Gemini:** `GeminiOptions: ApiKey, Model=gemini-3.6-flash, TimeoutSeconds=60`. HTTP `generativelanguage.googleapis.com` + `IHttpClientFactory`. The key lives in env/user-secrets, not in git.

---

## Contract (JSON, enums as strings)

### `CoffeeDrinkCategory`

`espresso` | `filter`

### `MenuItemAvailability`

`Unknown` | `Present` | `Absent`

### `MenuParseStatus`

`None` | `Pending` | `Running` | `Ready` | `Failed`

### `CoffeeDrinkDefinitionDto`

```json
{
  "slug": "cappuccino",
  "nameRu": "Капучино",
  "nameEn": "Cappuccino",
  "category": "espresso",
  "sortOrder": 40
}
```

### `ShopMenuItemDto`

```json
{
  "slug": "cappuccino",
  "nameRu": "Капучино",
  "nameEn": "Cappuccino",
  "category": "espresso",
  "availability": "Present",
  "price": 9.00,
  "currency": "BYN",
  "volumeMl": null,
  "source": "Parsed"
}
```

Public profile: **all** catalog slugs (the client hides `Unknown` itself). Custom is not included.

### `ShopMenuDto`

```json
{
  "capturedAtUtc": "2026-08-26T18:00:00Z",
  "updatedAtUtc": "2026-08-26T18:12:00Z",
  "currency": "BYN",
  "parseStatus": "Ready",
  "parseError": null,
  "suggestedPriceRange": "Moderate",
  "items": [ "...ShopMenuItemDto" ],
  "photos": [ { "id": "uuid", "fullUrl": "https://..." } ]
}
```

A `null` menu on a coffee shop = not created yet (client: empty state, not 404).

### Additive on existing DTOs

- `CoffeeShopDetailsDto.Menu` → `ShopMenuDto?`
- `ModerationShopDto.Menu` → `ShopMenuDto?`
- `ShopImportCandidateDto.Menu` → `ShopMenuDto?`
- `SendCoffeeShopToModerationCommand.MenuPhotos` → `List<UploadedPhotoDto>?` (like `ShopPhotos`)
- `ImportCandidatePublishedItem` + menu payload (items snapshot) — **additive**; does not break older consumers if the field is optional.

---

## Endpoints

All new fields are additive. Gateway: existing clusters + if needed the path `/api/menu/drinks` → shops-cluster.

### Public

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| GET | `/api/menu/drinks` | anonymous | Standard drink catalog (cached) |
| GET | `/api/CoffeeShops/{id}` | anonymous | Already exists; `menu` is added to `data` |

### Media

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| POST | `/api/Photos/menu` | user | Presign, same as shop, bucket Shop, prefix `menus/`. Does not go into the gallery |

### User — coffee shop submission

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| POST | `/api/ModerationShops` | user | As today + optional `menuPhotos[]`. Sets parse to Pending |

No separate "my menu draft" for users in v1 — after submission only the moderator edits.

### Catalog moderation (import candidate)

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| GET | `/api/admin/import/candidates/{id}` | moderator | Already exists; `menu` in the dossier |
| POST | `/api/admin/import/candidates/{id}/menu/photos` | moderator | Body: `{ photos: UploadedPhotoDto[] }`. Attach + enqueue parse. Sets `capturedAtUtc` if empty |
| POST | `/api/admin/import/candidates/{id}/menu/parse` | moderator | Re-run on already attached photos |
| PUT | `/api/admin/import/candidates/{id}/menu` | moderator | Manual edit of items (`availability`, `price`). `source=Manual` on changed items. Absent can be set. Optional `applySuggestedPriceRange: true` |

### User submission moderation

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| GET | `/api/ModerationShops/{id}` | moderator | + `menu` |
| POST | `/api/ModerationShops/{id}/menu/photos` | moderator | same as for candidate |
| POST | `/api/ModerationShops/{id}/menu/parse` | moderator | re-run |
| PUT | `/api/ModerationShops/{id}/menu` | moderator | manual edit |

### Published coffee shop (admin)

| Method | Path | Auth | Purpose |
|--------|------|------|------------|
| GET | `/api/admin/shops/{id}/menu` | admin | full menu + unmatched (for debugging) |
| POST | `/api/admin/shops/{id}/menu/photos` | admin | photos + parse |
| POST | `/api/admin/shops/{id}/menu/parse` | admin | re-run |
| PUT | `/api/admin/shops/{id}/menu` | admin | edit + optional apply priceRange on `CoffeeShop` |

---

## Flows

```
User/Moderator → Photos/menu presign → MinIO → confirm
     → attach photos (candidate | moderation shop | published shop)
     → ParseShopMenuCommand (Wolverine)
     → Gemini → map catalog → draft/shop menu Ready
     → moderator PUT corrections
     → publish/approve copies menu to CoffeeShop
     → client GET CoffeeShops/{id} sees menu + capturedAtUtc
```

`priceRange` on the card: after Ready it is suggested; it is written to the public `CoffeeShop.PriceRange` if a moderator/admin confirmed it (`applySuggestedPriceRange`) **or** on publish, if the shop still has the default and a suggestion exists. **Locked:** on publish from import, if `CoffeeShop.PriceRange` is not set explicitly, use the suggested value. If a moderator has already set the range manually, do not overwrite it.

---

## Tests (minimum)

- Seed: all v1 slugs are unique; aliases map «воронка» → `v60`, «доппио» → `doppio`.
- Parse mapper: two names for the same slug → one item, min price.
- Unmatched does not end up in the public `ShopMenuDto`.
- Unknown does not become Absent without a PUT.
- PriceRange: mean 6.5 → Cheap, 8 → Moderate, 10 → Expensive; empty price set → null suggested.
- GET details without a menu → `menu: null`.
- GET details with a menu → all catalog slugs are present.
- Publishing a candidate copies Present/Absent/prices and `capturedAtUtc`.
- `SendCoffeeShopToModeration` with `menuPhotos` creates a Pending draft (parser is mocked).
- Gemini failure → Failed + the previous draft survives.
- Menu photos are not serialized into `CoffeeShopDetailsDto.Photos`.

---

## Boundaries

**In scope**

- Catalog + public menu on the details page
- Photos + Gemini parse
- Draft in both moderation queues
- Manual PUT
- Auto `priceRange` from the mean
- `capturedAtUtc` / `updatedAtUtc`
- Groundwork for Custom (columns + unmatched)

**Out of scope**

- Food, desserts, signature drinks in the public API
- Multiple sizes as separate SKUs (min price only)
- Scraping Instagram/websites
- Renaming `/api/admin/import` → `/api/admin/moderation`
- Menu in search/map DTOs
- Users editing the menu after submission / owner self-serve
- Menu revision history (only the two dates exist)
- Multi-currency

---

## Implementation waves

1. **Contract + catalog + ShopMenu schema** (Shops migration, seed, GET `/api/menu/drinks`, `menu: null` in details).
2. **Gemini parser + Wolverine job** (adapter, mapper, options, mapping tests without the live API).
3. **Import candidate** photos/parse/PUT + field in the dossier DTO.
4. **User suggestion** `menuPhotos` + the same admin endpoints on `ModerationShops`.
5. **Publish/approve copy** + apply suggested priceRange.
6. **Admin published shop** GET/PUT/photos/parse.

Each wave is a buildable slice with tests, not "all the infrastructure first".

---

## Acceptance

- [ ] The catalog returns all v1 slugs
- [ ] A catalog moderator can attach menu photos; status goes Pending → Ready (or Failed)
- [ ] A user can attach menu photos in `POST /api/ModerationShops`
- [ ] After parsing, a cappuccino priced 9 becomes `Present` / `9.00` / BYN
- [ ] A drink not in the photos stays `Unknown`, not `Absent`
- [ ] Mean ~8 BYN → `Moderate`; below 7 → `Cheap`; above 9 → `Expensive`
- [ ] `GET /api/CoffeeShops/{id}` contains `menu` with `capturedAtUtc` after publishing
- [ ] PUT changes price/availability; `updatedAtUtc` is updated
- [ ] Menu photos are not in the `photos[]` gallery
- [ ] Custom/unmatched are not visible to the client

---

## Interview / locks

| Topic | Decision |
|------|---------|
| What is in the v1 menu | Only standard catalog coffee drinks |
| Signature drinks | Schema + unmatched, not API |
| Parser | Gemini vision JSON |
| OSM queue | Product = moderation; import routes stay untouched |
| 8 BYN | Moderate in the 7–9 band |
| Who edits | Moderator/admin; user only photos on create |
| For the client | Details page + upload date |
