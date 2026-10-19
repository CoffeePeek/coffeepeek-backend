# Ручное меню: цена и наличие

С 19 октября 2026 объём напитка удалён по решению владельца продукта. `Present` требует положительную цену; `Absent`/`Unknown` не содержат цену. Запрос проверяется целиком до записи: пустые/null элементы, неизвестный или повторённый напиток, неизвестный enum и цена вне `numeric(10,2)` возвращают 400. Moderation, заявки на изменение и Shops используют одну проверку значений. Старые JSON-сообщения с `volumeMl` читаются без него.

Меню сохраняет одну позицию на напиток. SQL upsert и повышение `InputRevision` идут в одной транзакции; конфликт возвращает 409. После upsert перечитывается EF tracking. Ручная правка инвалидирует более старый OCR. OCR без положительной цены пропускается, объём не запрашивается и не влияет на распознавание.

Миграция `RemoveDrinkVolumeFromMenus` заменяет `CK_ShopMenuItems_Values` и удаляет столбец объёма. Она не меняет ID, цены, наличие и фотографии. Перед выпуском остановить старых Shops/Moderation writers и сохранить backup; rollback требует восстановления удалённых значений из backup. Исторические миграции сохранены.

Проверки: `MenuHttpValidationTests`, `MenuDraftTests`, `ShopMenuItemValidationTests`, `ShopMenuManualWriteLiveTests`, `ShopChangeRequestMenuLiveTests`, `MenuOcrJobLiveTests`, `ShopsSchemaUpgradeLiveTests`. HTTP проверяет запрос без объёма, старое лишнее поле и ошибочные пакеты; live PostgreSQL проверяет SQL, конкуренцию и миграции.
