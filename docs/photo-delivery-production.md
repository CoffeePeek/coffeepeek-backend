# Production-доставка фотографий

После пилота владелец разрешил полный запуск для всех пользователей без этапов 5% / 25% и ожидания
между ними. Ограничение advertisement на отдельные аккаунты удалено из приложения и Compose.
`CHECKIN_VARIANTS_ADVERTISE_ENABLED=true` выдаёт готовые варианты каждому читателю с обычной проверкой
доступа. Для приватных фото продолжают действовать owner/visibility/block/delete checks.

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

## Выполненный пилот

1. `inventory`: не более 20 страниц по 5 кандидатов за dispatch, с проверкой original content.
   Не завершённый checkpoint продолжается отдельным dispatch. SQLite фиксирует cutoff и deployment;
   смена IP контейнера не меняет логическую идентичность checkpoint.
2. `evidence-before`: приватные fingerprints существующих Community business rows, очереди и
   снимок ресурсов renderer/Media/Gateway. Существующий baseline не перезаписывается.
3. Исторический пилот использовал reserved-domain аккаунт, два synthetic PNG и один private home
   check-in с drink cutout. До полного запуска успешно проверены 72 HTTPS checks и 38 отказов после
   удаления тестового check-in. Аккаунт сохранён для аудита. Повторное создание и удаление не требуются.
4. Старый `canary-activate` исключён из production workflow: режима с отдельным списком аккаунтов
   больше нет. Приватные журналы пилота сохраняются как доказательства ранее выполненных операций.

Инвентаризация не подтверждает Ready coverage по одному `existingCount`: отдельно сверять актуальный
Ready manifest, binding, source и Community projection. `needs_transfer`, повреждения, provenance и
metadata mismatches расследуются отдельно, без автоматической смены originals или business metadata.

## Исторический пилот пяти архивных фотографий

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

## Полный запуск и завершение архива

Операции `rollout-*` принимают точный `expected_revision` работающих образов. Они используют новый
приватный каталог `full-rollout-<revision>`, сохраняя исходные inventory/pilot/canary/evidence без
перезаписи. Смена Git SHA не считается разрешением забыть уже выполненную запись.

1. `rollout-enable` проверяет пять OCI revisions, IMAGE_TAG и migrations, сохраняет generation/read/
   serve/advertise=true, удаляет устаревшие canary assignments и пересоздаёт только Shops/Media.
   Actual runtime flags и health проверяются после запуска. Bounded private freshness для публичных
   вариантов применяется постоянно без настройки кеша; cleanup остаётся выключенным, dry-run включён.
   Public GET/HEAD/304 проверяются с включённым кешем; owner-only/private originals остаются no-store.
2. `rollout-inventory` создаёт свежий checkpoint исходного cohort и проверяет original content.
   До adoption cutoff совпадает с исходным pilot run; его immutable inventory не перезаписывается.
3. `rollout-adopt` принимает существующий paused pilot run только при совпадении его параметров,
   recipe и durable состояния и отсутствии незавершённых writes. Новый журнал сохраняет хеш старого.
   Исходный run продолжается; новый конкурирующий архивный run не создаётся.
4. `rollout-advance` продвигает ограниченное число страниц; `rollout-status` проверяет durable status.
   Продолжать до ScanComplete и завершения jobs, сохраняя приоритет новых uploads. При неизвестном
   ответе write сначала status/private-state review; повтор записи автоматически не выполняется.
5. Последовательно `rollout-smoke-create`, `rollout-smoke-verify`, `rollout-smoke-cleanup` создают
   отдельный reserved-domain аккаунт и private home с двумя новыми фото уже на текущем release.
   Проверяются generation, alpha, protected GET/HEAD, owner descriptors и отказ после удаления.
   Create/cleanup не повторяются после неизвестного результата; старые canary данные не меняются.
6. Повторный `rollout-inventory` создаёт новый checkpoint с текущим cutoff и проверяет original
   content и актуальную Ready coverage. Завершённая старая inventory не используется как свежий снимок.
7. `rollout-verify` сверяет Media и Community manifests с актуальными bindings, проверяет публичные
   descriptors и реальные HTTPS ответы, а также отказ доступа к удалённым synthetic фото. После
   завершения `rollout-pause` оставляет законченный архивный run остановленным.

Rollback generation/advertisement остаётся общей операционной настройкой. Выданные versioned URLs
продолжают проходить защищённый read path; originals и checkpoints ради отката не удаляются.

## Клиенты

Web canary проверяется на same-site `coffeepeek.by` после backend gate: лента, viewer, explicit original,
private home/drink photo, logout/account switch, expired credentials и offline deadline. Preview на
`*.vercel.app` не подтверждает refresh-cookie flow с `api.coffeepeek.by` и `SameSite=Strict`.
Для mobile требуется настоящий iOS compile job; зелёный wrapper при skipped compile доказательством
не является. Публикация клиентских сборок выполняется выбранным release workflow отдельно.

Процентного audience gate в приложении больше нет. Фактическую готовность архива подтверждают свежая
инвентаризация и Ready projections; счётчик `existingCount` сам по себе этого не доказывает.

Для мягкого отката pause архива и выключение generation/advertisement сохраняют read/serve уже выданных
versioned URLs. Originals, Ready artifacts, manifests и checkpoints ради отката не удаляются.

Кеш публичных вариантов работает постоянно: private freshness не более 20 секунд,
с учётом исходной проверки доступа и срока JWT. Отдельного флага в приложении, Compose и rollout нет;
устаревшая настройка в production `.env` не может отключить кеш после обновления сервиса.
Private/home-only/original фотографии остаются no-store. Ни Gateway, ни Caddy,
ни Media не получают shared-cache разрешение на защищённые фотографии.
