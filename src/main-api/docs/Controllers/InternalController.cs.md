# `Controllers/InternalController.cs`

[Открыть исходный файл](../../Controllers/InternalController.cs)

## Назначение

Внутренние маршруты для LMS и Telegram бота.

## Как работает

Проверяет `X-Internal-Key`, разрешает bearer token в ID пользователя и передаёт операции Telegram в `TelegramService`. Маршруты находятся под `/api/internal`.

## Что учитывать

Nginx блокирует внешние обращения к этому префиксу; проверка ключа в контроллере служит дополнительной защитой.
