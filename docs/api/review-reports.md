# Жалобы на публичные check-in

Канонические check-in заменили отдельные отзывы. Клиентские операции с check-in описаны в [контракте v1](check-ins.md). Все запросы ниже идут через Gateway с `Authorization: Bearer <token>`.

## Отправить жалобу

```http
POST /api/v1/check-ins/{id}/reports
Content-Type: application/json

{ "text": "В публикации оскорбления" }
```

Отправлять может авторизованный пользователь. Целью может быть только текущий одобренный публичный check-in; недоступная цель возвращает 404. Текст обязателен, максимальная длина — 2000 символов. Успех возвращает `201` и стандартный `Response<ReviewReportDto>` с ID жалобы в `data.id`.

## Очередь администратора

Эти маршруты требуют роль **Admin**:

| Метод | Маршрут | Результат |
| --- | --- | --- |
| `GET` | `/api/admin/check-in-reports?status=Pending&page=1&pageSize=20` | `data: { items, totalCount, page, pageSize }` |
| `GET` | `/api/admin/check-in-reports/{reportId}` | `data: { report, checkIn }`; `checkIn` может быть `null`, если историческая цель отсутствует |
| `PUT` | `/api/admin/check-in-reports/{reportId}/resolution` | Закрыть жалобу или скрыть check-in |

Без `status` список показывает `Pending`. Статусы: `Pending`, `Dismissed`, `CheckInDeleted`. Детали жалобы содержат `id`, `checkInId`, `reportedByUserId`, `text`, `status`, `createdAtUtc`, `resolvedByAdminId`, `resolvedAtUtc`. У текущего check-in в административной детали есть текст, оценки, даты и признак публичной видимости.

Решение передаётся явно:

```http
PUT /api/admin/check-in-reports/{reportId}/resolution
Content-Type: application/json

{ "deleteCheckIn": true }
```

`true` скрывает check-in и закрывает жалобу как `CheckInDeleted`; `false` закрывает её как `Dismissed`. Отсутствующий или `null` `deleteCheckIn` даёт 400. Повтор того же решения идемпотентен; попытка изменить уже принятое решение даёт конфликт. Жалобы сохраняются как административный аудит, включая те, чья историческая цель больше не существует.
