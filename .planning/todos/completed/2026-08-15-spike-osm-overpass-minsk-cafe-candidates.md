---
created: 2026-08-15T12:03:16+03:00
completed: 2026-08-15T12:07:00+03:00
title: Spike OSM Overpass Minsk cafe candidates
area: general
files:
  - .planning/spikes/001-osm-overpass-minsk-cafes/
---

## Problem

We need to populate the catalog of Minsk specialty coffee shops. The v1 source is OSM (+ lists), not Yandex Places. It is not yet known how many candidates Overpass yields and how noisy they are; without that, the admin queue and auto-filter cannot be designed.

## Solution

Done in spike 001 (VALIDATED). Overpass bbox of Minsk → 1576 candidates. Queue: http://127.0.0.1:8765/  Model: `queue-model.md`.
