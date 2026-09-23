---
spike: 001
name: osm-overpass-minsk-cafes
type: standard
validates: "Given Minsk bbox, when Overpass is queried for cafe/coffee/vending, then we get a usable candidate set with measurable noise"
verdict: VALIDATED
related: []
tags: [osm, overpass, import, minsk]
---

# Spike 001: OSM Overpass Minsk cafes

## What This Validates

Given a Minsk bounding box, when we query Overpass for `amenity=cafe`, `shop=coffee`, and coffee vending machines, then we get a candidate list large enough to seed an admin queue, and we can measure how much of it is noise.

## Research

Docs: Overpass QL area/bbox + `out center tags` ([wiki](https://wiki.openstreetmap.org/wiki/Overpass_API/Overpass_QL)), OSM tags `amenity=cafe` (broad sit-down drinks), `shop=coffee` (retail beans), `vending=coffee` (machines).

| Approach | Tool | Pros | Cons | Status |
|----------|------|------|------|--------|
| Overpass HTTP | overpass-api.de | Official OSM extract, no ToS storage ban | Rate limits, cafe tag is broad | **Chosen** |
| Nominatim search | search API | Simple text query | Not for bulk POI dump | Skipped |
| Yandex Places | Organization Search | Rich cards | Cannot persist on basic license | Out of scope |

**Chosen:** Overpass bbox `(53.824, 27.389, 53.974, 27.761)` via POST `data=`. Area-by-name skipped after bbox returned a full city-scale set.

## How to Run

```bash
# refresh from Overpass (or reuse cache)
python3 .planning/spikes/001-osm-overpass-minsk-cafes/fetch.py
python3 .planning/spikes/001-osm-overpass-minsk-cafes/fetch.py --reuse-raw

# review queue UI
python3 .planning/spikes/001-osm-overpass-minsk-cafes/serve.py
# open http://127.0.0.1:8765/
```

## What to Expect

- Console: `total` ≈ 1500+, counts split into buckets.
- UI: cards with Yes / No / Later, OSM map, export of decisions.

## Observability

Browser event log (load / yes / no / skip / export) is shown in the sidebar and included in the exported JSON.

## Investigation Trail

1. First Overpass pull: **1576** objects, all with coordinates. 1466 `amenity=cafe`, 96 vending, 14 `shop=coffee`.
2. Naive name regex for specialty hit **2** places (`Kitchen Coffee Roasters`, `Marks Coffee Roasters`). OSM does not tag specialty.
3. `cuisine=coffee_shop` ≈ 324 — useful priority signal, still mixed with chains.
4. Name scan: 87 «столов*» (canteens), Varka ≈ 96 points. `amenity=cafe` includes canteens, pizza, sushi, Lido.
5. Bug: regex `presso` auto-rejected `Espresso Coffe` / `7/25EspressoBar`. Fixed.
6. Refined buckets: specialty 2 · priority 380 · review 818 · noise 280 · vending 96.
7. Address present on 528/1576 — formatted address is optional; coords are enough to start.
8. `ModerationShop` requires `UserId` and models owner submissions — import needs a separate `ShopImportCandidate`.
9. Binary yes/no was wrong: good non-specialty shops still belong in the feed. Queue now labels Kind: Specialty / GoodCoffee / Cafe / ToGo / Reject.
10. Location is not enough to judge coffee attention. OSM has **1 image** and Instagram on **193/1576** (76 in priority). Queue now exposes Instagram / Yandex org photos / Yandex images / website og:image preview. Do not review all 1500 — start with priority (~380).
11. Overpass `out meta`: last OSM edit timestamp. **162 / 1581** objects untouched >5 years dropped from the queue (stale / likely closed or wrong). Card shows last edit + history link.
12. Live Yandex Places check on the card (name+coords → open/closed). Key stays in gitignored `secrets.local.json`. Current key returns **403 Limit is exceeded** — org-search tile is blocked, no quota. We do not persist Yandex org payloads into the catalog.
13. Google Places API (New) Text Search works. Coffe Joy @ OSM coords is **OPERATIONAL** in Google (Yandex said closed — sources disagree). Status shown on the card; payloads cached locally, not committed.

## Results

**VALIDATED** — OSM is a legal, sufficient source for a Minsk candidate queue. It is **not** a specialty catalog.

- Do not dump all 1576 into production shops.
- First human pass: `likely_specialty` + `priority` (~380), not the generic cafe bucket.
- Autofilter can start from vending / canteen / to-go chains; specialty still needs a human (or later labeled model).
- Yandex Geocoder is optional (address text), not required for the first queue.

See `queue-model.md` for the draft aggregate.
