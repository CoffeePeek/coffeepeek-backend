# Контролируемый запуск доставки фотографий

Операции выполняются workflow **Photo production operations** из проверенного commit. Для ручного
запуска выбрать `operation`; после deployment обязательно указать `expected_revision` — полный Git SHA
работающих образов, а не SHA feature-ветки перед merge. Push operator workflow выполняет только
read-only preflight. Запуск архива, изменение флагов и создание canary требуют отдельных явных dispatch.

## До deployment

1. `preflight`: проверить health, schema/migration history, disk headroom и существующий том MinIO.
   `storageTopology` должен подтверждать `coffeepeek_minio_data`, принадлежащий работающему MinIO.
   Отсутствие сервиса в выводе Compose не доказывает отсутствие данных.
2. `storage`: подписанные GET трёх bucket policies. Raw policies остаются на VPS; отчёт содержит
   только агрегаты и хеши. Отсутствующий bucket вариантов допустим до provisioning. Существующий
   private-original bucket не должен разрешать анонимный доступ. Для исторической gallery policy
   с denies проверить `checkInPrivateMarker`: provision сохраняет такую политику.
3. `backup`: сохранить четыре PostgreSQL custom archives, конфигурацию и rollback manifest;
   закрепить пять работающих image IDs отдельными локальными тегами. `complete=true` требуется
   отдельно от `privateBucketsReady`: существование резервной копии не доказывает готовность storage.

Все приватные данные находятся вне синхронизируемого checkout:
`/var/lib/coffeepeek/photo-backfill`, каталоги 0700, файлы 0600. Не копировать `.env`, SQL dumps,
raw policies, SQLite checkpoint, canary credentials или confirmation tokens в CI artifacts.
Workflow публикует только санитаризованный JSON. Базы копируются отдельными согласованными снимками;
это не единый распределённый snapshot. Проверка `pg_restore --list` подтверждает читаемость TOC,
но не считается тестовым восстановлением. Восстановление автоматически не выполняется.

## Deployment

Весь stacked backend выпуск может быть включён одним merge, сохраняющим его commits. Каждый push
`main` автоматически запускает CI, сборку пяти образов и VPS deployment, поэтому последовательные
merge всех промежуточных PR создадут повторные deployment.

CI публикует `sha-<полный SHA>` вместе с OCI revision. `ci-deploy.sh` подготавливает app images,
локально закреплённый SDK, provisioning image, renderer, EF tool и четыре Release startup assemblies
до остановки writers. Apply использует тот же SDK image ID, checkout и cache volumes с `--no-build`.
Ошибки registry/NuGet/build должны завершаться до остановки writers. После ошибки самой миграции
writers остаются остановленными; оператор завершает forward migration и повторяет штатный startup.

Первый deployment оставляет rollout flags выключенными. Проверить пять image revisions, health,
новые migrations, dedicated reader/writer identities и приватность двух buckets после provisioning.
Не использовать `SKIP_MIGRATIONS=1` для первого выпуска вариантов.

## Инвентаризация и canary

1. `inventory`: не более 20 страниц по 5 кандидатов за dispatch, с проверкой original content.
   Не завершённый checkpoint продолжается отдельным dispatch. SQLite фиксирует cutoff и deployment;
   смена IP контейнера не меняет логическую идентичность checkpoint.
2. `evidence-before`: приватные fingerprints существующих Community business rows, очереди и
   снимок ресурсов renderer/Media/Gateway. Существующий baseline не перезаписывается.
3. Последовательно `canary-prepare`, `canary-activate`, `canary-create`. Подтверждение email имеет
   ограниченный срок; между этими операциями не ставить долгие проверки. Это отдельный reserved-domain
   аккаунт, два synthetic PNG и один private home check-in с drink cutout.
4. Activation проверяет точные revisions/IMAGE_TAG и миграции, затем включает generation/read/serve и
   advertisement только одному canary viewer. Public freshness cache и cleanup остаются выключенными;
   cleanup dry-run остаётся включённым. Пересоздаются только Shops и Media.

Инвентаризация не подтверждает Ready coverage по одному `existingCount`: отдельно сверять актуальный
Ready manifest, binding, source и Community projection. `needs_transfer`, повреждения, provenance и
metadata mismatches расследуются отдельно, без автоматической смены originals или business metadata.

## Пять архивных фотографий

1. `pilot-create` создаёт paused run с batch 5 / interval 5 seconds.
2. `pilot-advance` явно возобновляет run и продвигает ровно одну страницу. Повторный dispatch означает
   ещё одну разрешённую страницу; автоматического цикла нет.
3. `pilot-status` читает durable counters и statuses. Дождаться Ready всей поставленной партии и
   доставки Community projections, затем `pilot-pause`.
4. `canary-verify` проверяет два новых private originals, alpha, форматы, exact bytes/ETags,
   GET/HEAD/conditional access, анонимный отказ и старую public Ready фотографию. Для обычного viewer
   descriptors по-прежнему отсутствуют; allowlisted viewer получает верные versioned descriptors.
5. `evidence-after` сравнивает business fingerprints и показывает pilot bytes, queue-to-Ready latency,
   очереди по приоритету и ресурсы. Изменения business rows могут быть реальным пользовательским
   трафиком и требуют расследования. CPU — отдельный instantaneous sample; latency включает очередь
   и retries, а declared source bytes нужно сопоставлять с verified inventory.

При неизвестном исходе mutation сначала читать status/private journal. Не удалять attempt marker,
не пересоздавать run и не повторять writes автоматически. Pause разрешён после неизвестного advance;
уже начатая попытка не прерывается. Новые uploads имеют priority 0, архив — priority 1.

## Клиенты и расширение аудитории

Web canary проверяется на same-site `coffeepeek.by` после backend gate: лента, viewer, explicit original,
private home/drink photo, logout/account switch, expired credentials и offline deadline. Preview на
`*.vercel.app` не подтверждает refresh-cookie flow с `api.coffeepeek.by` и `SameSite=Strict`.
Для mobile требуется настоящий iOS compile job; зелёный wrapper при skipped compile доказательством
не является. Публикация клиентских сборок выполняется выбранным release workflow отдельно.

Массовый rollout следует плану 5% → 25% → 100%, с минимум 24 часами между расширениями, проверками
privacy/error/latency и свежей Ready coverage недавних eligible фотографий не ниже 99%. Один
пятифотографический пилот не подтверждает эти условия. Временные canary данные удаляются отдельной
операцией `canary-cleanup` только после завершённой проверки; аккаунт остаётся для операторского аудита.

Для мягкого отката pause архива и выключение generation/advertisement сохраняют read/serve уже выданных
versioned URLs. Originals, Ready artifacts, manifests и checkpoints ради отката не удаляются.
