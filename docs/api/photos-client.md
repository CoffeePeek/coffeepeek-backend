# Client: check-in photos

Check-in photos use private MinIO storage for both coffee-shop visits and home brews. A signed-in user first requests presigned upload URLs from Media, uploads the bytes, then creates a check-in through Shops. The coffee-shop visit contract is in [check-ins.md](check-ins.md), the home contract is in [home-check-ins-client.md](home-check-ins-client.md), and moderation and protected photo reads are in [photos-admin.md](photos-admin.md).

Use the gateway base URL. With `dev/run-services.sh` it is `http://localhost:8080`.

## 1. Request presigned URLs

```http
POST /api/Photos/check-in
Authorization: Bearer {userJwt}
Content-Type: application/json

[
  { "sizeBytes": 734003, "fileName": "latte.jpg", "contentType": "image/jpeg" }
]
```

The `200` response uses the normal `Response<T>` envelope. Each item in `data` contains `photoId`, `uploadUrl` and `storageKey`; the key begins with `check-ins/`. Save that item's `storageKey` for step 3. The upload URL expires after 600 seconds.

## 2. Upload the bytes to MinIO

Send a `PUT` directly to the returned `uploadUrl` with the image bytes and the two signed headers:

```http
PUT {uploadUrl}
Content-Type: image/jpeg
x-amz-tagging: is_permanent=False

<binary image bytes>
```

`Content-Type` must equal the value submitted in step 1. Both headers are part of the signature; omitting or changing either causes MinIO to reject the request. A successful upload needs no separate confirmation call.

## 3. Attach to a check-in

```http
POST /api/v1/check-ins
Authorization: Bearer {userJwt}
Content-Type: application/json

{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-01T09:00:00Z",
  "visibility": 1,
  "photos": [
    {
      "fileName": "latte.jpg",
      "contentType": "image/jpeg",
      "storageKey": "check-ins/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg",
      "size": 734003
    }
  ]
}
```

`visibility` is optional and defaults to Private; `1` requests Public moderation. Text, rating and drink are optional. At most five distinct photos can be attached. Media checks the caller's ownership, uploaded bytes, supported raster type and unused metadata; the client-supplied filename, MIME and size do not authorize attachment. Each uploaded photo is single use.

The same upload flow serves a home brew: attach the returned keys to `POST /api/v1/check-ins/home` as described in [the home contract](home-check-ins-client.md#photos). The original `photos` array keeps its order and accepts at most five distinct uploads. Photos must belong to the author, be fully uploaded, have a supported raster type and remain unused. A key cannot be attached to another check-in. Check-ins without photos need no Media call.

## Optional transparent drink cutout

Upload one additional transparent PNG through `POST /api/Photos/check-in`, using the same PUT headers. Send its metadata as `drinkPhoto`, separately from the original `photos` array. A cutout requires at least one original photo; up to five originals plus one cutout are allowed. The backend does not generate the cutout.

```json
{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-01T09:00:00Z",
  "photos": [{"fileName":"full.jpg","contentType":"image/jpeg","storageKey":"original-upload-key","size":1200000}],
  "drinkPhoto": {"fileName":"cup.png","contentType":"image/png","storageKey":"cutout-upload-key","size":100000}
}
```

Media validates the immutable uploaded copy: it must be a nonanimated PNG of at most 4 MiB, 1–768 pixels on each side, with a visible object and transparent pixels. An invalid or missing cutout fails creation. The cutout is excluded from the original gallery; shop responses expose its protected `drinkPhotoUrl`. See [the complete check-in examples](check-ins-examples.md#transparent-drink-cutout) for response behavior and [the home contract](home-check-ins-client.md#photos) for home responses.

## Storage and access

Community owns check-in photo records in `CheckInPhotos`; the coffee-shop profile gallery uses separate `ShopPhotos`. A check-in's photo ID, Media binding and protected HTTP URL remain stable across this storage split. Media claims uploaded bytes into private immutable storage and confirms them for single use.

Use `photos[].url` from authorized shop check-in reads and `drinkPhotoUrl` for the cutout. `/api/v1/check-ins/{id}/photos/{photoId}` checks the current author or approved-public visibility on every request, including for home entries. Private or hidden content cannot be read through that route by other users. Never build a public MinIO URL from `storageKey`.

The old review upload action and unversioned check-in routes were removed. Use `/api/Photos/check-in` and `/api/v1/check-ins` for coffee-shop visits, or `/api/v1/check-ins/home` for home brews.

Coffee-shelf bag cards use their own purpose, `POST /api/Photos/shelf-bag` (exactly one JPEG, PNG or WebP of at most 1 MiB, private `shelf-bags/` keys). A check-in cannot claim a shelf-bag upload; see [the shelf contract](shelf-client.md#card-image).
