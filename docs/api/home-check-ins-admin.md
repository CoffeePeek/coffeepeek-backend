# Admin handoff: home coffee check-ins

Home check-ins are a private owner diary. The feature adds no administrator or moderator endpoint to read, edit, delete, publish, or moderate someone else's home entries. An Admin or Moderator JWT may use `/api/v1/check-ins/home` only for entries owned by that JWT's user ID. There is no request parameter for selecting another owner.

## Access matrix

| Surface | Access and effect |
| --- | --- |
| `POST/GET /api/v1/check-ins/home`, `GET/PUT/DELETE /api/v1/check-ins/home/{id}` | Authenticated owner only, including when that owner has an admin role. Other owners receive `404` for detail, update, delete, and protected photos. |
| `GET /api/v1/check-ins/home/calendar?from=...&to=...` | Signed-in owner's local-date counts only. Both dates are required, inclusive, at most 366 days apart. |
| `GET /api/v1/check-ins/home/stats/beans` and `/stats/methods` | Signed-in owner's statistics only; no `userId` override. |
| `GET /api/v1/check-ins/{id}/photos/{photoId}` | Reuses the protected check-in byte route. A home photo is available to its author and inaccessible to moderators or other administrators on the basis of role alone. |
| `GET /api/v1/moderation/check-ins` and `/api/v1/check-ins/{id}/moderation-photos/{photoId}` | Home entries never enter the moderation queue and have no moderation snapshot or moderation photo access. |
| Public `/api/v1/check-ins`, `/api/v1/feed`, shop statistics, and public profile counts | Home entries contribute no cards, ratings, shop counts, locations, or public `CheckInCount` increments. |

For example, a moderator querying another person's private entry receives the same not-found result as an unknown ID:

```http
GET /api/v1/check-ins/home/11111111-1111-4111-8111-111111111111
Authorization: Bearer <moderator JWT for a different user>

HTTP/1.1 404 Not Found
Content-Type: application/json
```

The response body is the standard `ErrorResponse` with message `Home check-in not found.` and no owner details.

An admin's own home history, calendar, and bean/method statistics use the [client contract](home-check-ins-client.md) exactly. `PUT` replaces editable values while keeping photos; `DELETE` removes the entry from personal reads and stats. A 401 means no valid JWT; invalid date, bean, method, score, dose, duration, drink, or photo input returns 400 as described in the client contract. Private IDs owned by another user remain 404. There is no admin override for these cases.

## Coffee product catalog operations

Home bean selection uses the **published CoffeeProduct** catalog. A client sends `coffeeProductSlug`; the server resolves the current published product and stores its GUID plus a display-name snapshot on each home entry. Admin catalog operations already exist behind the Moderator policy:

| Method and route | Existing operation |
| --- | --- |
| `GET /api/admin/coffees?status=Published&page=1&pageSize=20` | List products, including GUID, slug, status, content, warnings, and version in the normal response envelope. |
| `GET /api/admin/coffees/{id}` | Read one editable product; unknown ID returns 404. |
| `POST /api/admin/coffees` | Create a catalog draft. |
| `PATCH /api/admin/coffees/{id}` | Edit content, country codes, protected fields, and publication status with the current version. Stale version is a conflict (409). |
| `DELETE /api/admin/coffees/{id}` | Archive the product; 204 on success, 404 for an unknown ID. |

Example archive request and response:

```http
DELETE /api/admin/coffees/22222222-2222-4222-8222-222222222222
Authorization: Bearer <moderator JWT>

HTTP/1.1 204 No Content
```

Archiving or renaming a product changes future selection availability or catalog display. Existing home entries keep their original `coffeeProductId` and `beanName` snapshot, so an author's historical diary still makes sense. When editing such an entry, its owner can send the existing `coffeeProductId` on `PUT /api/v1/check-ins/home/{id}` to retain the saved snapshot without requiring the product to remain published. A different ID is rejected. Home dose is stored independently from `CoffeeVariant.weightGrams` (package size). Custom bean names need no catalog entry. The fixed home method codes (`espresso`, `pour_over`, `batch_drip`, `moka_pot`, `cezve`, `aeropress`, `french_press`, `other`) have no admin mutation API in this feature.

The older `/api/admin/beans` and `/api/admin/brew-methods` catalogs are separate shop data. Managing them does not change home product selection or home method codes. See [coffee-catalog-api.md](../coffee-catalog-api.md) for the complete existing coffee catalog edit contract.
