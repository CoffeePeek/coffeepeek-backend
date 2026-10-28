# Imported opening hours

Import publication carries optional structured `Schedules` in `ImportCandidatePublishedItem`.
Existing messages without this field remain valid. Shops writes the UTC schedule through the
shop aggregate and advances its profile revision.

`ImportOpeningHoursParser` accepts the formats retained by the Minsk import: English two- or
three-letter weekdays, Russian weekday names, weekday ranges, comma-separated intervals and
explicit `off` / `closed` / `выходной` days. Source hours are local Minsk time (UTC+3); both time
and starting weekday shift to UTC. Missing source hours remain unknown. Invalid or unsupported
nonempty text rejects publication before the candidate is changed; no partial week is published.
Full-day expressions such as `24/7` require manual review until their wire/client representation
is supported. Existing `00:00-23:59` source intervals are preserved literally.

## Restore previously published shops

Build `dev/RestoreShopSchedules/RestoreShopSchedules.csproj` and run its published DLL inside
the Shops service environment. No database migration is needed.

```text
dotnet RestoreShopSchedules.dll
dotnet RestoreShopSchedules.dll --apply
dotnet RestoreShopSchedules.dll reviewed-sources-2026-10-28.json
dotnet RestoreShopSchedules.dll reviewed-sources-2026-10-28.json --apply
```

The first command is a dry run. The tool reads source hours from the Moderation database on the
same PostgreSQL server, parses the entire plan before writing, and only selects shops with no
schedule. It preserves existing schedules, visibility, business status and all other profile
fields (existing database triggers recalculate data completeness). An optional reviewed JSON file maps exact
shop slugs to local hours, used only when the import has no hours. Missing sources are reported
without inventing hours. Every write advances
`ProfileRevision`; EF concurrency checks roll back the whole transaction if a profile changed.

Before applying, retain a database backup on the server. The tool requires the existing
`PostgresCpOptions__ConnectionString` and `RedisOptions__*` settings, verifies Redis availability,
commits the schedules, invokes the existing strict profile cache invalidation handler and checks
the restored count. A post-commit cache failure is reported as failure and requires retrying
invalidation; never revert or overwrite a subsequent user profile edit.

## Reviewed supplemental sources, 2026-10-28

These eight profiles had no retained source hours. The reviewed JSON is a one-time input;
it is never applied automatically by the import pipeline. Hours were checked against the
specific branch/address, with official branch information preferred over directories.

| Shop / branch | Local hours | Source |
| --- | --- | --- |
| Black coffee, Ложинская 4 | Daily 09:00–22:00 | [Official menu/footer](https://blackcoffee.by/menu/) |
| Gardd, Игоря Лученка 25 | Mon–Fri 08:00–22:00, weekends 09:00–22:00 | [Official site](https://gardd.by/) |
| Brioche, Татарская 2 | Daily 09:00–22:00 | [Official branch list](https://brioche.by/) |
| The Coffee Room, Петра Мстиславца 20 | Daily 08:00–22:00 | [CityDog branch list](https://citydog.io/post/gde-v-minske-breakfast/) |
| MARCONI, Тимирязева 74А | Daily 10:00–22:00 | [Branch listing](https://restaurantguru.ru/Marconi-Minsk-3), [directory](https://minsk.spravker.ru/kofejni/marconi-2.htm) |
| Eclair Delicious, Киселёва 28 | Mon–Fri 08:00–22:00, weekends 09:00–22:00 | [Branch listing](https://restaurantguru.ru/Eclair-Delicious-Minsk-2), [directory](https://minsk.spravker.ru/bulochnyie/raion-tsentralnyij/) |
| Blin garden, Кирилла Туровского 4 | Daily 10:00–23:00 | [OSM directory](https://ulitsy-belarusi.openalfa.com/%D0%9C%D0%B8%D0%BD%D1%81%D0%BA/%D0%B5%D0%B4%D0%B0-%D0%BF%D0%B8%D1%82%D1%8C%D0%B5), [branch identity/closing time](https://yandex.com/maps/157/minsk/house/Zk4YcAJkSUAFQFtpfXVycHlnZQ%3D%3D/inside/) |
| Cofee HUB, Авиационная 10, Копище | Daily 08:00–23:00 | [OSM directory](https://belarus-streets.openalfa.com/minsk-region/eating-drinking) |

The generic Eclair site lists another branch's hours; use the address-specific sources above.
Комьюнити, Некрасова 3 remains unknown: its profile is hidden/temporarily closed and external
information also reports possible permanent closure. Do not infer an operating schedule.

The production repair restored 150 schedules from retained import data and 8 from the reviewed
sources. All 11 Paragraph profiles have hours; the public API returned schedules and a known
opening state for all 19 checked Paragraph/supplemental profiles. No visible profile remained
without hours. Existing schedules and unrelated profile fields were verified unchanged.
