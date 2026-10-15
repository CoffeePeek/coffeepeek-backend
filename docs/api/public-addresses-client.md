# Публичные адреса для клиентов

Кофейни, обжарщики, города, кофейные зоны, пользователи и справочники используют slug. Публичные ответы возвращают адрес вместе с данными; дополнительный запрос `public-address` не нужен.

```ts
type PublicAddress = {
  slug: string;
  canonicalPath: string;
  revision: number;
  isAlias: boolean;
};
```

`canonicalPath` — относительный путь страницы клиента, например `/coffee-shops/26-october-16`, а не API URL. Для навигации используйте этот путь, для API запросов — `slug`. Не генерируйте slug из имени и не подставляйте GUID при ошибке. Сервер открывает только текущий slug: прежний адрес после смены возвращает 404, `isAlias` всегда `false`.

Публичные DTO не содержат GUID кофейни, города, обжарщика, зоны, пользователя или справочника. GUID check-in, фотографии и заявки модерации остаются идентификаторами операций. Административный API, кабинет владельца и внутренние сообщения используют внутренние ID.

## Чтение

| GET | Ответ |
| --- | --- |
| `/api/CoffeeShops` | `Response<{coffeeShops, totalItems, totalPages, currentPage, pageSize}>` |
| `/api/CoffeeShops/{slug}` | `Response<PublicShopDetailsDto>`; данные прямо в `data` |
| `/api/roasters` | Массив публичных обжарщиков в `data`; необязательный фильтр `tags` |
| `/api/roasters/{slug}` | `Response<PublicRoasterDetailsDto>` |
| `/api/Catalogs/cities/{slug}` | `{address, name}` без `Response` |
| `/api/Catalogs/coffee-zones/{slug}` | Опубликованная зона с `address` без `Response` |
| `/api/Users/by-slug/{slug}` | Публичный профиль и `address` |
| `/api/Users/me` | Профиль текущего пользователя, включая `data.address` |
| `/api/v1/check-ins/mine` | История автора: `Response<{items, totalCount}>`, нужна авторизация |
| `/api/Map?minLat=…&minLon=…&maxLat=…&maxLon=…&zoom=…` | Кофейни, кластеры и зоны карты |

`/api/CoffeeShops/by-slug/{slug}`, `/api/roasters/by-slug/{slug}`, `/api/Catalogs/cities/by-slug/{slug}` и `/api/Catalogs/coffee-zones/by-slug/{slug}` поддерживают те же данные, что короткие маршруты. Для пользователя используйте `by-slug`: `me` и `exists` зарезервированы как отдельные маршруты.

Сводка кофейни содержит `address`, `city`, `name`, `photos`, `rating`, `checkInCount`, признаки посещения/новизны/открытия, данные местоположения и каталоги. Деталь добавляет `description`, `checkIns`, `userCheckIns`, `schedules`, `tags`, `menu`. У check-in поля `shop` и `author` содержат публичные адреса. Текущий [контракт check-in](check-ins.md) описывает создание, чтение, фото и модерацию; отдельные маршруты отзывов удалены.

## Справочники и фильтры

`GET /api/Catalogs/cities`, `/beans`, `/equipments`, `/brew-methods`, `/shop-tags` возвращают массив непосредственно в `data`. Обжарщики доступны через `GET /api/roasters`; старый `/api/Catalogs/roasters` удалён.

`GET /api/CoffeeShops` принимает `city` и массивы slug `roasters`, `equipments`, `beans`, `brewMethods`, `tags`. Передавайте массив повторением параметра:

```http
GET /api/CoffeeShops?city=minsk&beans=arabica&tags=dog-friendly&tags=laptop-friendly&page=1&pageSize=20
```

Неизвестный фильтр возвращает 404. Для списка кофеен применяются `page` и `pageSize` (1–100). Для истории автора check-in используются `pageNumber`, `pageSize`, необязательные `from`/`to` и заголовки `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`.

## Запись и заявки

Пользователь берётся из авторизации; `userId` передавать не нужно. Для check-in, жалоб, заявок изменения кофейни и `/mine` нужен `Authorization: Bearer …`. Пример создания check-in:

```http
POST /api/v1/check-ins
Content-Type: application/json

{"coffeeShopSlug":"26-october-16","visitedAt":"2026-09-30T12:00:00Z","visibility":0,"text":"Отличный кофе"}
```

Обязательны только `coffeeShopSlug` и `visitedAt`; подробности есть в [check-ins.md](check-ins.md). Отдельного API отправки отзывов нет. Клиентские списки заявок: `/api/ModerationShops/mine`, `/api/ModerationRoasters/mine`, `/api/ShopChangeRequests/mine`; их формат и параметры описаны в [moderation-mine.md](moderation-mine.md). Новую кофейню или обжарщика можно предложить анонимно.

`PATCH /api/Users/me/username` меняет username и адрес пользователя; ранняя повторная смена возвращает 409. Исторические скрытые или удалённые связи возвращаются как `null` либо опускаются без GUID fallback.
