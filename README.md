# Башперевод

Рабочий MVP башкирской языковой школы: React сайт, основной REST API на C#, отдельный LMS API на C#, Telegram бот на C#, PostgreSQL, Nginx и Caddy для бесплатного автоматического HTTPS на `bashperevod.ru`.

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
Интернет :80/:443 → Caddy (TLS) → Nginx
                                    ├─ /           → React (статический Nginx)
                                    ├─ /api/lms/*  → LMS API
                                    └─ /api/*      → основной API
Telegram Bot API ↔ Telegram бот → основной API
основной API + LMS API → PostgreSQL
```

Служебные `/api/internal/*` доступны только внутри сети Compose: внешний Nginx отвечает 404, а основной API дополнительно проверяет секретный заголовок.

## Развёртывание на сервере

Нужны Linux сервер с публичным IPv4, Docker Engine и Docker Compose v2, доступ к DNS домена и токен Telegram бота, если бот нужен. На сервере порты 80 и 443 должны быть свободны.

1. Создайте `A` запись `bashperevod.ru` с публичным IPv4 сервера. Если есть `AAAA` запись, она должна указывать на рабочий IPv6 этого же сервера; иначе удалите её. Откройте входящий TCP 80/443 и UDP 443.
2. Склонируйте репозиторий и перейдите в его каталог.
3. Создайте `.env` из шаблона и замените оба секрета. Для генерации используйте, например, `openssl rand -hex 32` **дважды**. В `TELEGRAM_BOT_TOKEN` укажите токен от BotFather. Если бот пока не нужен, оставьте поле пустым.

   ```bash
   cp .env.example .env
   nano .env
   chmod 600 .env
   ```

4. Запустите контейнеры:

   ```bash
   docker compose config --quiet
   docker compose up -d --build
   docker compose ps
   docker compose logs -f caddy main-api lms-api
   ```

5. Откройте `https://bashperevod.ru` и проверьте `https://bashperevod.ru/api/health` и `https://bashperevod.ru/api/lms/health`.

Caddy сам получает и обновляет сертификат, когда DNS указывает на сервер и порты доступны. Данные сертификата живут в томе `caddy_data`, PostgreSQL — в `postgres_data`. Не удаляйте эти тома при обновлении. Первый старт PostgreSQL исполняет `db/001_init.sql` и создаёт таблицы и демонстрационные записи. При уже существующем томе init скрипт повторно не запускается.

### Обновление и резервное копирование

```bash
git pull
docker compose up -d --build
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' > backup.sql
```

Перед изменением схемы существующей базы сделайте резервную копию. Для дальнейших обновлений схемы понадобятся отдельные SQL миграции; init скрипт подходит для новой установки.

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

Исходники разложены по `client/`, `src/main-api/`, `src/lms-api/`, `src/telegram-bot/`; схема базы — `db/001_init.sql`; конфигурация прокси — `deploy/`.

## Локальная проверка сборки

```bash
dotnet build src/main-api/MainApi.csproj -c Release
dotnet build src/lms-api/LmsApi.csproj -c Release
dotnet build src/telegram-bot/TelegramBot.csproj -c Release
cd client && npm ci && npm run build
```

Для полного запуска нужен Docker Compose и PostgreSQL. Секреты из `.env` не коммитятся.
