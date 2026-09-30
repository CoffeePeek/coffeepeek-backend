# Map search client contract

The current API uses public addresses for shops and coffee zones. See the [public identity contract](api/public-addresses-client.md) for the shared address type and deployment dependency.

```http
GET /api/Map?minLat={south}&minLon={west}&maxLat={north}&maxLon={east}&zoom={zoom}
```

Authentication is not required. Latitude is -90..90, longitude -180..180, optional integer zoom 0..22. Bounds are inclusive; minimum must be no greater than maximum. Split an antimeridian-crossing viewport into two requests.

## Response

```ts
type PublicAddress = { slug: string; canonicalPath: string; revision: number; isAlias: boolean };
type MapShop = {
  address: PublicAddress;
  latitude: number;
  longitude: number;
  title: string;
  type: "Specialty" | "CoffeeBar" | "Cafe" | null;
  primaryZone: PublicAddress | null;
};
type MapCluster = {
  id: string; // Opaque, request-dependent cluster key. Do not persist it.
  latitude: number;
  longitude: number;
  count: number;
  bounds: { minLatitude: number; minLongitude: number; maxLatitude: number; maxLongitude: number };
};
type MapZone = {
  address: PublicAddress;
  name: string;
  description: string | null;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  shopCount: number;
  polygon: { latitude: number; longitude: number }[];
};
type MapData = {
  shops: MapShop[];
  clusters: MapCluster[];
  zones: MapZone[];
  isTruncated: boolean;
};
// Envelope: { isSuccess: boolean, message: string | null, data: MapData }
```

All arrays and `isTruncated` are present, including when zoom is omitted. There is no shop `id`, zone `id` or `primaryZoneId`. Navigate using `address.canonicalPath`; API detail requests use `address.slug`. No additional address lookup is needed.

## Rendering

Render the returned contents; thresholds are server configuration and can change independently of the client.

1. Render clusters; on tap fit to `bounds`, or zoom in one level if the bounds collapse to a point.
2. Render zones using `polygon`. Latitude/longitude/radius are the polygon's centroid and bounding circle. `shopCount` counts shops assigned to the zone, not only shops in the viewport.
3. Render shop markers and use `primaryZone` for grouping. It can be null.
4. When `isTruncated` is true, request a smaller viewport or higher zoom.

The current usual output is clusters at zoom 0–10, published zones plus clusters outside them at 11–13, and shop markers at 14–22. Omitted zoom uses the existing flat marker query capped at 500; clients should send zoom to obtain the query's truncation signal. The public envelope supplies false when the internal flat query has no truncation metadata.

```json
{
  "isSuccess": true,
  "data": {
    "shops": [{
      "address": {"slug":"26-october-16","canonicalPath":"/coffee-shops/26-october-16","revision":1,"isAlias":false},
      "latitude":53.9,"longitude":27.56,"title":"26","type":"Specialty","primaryZone":null
    }],
    "clusters":[],"zones":[],"isTruncated":false
  }
}
```

Invalid bounds/zoom returns 400; disabled addresses or a required marker address missing before backfill returns 503. Public responses use `Cache-Control: no-store`.
