# Drinks in reviews and check-ins

`GET /api/catalogs/drinks` returns `Response<ConsumedDrinkOptionDto[]>` with active catalog entries and a final `other` option (`category: null`). The existing `GET /api/menu/drinks` remains the catalog for shop menus and does not include Other.

Create a check-in or submit/edit a review using these optional fields:

```json
{ "drinkSlug": "cappuccino" }
```

For a specialized drink:

```json
{ "drinkSlug": "other", "customDrinkName": "Эспрессо-тоник" }
```

One drink per entry. Other requires a trimmed name of 1–100 characters. Catalog selections reject custom names, unknown slugs and inactive drinks. Selection is independent of the shop's menu. User-entered names are reviewed with the review and never added automatically to the catalog.

Review edits that omit both fields preserve the previous selection. To remove it, send `clearDrink: true` without a drink selection. Repeating an unchanged selection remains valid after deactivation. Changing a selection validates the new drink against the current catalog. Editing the review does not rewrite the original check-in.

Review, moderation-review and check-in DTOs return `drinkSlug`, `customDrinkName`, `drinkNameRu` and `drinkNameEn`. Names are snapshots taken at selection time. Existing entries have null fields. Display the saved names for historical entries, including drinks no longer selectable in the catalog. Clients cannot set snapshot names or the internal trusted selection.

## Admin API

All admin operations require the `Admin` policy at the gateway and Shops service:

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/admin/drinks` | List all entries, including inactive drinks |
| POST | `/api/admin/drinks` | Create a catalog entry |
| PUT | `/api/admin/drinks/{slug}` | Edit or deactivate an existing entry |

Create body (PUT uses the same fields except `slug`):

```json
{
  "slug": "espresso_tonic",
  "nameRu": "Эспрессо-тоник",
  "nameEn": "Espresso tonic",
  "category": 1,
  "aliases": "эспрессо-тоник,espresso tonic",
  "sortOrder": 150,
  "isActive": true
}
```

Category: `1` espresso, `2` filter. Slugs are unique, immutable and limited to 50 characters (`a-z`, digits, `_`, `-`). `other` is reserved and is not an editable catalog row. Names are limited to 100 characters and aliases to 500. PUT replaces the editable fields; set `isActive: false` to deactivate. Deactivated entries remain available to admins and can be reactivated. Physical deletion is not exposed.

The same catalog is used for shop menus. Admin updates invalidate menu-catalog and shop-detail caches. Custom review/check-in names do not create menu items.

## Deployment

Apply the `AddConsumedDrinks` EF migrations to Shops and Moderation before deploying the updated services. Enable selection in clients after both services are updated. The Shops migration adds Lungo as a separate entry and removes `lungo` from Americano's aliases. Previous migrations and their shared seed array remain unchanged.

Moderation validates new catalog selections through Shops. Catalog outages return 503 rather than silently accepting an unverified drink. Other and empty selections do not require a catalog request. Queued public check-ins carry the already validated snapshot, so later deactivation does not prevent moderation.
