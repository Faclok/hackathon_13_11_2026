# Башперевод

Рабочий MVP башкирской языковой школы: React сайт, основной REST API на C#, отдельный LMS API на C#, Telegram бот на C#, PostgreSQL и Nginx. Для сервера без общего входного прокси предусмотрен Caddy с бесплатным автоматическим HTTPS на `bashperevod.ru`.

## Что работает

- Регистрация и вход на сайте, профиль и баллы.
- Общее задание дня для всех участников: башкирская фраза и предложенный русский перевод.
- Подтверждение или исправление перевода с сайта и через Telegram. Повторная отправка обновляет ответ без повторного начисления баллов.
- Согласование: два одинаковых независимых ответа подтверждают перевод; разногласие отмечается как `disputed`. Исходный демонстрационный перевод не считается голосом.
- Рейтинг, подтверждённые переводы и CSV выгрузка.
- Отдельный LMS API с вводным курсом, уроками и сохранением прохождения.

Сейчас поддерживается пара `ba-ru`. В базе лежат **демонстрационные фразы**, созданные для проверки системы. Перед публичным использованием их нужно вычитать носителем языка. Импорт реальных датасетов, аудио, экспертная модерация и привязка Telegram аккаунта к веб аккаунту в этот MVP не включены.

## Архитектура

```text
Интернет :80/:443 → общий прокси Coolify (Traefik, TLS)
                              ├─ vinokurov-it.ru → launch_core
                              └─ bashperevod.ru → nginx:80
                                                    ├─ /          → React
                                                    ├─ /api/lms/* → LMS API
                                                    └─ /api/*     → основной API
Telegram Bot API ↔ Telegram бот → основной API
основной API → main-db (PostgreSQL)
LMS API → lms-db (PostgreSQL)
Telegram бот → bot-db (PostgreSQL)
```

Служебные `/api/internal/*` доступны только внутри сети Compose: внешний Nginx отвечает 404, а основной API дополнительно проверяет секретный заголовок.
Все три C# сервиса работают со своей базой через EF Core. LMS узнаёт пользователя через защищённый вызов основного API, не читая его таблицы. Бот хранит состояние диалога и позицию опроса Telegram в собственной базе.

## Развёртывание через Coolify

В текущей установке Coolify серверный прокси — **Traefik**. Веб-сервису этого проекта не нужен собственный Caddy и не нужно публиковать порты 80/443. Основной `compose.yaml` содержит только сервисы приложения; Nginx слушает порт 80 **внутри** Docker сети. Каждому C# сервису выделены отдельный PostgreSQL контейнер и постоянный том. Таблицы создаются миграциями EF Core при запуске сервисов.

1. Направьте DNS `A` запись `bashperevod.ru` на тот же публичный IP, что и `vinokurov-it.ru`. Если есть `AAAA`, она должна указывать на рабочий IPv6 этого сервера; иначе удалите её. `www` добавляйте только при наличии отдельной DNS записи.
2. В Coolify выберите Git репозиторий `Faclok/hackathon_13_11_2026`, ветку `main`, стратегию **Docker Compose**, путь `/compose.yaml`.
3. В Environment Variables задайте случайный `POSTGRES_PASSWORD` и отдельный случайный `INTERNAL_API_KEY`. Для генерации двух секретов можно дважды выполнить `openssl rand -hex 32`. Роль PostgreSQL `app` задана в Compose. `TELEGRAM_BOT_TOKEN` нужен только для работающего Telegram бота; при пустом значении контейнер остаётся в режиме ожидания. Три контейнера PostgreSQL используют раздельные базы и тома.
4. В Domains назначьте **сервису `nginx`** адрес `https://bashperevod.ru`, внутренний порт **80**. Удалите старую запись `https://bashperevod.ru:8088` у сервиса `caddy`: `8088` был портом хоста в прежней конфигурации, а не внутренним портом Nginx. Запись `www.bashperevod.ru` удалите, если она пока не нужна или не имеет DNS записи.
5. После обновления исходников выполните Deploy. Проверьте логи сборки и адреса `https://bashperevod.ru/api/health` и `https://bashperevod.ru/api/lms/health`. Работающий маршрут `vinokurov-it.ru` не меняйте.

Ранее деплой в Coolify останавливался при монтировании `deploy/nginx/default.conf` с хоста. Образы в текущей версии устраняют эту зависимость. Первый старт новых баз создаёт таблицы миграциями EF Core и добавляет демонстрационные записи. Старый общий том `postgres_data` не удаляется автоматически, но новые сервисы его не используют. Если там есть пользовательские данные, перенесите их до продуктивного использования новой схемы.

### Развёртывание без Coolify

Создайте `.env` из `.env.example`, задайте два случайных секрета и при необходимости токен Telegram бота:

```bash
cp .env.example .env
nano .env
chmod 600 .env
```

