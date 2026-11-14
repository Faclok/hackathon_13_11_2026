# `Migrations/AppDbContextModelSnapshot.cs`

[Открыть исходный файл](../../Migrations/AppDbContextModelSnapshot.cs)

## Назначение

Текущий снимок модели EF Core.

## Как работает

Используется командами `dotnet ef migrations add` для вычисления отличий между текущим `AppDbContext` и последней миграцией.

## Что учитывать

EF Core обновляет этот файл при создании новой миграции. Он не выполняет миграцию сам по себе.
