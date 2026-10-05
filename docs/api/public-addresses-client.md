# Контракт публичных адресов для клиентов

Текущий API меняется напрямую, без `/v2`. Кофейни, обжарщики, города, кофейные зоны, пользователи и справочники используют slug. Адрес приходит вместе с данными; дополнительный запрос `public-address` не нужен. Этот контракт относится к новой сборке, а не к уже работающей версии сервера.

## Идентификаторы и адреса

```ts
type PublicAddress = {
  slug: string;
  canonicalPath: string;
  revision: number;
  isAlias: boolean;
};

type ApiResponse<T> = {
  isSuccess: boolean;
  message: string | null;
  data: T;
};

type Page<T> = {
  items: T[];
  totalItems: number;
  totalPages: number;
  currentPage: number;
  pageSize: number;
};
```

`canonicalPath` — относительный путь страницы клиента, например `/coffee-shops/26-october-16`, а не API URL. Для навигации используйте этот путь, для запросов — `slug`. Не генерируйте slug из имени самостоятельно. Полученные значения справочников тоже сохраняйте как строки, не преобразуйте в UUID.

Публичные DTO не содержат GUID кофейни, города, обжарщика, зоны, пользователя или справочника. GUID отзывов, check-in, фотографий и заявок модерации остаются служебными идентификаторами. В ответе создания заявки `shopId`/`roasterId` обозначает заявку, а не опубликованную сущность. Административный API, кабинет владельца, внутренние сообщения и аутентификация сохраняют внутренние ID.

Slug уникален внутри вида сущности. Переименование отображаемого имени кофейни или справочника не меняет его. При явной смене адреса старый slug перестаёт открываться и может использоваться снова. При удалении сущности её slug освобождается. У пользователя адрес следует за username; смена username разрешена раз в семь дней.

## Чтение

| GET | Данные ответа |
| --- | --- |
| `/api/CoffeeShops` | `ApiResponse<{coffeeShops: ShopSummary[], totalItems, totalPages, currentPage, pageSize}>` |
| `/api/CoffeeShops/{slug}` | `ApiResponse<ShopDetails>`; данные лежат непосредственно в `data`, без `shopDto` |
| `/api/Roasters/{slug}` | `ApiResponse<RoasterDetails>` |
| `/api/Catalogs/cities/{slug}` | `{address: PublicAddress, name: string}` без `ApiResponse` |
| `/api/Catalogs/coffee-zones/{slug}` | `{address, name, description, latitude, longitude, radiusMeters, shopCount, polygon}` без `ApiResponse` |
| `/api/Users/by-slug/{slug}` | `{data: PublicUserProfile, address: PublicAddress}` |
| `/api/Users/me` | `ApiResponse<UserProfile>`; в `data.address` адрес текущего пользователя или `null` |
| `/api/users/{slug}/reviews` | `ApiResponse<Page<Review>>`, нужна авторизация |
| `/api/CheckIns` | `ApiResponse<Page<CheckIn>>` текущего пользователя, нужна авторизация |
| `/api/Map?minLat=…&minLon=…&maxLat=…&maxLon=…&zoom=…` | `ApiResponse<{shops, clusters, zones, isTruncated}>` |

Для деталей также доступны `/api/CoffeeShops/by-slug/{slug}`, `/api/Roasters/by-slug/{slug}`, `/api/Catalogs/cities/by-slug/{slug}`, `/api/Catalogs/coffee-zones/by-slug/{slug}`. Формат ответа совпадает с коротким маршрутом. У пользователей краткий `/api/Users/{slug}` тоже доступен, но `me` и `exists` — специальные маршруты; для профиля используйте `by-slug`.

У `ShopSummary` есть `address`, `city: PublicAddress | null`, `name`, `photos`, `rating`, `reviewCount`, `isVisited`, `isNew`, `isOpen`, `dataCompletenessScore`, `priceRange`, `type`, `location`, `beans`, `roasters`, `equipments`, `brewMethods`, `shopContact`. `ShopDetails` добавляет `description`, `reviews`, `userCheckIns`, `canCreateReview`, `existingReviewId`, `schedules`, `tags`, `menu`. В деталях зёрна также называются `beans`.

У вложенного обжарщика: `{address, name, photoUrl, coverPhoto}`. У отзывов `author` и `shop` — адреса вместо `userId` и `coffeeShopId`. У check-in `shop` — адрес вместо `shopId`. У маркера карты `address` вместо `id`, `primaryZone` вместо `primaryZoneId`; у зоны карты `address` вместо `id`. В деталях обжарщика `shops[]` содержит `{address, name, coverPhoto}`.

Недоступная историческая связь с удалённой/скрытой сущностью возвращается как `null` или исключается из массива связей. GUID fallback отсутствует. Фото и содержимое отзывов используют прежние поля.

Пример части ответа списка:

```json
{
  "isSuccess": true,
  "data": {
    "coffeeShops": [
      {
        "address": {"slug": "26-october-16", "canonicalPath": "/coffee-shops/26-october-16", "revision": 1, "isAlias": false},
        "city": {"slug": "minsk", "canonicalPath": "/cities/minsk", "revision": 1, "isAlias": false},
        "name": "26",
        "beans": [{"slug": "arabica", "name": "Arabica"}]
      }
    ],
    "totalItems": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 10
  }
}
```

