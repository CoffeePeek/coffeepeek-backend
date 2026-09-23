---
id: SEED-001
status: ready
planted: 2026-08-15
planted_during: tech-debt / post Tier-0 catalog bootstrap
trigger_when: when starting catalog content bootstrap or a "shops data / import" milestone
scope: medium — Moderation admin queue + OSM collector + approve→Shops path
---

# SEED-001: Admin import queue + OSM collector

## Why This Matters

Without coffee shops in the DB, CoffeePeek is empty. We need a controlled bootstrap: broad candidate collection (OSM + lists) → admin queue with a Kind category → all approved shops go to the feed (not only specialty). A manual pass on a small sample first, then an auto-filter.

## When to Surface

**Trigger:** when starting catalog content bootstrap or a "shops data / import" milestone

Surface during `/gsd:new-milestone` if the scope includes shop population, import, OSM, or admin moderation of candidates.

## Scope Estimate

**Medium** — a new (or extended) candidate entity in Moderation, admin API/UI contracts, an OSM Overpass collector, approve → create `CoffeeShop`, later scoring/auto-filter. No storage of Yandex Places org payloads.

## Breadcrumbs

- `.planning/notes/2026-08-15-specialty-coffee-shops-minsk-import.md`
- `CoffeePeek.Moderation.*` — shop moderation queue
- `CoffeePeek.Moderation.Infrastructure/External/Yandex/` — geocode only
- `CoffeePeek.Shops.Domain` — `CoffeeShop` (production after approve)
- Research: Yandex Places requires a storage license; MVP = OSM + lists

## Not in this seed

- Coffee bean import / tasting notes
- HTML scraping of Yandex Maps
- Persist Yandex Organization Search results without proper license
