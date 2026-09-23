---
date: "2026-08-15 12:03"
promoted: false
---

# Importing Minsk specialty coffee shops

Decisions from `/gsd:explore` (2026-08-15):

- **Goal:** populate the app with real specialty coffee shops; without them the product is useless.
- **Scope v1:** coffee shops (not beans). Beans and the rest come later.
- **Geography:** Minsk. Target ~200–250 specialty shops; there may be up to ~1000 candidates (noise: vending machines, "just coffee").
- **Flow:** collector → **admin queue in CoffeePeek** (Kind category, not yes/no) → all approved Kinds go to the feed.
- **Categories v1 (2026-08-15):** `Specialty` · `GoodCoffee` (good coffee, not specialty) · `Cafe` (food + decent coffee) · `ToGo` (takeaway / chain). Rejected = not in the feed. Specialty is one type, not the only ticket into the feed.
- **Queue card:** coordinates are not enough. The curator checks Instagram and Yandex photos — these links (and a website preview) must be on the card. OSM has almost no photos. Do not review all 1500: start with the ~380 that have a coffee signal.
- **OSM freshness:** if an object has not been edited for **>5 years**, drop it from the queue (in the 2026-08-15 run: 162 of 1581). The card shows the last edit date and a link to the history.
- **Selection:** first a manual review of a small sample → then an auto-filter based on the patterns found.
- **Candidate sources:** **OSM + public/manual specialty lists**. Yandex is used only for the **Geocoder** (coordinates). Do not parse Yandex HTML cards; do not use Places/Organization Search for storage without a data-storage license (the basic ToS forbids persisting org data).
- **Already in code:** Geocoder models in `CoffeePeek.Moderation.Infrastructure/External/Yandex/`; shop moderation is the natural home for the import queue.
