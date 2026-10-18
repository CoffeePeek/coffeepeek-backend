# Активация Android-релиза

Загрузка APK и регистрация его метаданных не выбирают активный релиз API. После успешной публикации APK и Play-артефакта workflow явно активирует зарегистрированный релиз:

```http
POST /api/admin/v1/app-downloads/android/releases/{id:guid}/publish
Authorization: Bearer <existing app release automation token>
```

Тело запроса не требуется. `id` берётся из записи, чьи версия, versionCode, immutable fileUrl, fileSize и SHA-256 совпадают с артефактом этого запуска. Перед вызовом оператор должен подтвердить успешные `publish_apk` и `publish_play` одного release-run. Gateway проверяет маршрут и полномочия; проверка результатов CI остаётся обязанностью release-workflow.

Маршрут использует существующий `AppReleaseAutomationPolicy`, actor и token без изменения схемы аутентификации. Обычный JWT Admin также сохраняет доступ. Automation-доступ ограничен следующими точными запросами:

| Метод | Путь относительно `/api/admin/v1/app-downloads` |
| --- | --- |
| GET, POST | `/android/releases` |
| POST | `/android/releases/{id:guid}/publish` |
| GET | корень настроек |
| PUT | `/android/version` |

GET, HEAD, PUT, PATCH и DELETE пути `publish`, POST с не-GUID идентификатором или дополнительным сегментом и остальные admin-записи не получают automation-доступа. Общий admin-маршрут продолжает требовать JWT Admin. Клиентские `X-User-*` и `X-Gateway-Auth` удаляются и формируются Gateway из проверенной identity.

После успешного ответа требуется прочитать `GET /api/admin/v1/app-downloads/android/releases` и проверить ровно один активный релиз с теми же ID и метаданными. При потере ответа сначала выполнить чтение состояния; не выбирать другой релиз и не повторять публикацию без проверки результата.

Регрессии `AppReleaseAutomationGatewayHttpTests` поднимают настоящие Kestrel и YARP с маршрутами из production `appsettings.json`, существующими JWT/automation policies и trusted-header authentication локального downstream. Они проверяют успешный POST, actor и защиту от подмены заголовков, отказ анонимному/неверному токену/User JWT/истёкшему и отозванному Admin JWT, границы методов/пути и сохранение JWT Admin. Изменение не затрагивает бизнес-обработчик публикации, фотофлаги или права остальных admin-маршрутов.
