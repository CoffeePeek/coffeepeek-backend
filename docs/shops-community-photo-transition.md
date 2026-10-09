# P18/P29: фотографии Community

`CheckInPhoto` принадлежит Community и хранится в `CheckInPhotos`. Имя навигации `CheckIn.ShopPhotos`, JSON DTO, UUID фотографии, ключ Media и защищённый HTTP URL сохранены. Галерея кофейни продолжает использовать `ShopPhoto`/`ShopPhotos`. Создание, projections, проверка использованных ключей и оба legacy backfill инструмента используют новую таблицу для canonical-посещений; Moderation сохраняет свой snapshot и прежние photo IDs.

Агрегат проверяет весь пакет до изменения: автор, единственный родитель, разные ID/ключи, максимум пять. Коллекция и metadata закрыты для публичной записи. Добавление фотографий повышает content revision и сбрасывает прежнее одобрение; запоздалый decision не публикует новую ревизию. EF не генерирует Media ID, поэтому добавление ребёнка к tracked-посещению отслеживается как `Added`.

## Переход

1. Сохранить backup и проверить актуальное состояние Media/private buckets по существующему legacy runbook. Эта миграция копирует metadata, не переносит объекты и не исправляет их права доступа.
2. Остановить и дренировать все старые Shops writers, включая queued consumers и инструменты backfill. Одновременная запись старого и нового бинарников запрещена: старый пишет `ShopPhotos`, новый — `CheckInPhotos`.
3. Применить `20261009081229_SeparateCommunityCheckInPhotos`. В одной транзакции preflight блокирует foreign owner, совместное владение с галереей, invalid metadata, повтор ключа и более пяти фотографий. Никакая из этих ошибок не исправляется автоматически. Индекс уникальности ключа и FK новой таблицы создаются вместе с копированием ID, порядка и времён. `ShopPhotos` и её nullable legacy FK сохраняются.
4. Сверить canonical ID/parent/owner/key/order/timestamps и число строк, проверить protected photo endpoint, затем запустить новый бинарник. Отдельный PostgreSQL gate репетирует upgrade старой схемы с approved и deleted посещениями, галереей и повторным idempotent SQL script.
5. Старый бинарник может читать сохранённые строки до первой новой записи. Возврат к старому writer после новых записей требует остановки, обратной сверки/копирования и явного forward fix. `Down` запрещён: удаление новой таблицы потеряло бы данные. Destructive contract migration не входит в этот PR.

Для legacy-перехода с архивами оба инструмента применяют **только** idempotent script этой новой additive migration до переноса контента. Это создаёт `CheckInPhotos`, не запускает старую contract migration и не удаляет архив. После успешного переноса штатный EF `Migrate` применяет остальные отсутствующие миграции; уже применённая photo expansion повторно не выполняется. Dry-run legacy-инструмента остаётся без записи. PostgreSQL/MinIO тест репетирует storage failure, незавершённый transfer, завершение и повторный запуск.

`CheckInPhotoMigrationLiveTests` дополнительно проверяет, что каждая неоднозначность останавливает upgrade без изменения архива. `CheckInModerationRaceLiveTests` покрывает hide/delete/edit/photos против уже прочитанного approval. `CheckInLimitsLiveTests` запускает конкурирующие Application creates на реальных транзакциях: cooldown и daily limit учитывают также soft-deleted посещения; duplicate PUT/DELETE helpful меняет счётчик ровно один раз. Media/bus doubles в этих limit tests не доказывают доставку брокером — delivery проверяется отдельно.
