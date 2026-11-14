# `Controllers/TranslationsController.cs`

[Открыть исходный файл](../../Controllers/TranslationsController.cs)

## Назначение

Публикация подтверждённых переводов.

## Как работает

`GET /api/translations` возвращает JSON, `GET /api/translations/export` — CSV файл. Данные получает `TranslationService`.

## Что учитывать

В публичную выборку попадают только записи со статусом `confirmed`.
