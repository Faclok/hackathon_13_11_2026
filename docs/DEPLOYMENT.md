# Развёртывание «Башперевода»

Инструкция относится к корневому Docker Compose этого репозитория. Он собирает три клиента, ядро, LMS API, экспертный шлюз, Telegram-бота, Nginx и три PostgreSQL-базы. Ядро само применяет миграции при запуске и создаёт первого администратора на пустой базе. Данные хранятся в именованных томах Compose.

## 1. Подготовка сервера

Нужны Linux-сервер с Docker Engine и плагином Compose V2, доступ к репозиторию, HTTPS для публичного запуска и DNS-записи `A`/`AAAA` для `bashperevod.ru`, `lms.bashperevod.ru` и `expert.bashperevod.ru` (а также `www`-имён, если они используются). Для автономного Caddy порты 80 и 443 должны быть свободны. TLS-сертификаты Caddy выдаёт при доступных DNS и портах.

```bash
git clone https://github.com/Faclok/hackathon_13_11_2026.git
cd hackathon_13_11_2026
cp .env.example .env
chmod 600 .env
```

Задайте в `.env` разные длинные случайные значения для `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `BOOTSTRAP_PASSWORD` и `INTERNAL_API_KEY`. Пример генерации одного значения: `openssl rand -hex 32`; повторите для каждого секрета. `JWT_SIGNING_KEY` должен содержать не менее 32 байт. Остальные переменные и условия их использования перечислены в [SECRETS.md](SECRETS.md). Сохраните `.env` в защищённом хранилище: без прежнего пароля PostgreSQL и ключа JWT восстановление работы после переноса будет затруднено.

## 2. Выбор способа публикации

Используйте **один** из следующих вариантов. Во всех вариантах Nginx выбирает клиент по заголовку `Host`, поэтому прокси должен его передавать.

### А. Сервер без существующего прокси

`compose.standalone.yaml` запускает Caddy на портах 80/443; [Caddyfile](../deploy/Caddyfile) уже содержит три домена.

```bash
docker compose --env-file .env -f compose.yaml -f compose.standalone.yaml config --quiet
docker compose --env-file .env -f compose.yaml -f compose.standalone.yaml up -d --build
docker compose --env-file .env -f compose.yaml -f compose.standalone.yaml ps
```

### Б. Caddy уже работает на хосте

`compose.host-caddy.yaml` публикует Nginx на `127.0.0.1:8088` (порт меняется через `BASHPEREVOD_HTTP_PORT`). Запустите Compose:

```bash
docker compose --env-file .env -f compose.yaml -f compose.host-caddy.yaml config --quiet
docker compose --env-file .env -f compose.yaml -f compose.host-caddy.yaml up -d --build
```

В конфигурации Caddy на хосте направьте `bashperevod.ru`, `lms.bashperevod.ru` и `expert.bashperevod.ru` на `127.0.0.1:8088` с исходным `Host`. Например:

```caddyfile
bashperevod.ru, lms.bashperevod.ru, expert.bashperevod.ru {
    reverse_proxy 127.0.0.1:8088
}
```

Если нужны `www`-адреса, добавьте их в список. Перезагрузите свой Caddy после проверки его конфигурации.

### В. Caddy работает в другом Docker-проекте

Подключите его и Nginx к одной внешней сети. Укажите её имя в `CADDY_DOCKER_NETWORK` в `.env`, затем:

```bash
docker compose --env-file .env -f compose.yaml -f compose.caddy-network.yaml config --quiet
docker compose --env-file .env -f compose.yaml -f compose.caddy-network.yaml up -d --build
```

В конфигурации существующего Caddy используйте `reverse_proxy bashperevod-nginx:80` для трёх доменов. Псевдоним `bashperevod-nginx` задаётся в `compose.caddy-network.yaml`.

### Г. Coolify

Разверните репозиторий как Docker Compose-проект. Назначьте домены `bashperevod.ru`, `lms.bashperevod.ru` и `expert.bashperevod.ru` сервису `nginx` на внутреннем порту `80`; не назначайте их клиентским контейнерам по отдельности. Добавьте секреты из `.env.example` в переменные окружения проекта. Внешний прокси Coolify должен сохранять `Host` для выбора сайта в Nginx. Дополнительные Compose-файлы Caddy в этом варианте не нужны.

## 3. Проверка после запуска

Проверьте состояние контейнеров командой `docker compose -f compose.yaml -f <выбранный-файл> ps`. При ошибке смотрите логи конкретного сервиса, например `docker compose -f compose.yaml -f compose.standalone.yaml logs --tail=100 main-api`. Не публикуйте логи с секретами.

```bash
curl -fsS https://bashperevod.ru/health/ready
curl -fsS https://lms.bashperevod.ru/api/lms/health
curl -fsS https://expert.bashperevod.ru/health/ready
curl -I https://bashperevod.ru/
curl -I https://lms.bashperevod.ru/
curl -I https://expert.bashperevod.ru/
```

Откройте три сайта в браузере и проверьте вход. Демо-доступы для жюри указаны в [README](../README.md#вход-в-кабинеты). Первый администратор создаётся из `BOOTSTRAP_USERNAME` и `BOOTSTRAP_PASSWORD` **только когда база ядра пуста**. LMS позволяет зарегистрировать отдельного пользователя. Экспертный кабинет использует тот же логин и пароль ядра; право на импорт CSV назначается отдельно. Для других, непубличных аккаунтов используйте [приватный шаблон](ACCESS.private.md.example), без коммита в Git.

Telegram-бот входит в базовый Compose. Заполните `TELEGRAM_BOT_TOKEN` и `TELEGRAM_BOT_USERNAME`, затем проверьте его по [его инструкции](../src/telegram-bot/README.md). Если бот пока не нужен, добавьте `--scale telegram-bot=0` к команде `up` (и к последующим обновлениям); иначе контейнер с пустым токеном будет перезапускаться. Для аудиоупражнений настройте ключи S3 и доступный клиентам `https://s3.bashperevod.ru`; без них остальные упражнения работают, а аудио недоступно.

## 4. Обновление и данные

Перед обновлением сделайте дампы всех трёх баз и сохраните их вне сервера вместе с `.env`. Пример для запуска из каталога репозитория:

```bash
mkdir -p backups
docker compose -f compose.yaml exec -T core-db pg_dump -U app -d core -Fc > backups/core.dump
docker compose -f compose.yaml exec -T lms-db pg_dump -U app -d lms -Fc > backups/lms.dump
docker compose -f compose.yaml exec -T bot-db pg_dump -U app -d bot -Fc > backups/bot.dump
```

После проверки ненулевого размера дампов и их переноса в защищённое хранилище обновите код и пересоберите сервисы той же комбинацией Compose-файлов, что использовали при первом запуске:

```bash
git pull --ff-only
docker compose --env-file .env -f compose.yaml -f compose.standalone.yaml up -d --build
```

В последней команде замените `compose.standalone.yaml` своим вариантом, если используете внешний прокси. Затем повторите проверки готовности из раздела 3. Не применяйте `docker compose down -v`: ключ `-v` удаляет тома с данными. Старые аккаунты и переводы из прежней базы `main_data_v2` автоматически не переносятся в `core_data`; для миграции нужен отдельный план.
