# SPEC: Duplicate suggestions + file-import mark

**Audience:** frontend/admin (`coffee-peek-admin`) + ops  
**Backend:** this change (Moderation + Shops). Additive APIs, no breaking public catalog contracts.

## What the backend now does

1. **Auto-merge on ingest stays strict** (OSM id / Instagram / phone / same name within ~100 m). Silent merge, as before.
2. **Looser lookalikes are NOT merged.** A job writes **pending duplicate suggestions** for admin confirm/reject. Typical case: `Surf Coffee` + `пр. Незалежнасці, 25` vs `проспект Независимости, 25` with coords drifted 100–350 m.
3. **File-imported places are marked:**
   - queue: `source=File`, signal `import:file`, `importedFromFile=true`, `createdAtUtc`
   - published shop: `importedFromFileAt` (admin-only, not a public tag)

## Duplicate APIs

All under `/api/admin/import`, Moderator/Admin JWT. Gateway already routes the prefix.

| Method | Path | Body / query | Success |
|---|---|---|---|
| POST | `/duplicates/refresh` | — | `{ scanned, suggested, alreadyTracked }` |
| GET | `/duplicates?status=Pending&page=1&pageSize=20` | `status`: Pending=1, Confirmed=2, Rejected=3 | list + `X-Total-Count` |
| POST | `/duplicates/{id}/decide` | `{ "accept": true \| false }` | `{ suggestionId, status, keeperCandidateId, mergedCandidateId }` |

Ingest `POST /file` now also scans and returns `suggestedDuplicates` on the existing result.

**GET item shape (camelCase):**

```json
{
  "id": "…",
  "score": 84,
  "distanceMeters": 142.2,
  "reasons": ["same-name", "same-house-nearby", "similar-address", "distance:142m"],
  "status": "Pending",
  "left": { "id": "…", "source": "Osm", "name": "Surf Coffee", "address": "пр. Незалежнасці, 25, Мінск", "latitude": 53.90, "longitude": 27.56, "phone": null, "website": null, "instagram": null, "queueStatus": "Pending", "importedFromFile": false, "resultingShopId": null, "externalId": "node/1" },
  "right": { "id": "…", "source": "File", "importedFromFile": true, "…": "…" }
}
```

**Accept (`accept: true`)**
- Picks a keeper (Published > OSM > richer contacts).
- Enrich keeper from the other row (fill-empty only).
- Other row → Rejected + reason `Duplicate` (4). Does **not** publish.
- If keeper already has a catalog shop, contacts/address are fill-empty enriched.
- 400 if **both** are already published as different shops.

**Reject (`accept: false`)**
- Pair is remembered. Refresh will not show it again. Both rows stay in the queue.

Refresh is idempotent: existing pairs (any status) are skipped.

## File-import mark APIs

| Surface | How |
|---|---|
| Queue | `GET /api/admin/import/candidates?source=File` (Osm=1, File=2). DTO adds `createdAtUtc`, `importedFromFile`. |
| Stats | `pendingDuplicates` on `GET /api/admin/import/stats`. Rejected reasons include `duplicate`. |
| Catalog | `GET /api/admin/shops?importedFromFile=true`. DTO adds `importedFromFileAt`. |

Do **not** add a public shop tag like `imported_from_file` — it would leak into the user app filters.

## Admin UI (this is the FE work)

### 1. New tab «Похожие» ("Similar") next to Inbox / Queue / Stats

List pending suggestions. Each card is a pair:

- Name + address + source badge (OSM / `файл` = file) + coords + distance + score + reason chips
- Map links already on candidate research if you reuse inbox links; otherwise Google/Yandex from lat/lon
- Buttons: **Это одно место** ("Same place") / **Разные места** ("Different places")
- Toast on success; invalidate `['admin', 'import']`
- Empty state: «Похожих пар нет. После загрузки JSON нажмите “Найти похожие”.» ("No similar pairs. After uploading JSON, click 'Find similar'.")

### 2. Stats page

- Keep existing OSM refresh + `import-decisions.json`.
- Keep/add **Загрузить JSON мест** ("Upload places JSON") → `POST /file` (previous TZ).
- New button **Найти похожие** ("Find similar") → `POST /duplicates/refresh`, toast `предложено {suggested}` ("{suggested} suggested").
- Stat chip **Похожие** = `pendingDuplicates`, click → «Похожие» tab.

### 3. Inbox / queue

- Filter «Из файла» ("From file") = `source=File`.
- Badge `файл` when `importedFromFile`.
- After file ingest, if `suggestedDuplicates > 0`, toast + link to the «Похожие» tab. **Do not auto-merge.**

### 4. Published shops admin list

- Filter checkbox «Импорт из файла» ("Imported from file") → `importedFromFile=true`.
- Show `importedFromFileAt` as relative date.

### 5. Client

```ts
IMPORT_DUPLICATES: '/api/admin/import/duplicates',
IMPORT_DUPLICATES_REFRESH: '/api/admin/import/duplicates/refresh',
IMPORT_DUPLICATE_DECIDE: (id: string) => `/api/admin/import/duplicates/${id}/decide`,
```

Map `Pending`/`Confirmed`/`Rejected` from string or number like other import enums.

## Ops

After deploy, apply migrations (`make up-mod` + `make up-shops`). Then as moderator:

```
POST /api/admin/import/duplicates/refresh
```

or `dev/suggest-import-duplicates.sh` against a running gateway.

Re-run after each JSON dump if you did not ingest through `POST /file` (that path already scans).
