# `Controllers/AuthController.cs`

[Открыть исходный файл](../../Controllers/AuthController.cs)

## Назначение

HTTP слой регистрации, входа и профиля.

## Как работает

Обрабатывает `POST /api/auth/register`, `POST /api/auth/login` и `GET /api/me`. Тело запроса передаётся в `AuthService`; профиль получает заголовок Authorization.

## Что учитывать

Валидация логина, пароля и сессии выполняется в сервисах. Маршрут `/api/me` задан абсолютным путём, поскольку не входит в префикс `/api/auth`.
