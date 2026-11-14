# `Controllers/CommunityController.cs`

[Открыть исходный файл](../../Controllers/CommunityController.cs)

## Назначение

Публичный маршрут рейтинга участников.

## Как работает

`GET /api/leaderboard` запрашивает готовый рейтинг у `CommunityService` и возвращает его как JSON.

## Что учитывать

Подсчёт баллов и сортировка находятся в бизнес-сервисе.