## Справочники и фильтры

Все шесть каталогов возвращают `ApiResponse<T[]>`: массив находится непосредственно в `data`, без обёрток `cities`, `beans` и т. п.

| GET `/api/Catalogs/…` | Элемент |
| --- | --- |
| `cities` | `{address, name}` |
| `roasters` | `{address, name, photoUrl, coverPhoto}` |
| `beans` | `{slug, name}` |
| `equipments` | `{slug, name, brand, model, category}` |
| `brew-methods` | `{slug, name, category}` |
| `shop-tags` | `{slug, name, description, sortOrder}`; только активные теги |

`GET /api/CoffeeShops` принимает `city` вместо `cityId`. `roasters`, `equipments`, `beans`, `brewMethods`, `tags` — массивы slug. Передавайте массив повторением параметра:

```http
GET /api/CoffeeShops?city=minsk&beans=arabica&equipments=la-marzocco-linea&brewMethods=v60&tags=dog-friendly&tags=laptop-friendly&page=1&pageSize=20
```

Каждый массив — максимум 100 значений. Пустой массив не задаёт фильтр. Неизвестное значение возвращает 404; сервер не игнорирует его и не выдаёт все кофейни. Alias принимается и приводит к той же сущности. Остальные параметры (`q`, `isOpen`, `isNew`, `isVisited`, `priceRange`, `type`, `minRating`) сохраняют смысл. Enum в JSON сериализуются строками.

Пагинация списка кофеен: `page`, `pageSize` (1–100). Для отзывов пользователя: `pageNumber`, `pageSize`. Для `/api/CheckIns`: заголовки `X-Page-Number`, `X-Page-Size`, опциональные `from`/`to` с часовым поясом, интервал `[from, to)`. Ответы также содержат `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`.

## Запись

Текущий пользователь берётся из JWT; `userId` передавать не нужно. Для check-in, отзывов, жалоб, изменения кофейни и `/mine` нужен `Authorization: Bearer …`. Новую кофейню или обжарщика можно предложить анонимно.

```http
POST /api/CheckIns
Content-Type: application/json

{"shop":"26-october-16","isPublic":false,"visitedAt":"2026-09-30T12:00:00Z","rating":{"coffee":5,"place":4,"service":5},"note":"Отличный кофе"}
```

```http
POST /api/ModerationReviews
Content-Type: application/json

{"shop":"26-october-16","header":"Хорошее место","comment":"Отличный кофе","rating":{"coffee":5,"place":4,"service":5},"photos":[]}
```

У отзыва также поддерживаются плоские `ratingCoffee`, `ratingPlace`, `ratingService`; при наличии вложенного `rating` он имеет приоритет. Оценки и бизнес-ограничения проверяет существующий обработчик.

| POST | Изменённые поля запроса |
| --- | --- |
| `/api/ShopIssueReports` | `shop: string`; `category`, `description` сохраняются |
| `/api/ShopChangeRequests` | `shop: string`, `section`, `payload`; в payload `tags`, `roasters`, `equipments`, `brewMethods` — массивы строк вместо `*Ids` |
| `/api/ModerationShops` | `city: string`, `beans`, `equipments`, `roasters`, `brewMethods`: массивы строк; `name`, `address`, контакты, расписание и фото сохраняются |
| `/api/ModerationRoasters` | `city: string | null` вместо `cityId`; остальные поля сохраняются |

`null` у поля payload означает «поле не задано», `[]` — «очистить список». Одна заявка изменения относится к одной `section`; правила допустимых полей сохраняются. В gallery/menu `retainedPhotoIds` остаются служебными GUID фотографий.

```json
{"shop":"26-october-16","section":"Tags","payload":{"tags":["dog-friendly","laptop-friendly"]}}
```

`GET /api/ModerationShops/mine`, `/api/ModerationRoasters/mine`, `/api/ModerationReviews/mine`, `/api/ShopChangeRequests/mine` возвращают `ApiResponse<Page<T>>`, список всегда в `data.items`. В заявках кофейни `city` и `publishedShop` — адреса, справочники — массивы slug. В заявках отзывов/изменений `shop` — адрес. Фильтр заявок изменений называется `shop` и принимает slug.

`PATCH /api/Users/me/username` принимает `{"username":"Petr"}` и возвращает 202 с `data: {username, address}`. Ранняя повторная смена — 409. Адрес может быть `null`, если функция отключена или пользователь ещё не прошёл backfill.

## Ошибки и canonical

- 400: некорректный/reserved/GUID-shaped slug, слишком большой массив или невалидное тело.
- 401/403: аутентификация/права.
- 404: неизвестная, скрытая, удалённая сущность или неизвестный фильтр.
- 409: бизнес-конфликт, включая cooldown username.
- 503: адреса отключены, обязательный адрес ещё не создан либо внутренний сервис адресов недоступен. Не пытайтесь подставлять GUID.

Сервер открывает только текущий slug; старый адрес после переименования возвращает 404. Поле `isAlias` сохранено в формате ответа и всегда равно `false`. Публичные ответы с адресами используют `Cache-Control: no-store`.

Публичные `/{id}/public-address` и `/public-addresses?ids=…` удалены. `/internal/public-addresses/**` предназначен только для серверов и недоступен через gateway. Порядок включения и backfill описан в [runbook](public-addresses.md).
