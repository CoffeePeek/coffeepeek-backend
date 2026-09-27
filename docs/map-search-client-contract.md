# Map search client contract

This contract describes the public map endpoint introduced by commit `33b600e7`.

## Endpoint

```http
GET /api/Map?minLat={south}&minLon={west}&maxLat={north}&maxLon={east}&zoom={zoom}
```

Authentication is not required.

### Query parameters

| Parameter | Type | Required | Constraints | Meaning |
|---|---:|---:|---:|---|
| `minLat` | decimal | yes | `-90..90` | Southern viewport boundary |
| `minLon` | decimal | yes | `-180..180` | Western viewport boundary |
| `maxLat` | decimal | yes | `-90..90` | Northern viewport boundary |
| `maxLon` | decimal | yes | `-180..180` | Eastern viewport boundary |
| `zoom` | integer | no | `0..22` | Current map zoom |

`minLat <= maxLat` and `minLon <= maxLon` are required. Bounds are inclusive.

The endpoint does not accept a bounding box crossing the antimeridian. Such a
viewport must be split into two requests: `[-180, east]` and `[west, 180]`.

Example:

```http
GET /api/Map?minLat=53.80&minLon=27.40&maxLat=54.00&maxLon=27.75&zoom=12
```

## Success envelope

```ts
export interface ApiResponse<T> {
  isSuccess: boolean;
  message: string;
  data: T;
  statusCode: number | null;
}

export type CoffeeShopType = "Specialty" | "CoffeeBar" | "Cafe";

export interface MapSearchData {
  shops: MapShop[];

  // Omitted only by the legacy request without zoom.
  clusters?: MapCluster[];
  zones?: MapCoffeeZone[];
  isTruncated?: boolean;
}

export interface MapShop {
  id: string; // UUID
  latitude: number;
  longitude: number;
  title: string;
  type: CoffeeShopType | null;

  // Present only when the shop belongs to a published primary zone.
  primaryZoneId?: string; // UUID
}

export interface MapCluster {
  id: string; // Opaque and request-dependent; do not persist it.
  latitude: number;
  longitude: number;
  count: number;
  bounds: MapBounds;
}

export interface MapBounds {
  minLatitude: number;
  minLongitude: number;
  maxLatitude: number;
  maxLongitude: number;
}

export interface MapCoffeeZone {
  id: string; // UUID
  name: string;
  description: string | null;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  shopCount: number;
}
```

## Rendering rules

The response is discriminated by its contents rather than by hard-coded zoom
thresholds. Server thresholds are configuration and may change independently of
the client.

1. Render every item in `data.clusters ?? []` as a cluster marker. On tap, fit
   the map to `cluster.bounds`; if the bounds collapse to a single point, zoom in
   by one level instead.
2. Render every item in `data.zones ?? []` as a named circular zone using
   `latitude`, `longitude`, and `radiusMeters`. `shopCount` is the total number of
   shops assigned to that zone, not only the number currently visible onscreen.
3. Render every item in `data.shops` as an individual shop marker. Use
   `primaryZoneId` only for visual grouping; navigation to shop details continues
   to use `id`.
4. If `isTruncated === true`, request a smaller viewport or a higher zoom. Do not
   assume that the first 500 items represent the entire viewport.

With the current server configuration the usual output is:

| Zoom | `shops` | `clusters` | `zones` |
|---:|---|---|---|
| `0..10` | empty | populated | empty |
| `11..13` | empty | shops outside zones | published zones |
| `14..22` | populated | empty | empty |
| omitted | legacy flat markers, maximum 500 | omitted | omitted |

Clients should send `zoom`. Omitting it exists only for backward compatibility
and does not provide the `isTruncated` signal.

## Response examples

Cluster response:

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "shops": [],
    "clusters": [
      {
        "id": "cluster-value-is-opaque",
        "latitude": 53.9021,
        "longitude": 27.5618,
        "count": 18,
        "bounds": {
          "minLatitude": 53.875,
          "minLongitude": 27.51,
          "maxLatitude": 53.93,
          "maxLongitude": 27.62
        }
      }
    ],
    "zones": [],
    "isTruncated": false
  },
  "statusCode": null
}
```

Zone response:

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "shops": [],
    "clusters": [],
    "zones": [
      {
        "id": "5f781b6b-dc42-46c6-b1f8-8968bbb96f31",
        "name": "Октябрьская",
        "description": null,
        "latitude": 53.887,
        "longitude": 27.574,
        "radiusMeters": 900,
        "shopCount": 12
      }
    ],
    "isTruncated": false
  },
  "statusCode": null
}
```

Individual marker response:

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "shops": [
      {
        "id": "165e7de8-612d-4164-bc7f-4ed237ca4498",
        "latitude": 53.8872,
        "longitude": 27.5731,
        "title": "Coffee Shop",
        "type": "Specialty",
        "primaryZoneId": "5f781b6b-dc42-46c6-b1f8-8968bbb96f31"
      }
    ],
    "clusters": [],
    "zones": [],
    "isTruncated": false
  },
  "statusCode": null
}
```

## Errors

Invalid numeric ranges or a missing required parameter return HTTP `400`:

```ts
export interface ApiError {
  isSuccess: false;
  message: string;
  data: null;
  statusCode: number | null;
  errorCode?: string | null;
  errors?: Record<string, string[]> | null;
}
```

Clients must use the HTTP status as the primary error signal and should display
`message` only as a fallback. Field validation details, when present, are in
`errors`.

## Minimal client request

```ts
export async function getMapSearch(
  apiBaseUrl: string,
  bounds: MapBounds,
  zoom: number,
  signal?: AbortSignal,
): Promise<MapSearchData> {
  const query = new URLSearchParams({
    minLat: String(bounds.minLatitude),
    minLon: String(bounds.minLongitude),
    maxLat: String(bounds.maxLatitude),
    maxLon: String(bounds.maxLongitude),
    zoom: String(Math.max(0, Math.min(22, Math.round(zoom)))),
  });

  const response = await fetch(`${apiBaseUrl}/api/Map?${query}`, { signal });
  const body = (await response.json()) as ApiResponse<MapSearchData> | ApiError;

  if (!response.ok || !body.isSuccess) {
    throw new Error(body.message || `Map request failed: ${response.status}`);
  }

  return body.data;
}
```

Map movement should debounce this request and abort the previous in-flight
request so that an older viewport response cannot overwrite a newer one.
