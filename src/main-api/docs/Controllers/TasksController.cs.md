# `Controllers/TasksController.cs`

[Открыть исходный файл](../../Controllers/TasksController.cs)

## Назначение

HTTP слой ежедневного задания и ответа.

## Как работает

`GET /api/tasks/today` получает задание, `POST /api/tasks/{id}/submission` записывает ответ. Оба маршрута сначала разрешают bearer token.

## Что учитывать

Назначение задания, начисление баллов и согласование перевода выполняет `TaskService`.
