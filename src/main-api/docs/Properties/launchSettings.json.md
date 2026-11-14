# `Properties/launchSettings.json`

[Открыть исходный файл](../../Properties/launchSettings.json)

## Назначение

Локальные профили запуска `dotnet run` и IDE.

## Как работает

Профиль `http` использует localhost:5096, а `https` добавляет localhost:7203. Оба выставляют окружение Development.

## Что учитывать

Эти адреса относятся к локальному запуску и не задают публичные адреса Docker или прокси.