Если внешний Caddy работает **на самом сервере**, запустите Nginx с локальным портом 8088:

```bash
docker compose -f compose.yaml -f compose.host-caddy.yaml up -d --build
```

В действующий Caddyfile добавьте блок, сохранив существующие домены:

```caddyfile
bashperevod.ru {
    reverse_proxy 127.0.0.1:8088
}
```

Если внешний Caddy работает **в Docker**, подключите Nginx к его сети через `compose.caddy-network.yaml` и направьте Caddy на `bashperevod-nginx:80`. Имя сети укажите в `.env` как `CADDY_DOCKER_NETWORK`.

Если сервер свободен от других входных прокси, можно запустить Caddy из этого репозитория:

```bash
docker compose -f compose.yaml -f compose.standalone.yaml up -d --build
```

Этот режим занимает публичные порты 80/443 и автоматически получает HTTPS сертификат для `bashperevod.ru`. Он не подходит для текущего сервера с работающим Coolify Traefik.

### Обновление и резервное копирование

При развёртывании без Coolify:

```bash
git pull
docker compose -f compose.yaml -f compose.host-caddy.yaml up -d --build
docker compose exec -T main-db pg_dump -U app main > main-backup.sql
docker compose exec -T lms-db pg_dump -U app lms > lms-backup.sql
docker compose exec -T bot-db pg_dump -U app bot > bot-backup.sql
```

Если вы использовали другое дополнение Compose, при обновлении повторите тот же набор `-f` файлов.

В Coolify обновляйте приложение через Deploy из Git и настраивайте резервные копии всех трёх томов PostgreSQL. Перед изменением схемы сделайте резервную копию. Все три C# сервиса применяют собственные миграции EF Core при запуске. Для новой миграции используйте `dotnet ef migrations add <Имя> --project src/main-api --context AppDbContext`; для LMS и бота укажите соответственно `src/lms-api` / `LmsDbContext` или `src/telegram-bot` / `BotDbContext`.

## Как проверить сценарий

1. Зарегистрируйте двух разных пользователей на сайте. Оба получат одну и ту же фразу дня.
2. Подтвердите предложенный перевод от обоих пользователей. После второго ответа запись появится в разделе «Сообщество» и в `GET /api/translations`.
3. В LMS откройте «Курс» и отметьте урок пройденным.
4. В Telegram отправьте боту `/start`, `/task`, затем `да` или свой перевод. `/score` показывает баллы, `/cancel` отменяет ожидаемый ответ. Бот хранит состояние диалога в PostgreSQL через основной API.

Веб и Telegram аккаунты сейчас отдельные. Бот создан как ещё один независимый участник согласования.

## REST API

Публичные маршруты на `https://bashperevod.ru`:

| Метод | Маршрут | Назначение |
| --- | --- | --- |
| POST | `/api/auth/register` | Регистрация: `username`, `password`, `displayName` |
| POST | `/api/auth/login` | Вход: `username`, `password` |
| GET | `/api/me` | Профиль, нужен `Authorization: Bearer <token>` |
| GET | `/api/tasks/today` | Задание дня, нужен токен |
| POST | `/api/tasks/{id}/submission` | `{"action":"confirm"}` или `{"action":"correct","answerText":"..."}` |
| GET | `/api/leaderboard` | Рейтинг |
| GET | `/api/translations` | Только подтверждённые переводы с источником |
| GET | `/api/translations/export` | CSV выгрузка |
| GET | `/api/lms/courses` | Список курсов |
| GET | `/api/lms/courses/{id}/lessons` | Уроки курса |
| GET | `/api/lms/me/progress` | Прогресс, нужен токен |
| POST | `/api/lms/lessons/{id}/complete` | Отметить урок, нужен токен |

Пример регистрации и ответа:

```bash
curl -X POST https://bashperevod.ru/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"student1","password":"long-demo-password","displayName":"Студент"}'
# Сохраните token из ответа:
curl https://bashperevod.ru/api/tasks/today -H 'Authorization: Bearer <token>'
curl -X POST https://bashperevod.ru/api/tasks/<id>/submission \
  -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' \
  -d '{"action":"confirm"}'
```

Исходники разложены по `client/`, `src/main-api/`, `src/lms-api/`, `src/telegram-bot/`; миграции лежат в `Migrations/` каждого C# сервиса, старая общая схема сохранена в `db/legacy_shared_schema.sql`; конфигурация прокси — `deploy/`.

## Локальная проверка сборки

```bash
dotnet build src/main-api/MainApi.csproj -c Release
dotnet build src/lms-api/LmsApi.csproj -c Release
dotnet build src/telegram-bot/TelegramBot.csproj -c Release
cd client && npm ci && npm run build
```

Для полного запуска нужен Docker Compose и PostgreSQL. Секреты из `.env` не коммитятся.
