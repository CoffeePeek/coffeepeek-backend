# Изолированный fault gate Shops/Media

Все три обязательных родительских сценария прошли без skips в
[CI fd8604bd](https://github.com/CoffeePeek/coffeepeek-backend/actions/runs/37909817024).
В том же run прошли 40 direct-write сценариев, полный backend regression,
vulnerability audit и legacy PostgreSQL/MinIO rehearsal.

`ShopsFaultRecoveryLiveTests` входит в обязательный PostgreSQL CI gate: каждый класс
запускается отдельно, TRX должен содержать положительное число тестов, только passed
и ноль skipped. Fault worker не является самостоятельной интеграционной проверкой:
его запускает родительский тест отдельным `dotnet` процессом и завершает через
`Process.Kill` после наблюдаемой контрольной точки. Его обычный offline skip не
подменяет выполнение трёх обязательных родительских сценариев.

Контур использует PostgreSQL 17, RabbitMQ 4.1, Redis 7, MinIO и
[Toxiproxy 2.12](https://github.com/Shopify/toxiproxy). Proxy отключает существующее
TCP-соединение; тест не подменяет исключение Redis/RabbitMQ. Родитель читает бизнес
данные через новый DbContext и наблюдает очереди и контрольные точки в файлах.
Polling ограничен 45 секундами; произвольных задержек перед kill нет.

| Сценарий | Проверка |
| --- | --- |
| Redis недоступен после commit профиля | Успешный ответ и одна ревизия в PG; настоящий Redis сохраняет старый ключ; production counter `shops.cache_invalidation.failures` зарегистрировал сбой; durable inbox сохраняет работу. Kill/restart и восстановление TCP приводят к удалению ключа и завершению работы без второго PUT. |
| Kill после фактического Redis DELETE до возврата consumer | Необработанный durable envelope восстанавливается новым процессом; повтор DELETE безопасен, профиль остаётся с одной ревизией. |
| Потерянный ответ Media, отказ RabbitMQ после commit, kill Media после PG commit до ack и новый envelope повтор | Production Media handler реально резервирует объект в PG/MinIO; потерю ответа моделирует lookup adapter. До retry в Shops нет обжарщика; retry использует прежний ID назначения и Media photo ID. После отказа TCP бизнес-запись сохранена вместе с outgoing envelope. Новый sender доставляет его; Media подтверждает фото и завершает commit до принудительного kill. Новый receiver завершает inbox, повторное бизнес-событие не добавляет фото/обжарщика. |

Claim/finalization использует настоящие `PhotoRepository`, `ClaimShopPhotosHandler`,
`ShopPhotosAttachedHandler`, `UnitOfWork<MediaDbContext>` и `MinIOStorageService`.
Claim lookup вызывает server handler непосредственно: HTTP/auth/timeout mapping
проверяются существующими контрактными тестами, а этот gate доказывает сохранение
резервации и реальную broker delivery. Redis consumer и профильный writer также
production. Runtime compilation применяется только к тестовой composition; release
generated adapters проверяются полным backend/host regression.

Запуск допустим только на изолированных контейнерах: loopback endpoints,
`COFFEEPEEK_LIVE_PG` с maintenance DB `postgres` и pgpass, существующие guards
`cp_live_*`, `COFFEEPEEK_LIVE_FAULTS=1`, `COFFEEPEEK_LIVE_MINIO` и два proxy
`shops-redis`/`shops-rabbit` из CI. Disposable MinIO credentials совпадают с legacy
gate; данные каждого теста находятся в собственном `cp-fault-*` bucket. Штатный
finally завершает только дочерние процессы, восстанавливает proxy, удаляет собственные
объекты/БД и сохраняет worker logs в CI artifact. Контур не подключается к deployment.

Gate пока не доказывает потерю PG wire commit acknowledgement, рестарт во время
платного OCR вызова, все import races и production SLO. Это отдельные пункты P31/P32;
их нельзя закрыть результатом этих трёх сценариев. Приёмку этих тестов фиксировать
только после фактического CI выполнения, а не успешной offline сборки.
