# Контекст для коллеги и ИИ: уроки main-api → основной UI

Сверено с кодом 14.11.2026, ветка `main`, исходный HEAD `a9790ac`.
Это сохранённый контекст работы, а не обещание уже выполненной интеграции.

## Прочитать сначала

1. Этот файл — границы задачи и короткая карта.
2. [LESSON-INTEGRATION.md](LESSON-INTEGRATION.md) — парсинг, все механики, запросы, ошибки, перенос.
3. [BACKLOG.md](BACKLOG.md) — порядок следующих изменений и критерии готовности.
4. [src/examples/daily-lesson.json](src/examples/daily-lesson.json) — интерактивный образец шести видов.

Этого достаточно для начала работы с UI. Код читать адресно по таблице ниже.
Если контракт меняется, проверить backend-файлы и обновить эти документы вместе с примерами.

## Что есть сейчас

| Часть | Фактическое состояние |
| --- | --- |
| `src/main-api` | Реальное ядро: дневной урок, шесть видов упражнений, серверная проверка, проверки переводов, босс |
| `client/test` → `/test/` | Отдельный рабочий кабинет на обычном JS; использует реальный API и умеет показывать примеры без входа |
| `client/src` → `/` | React UI с игровой моделью, моками уроков и локальной проверкой `choice/match/order` |
| Telegram | Ещё один клиент того же прогресса ядра; веб не должен создавать параллельный локальный прогресс |
| LMS | Отдельные курсы/задания и маршруты `/api/lms/*`; это другой сценарий |

`main` здесь — текущая git-ветка и контекст основного интерфейса/ядра. `test ui` — каталог `client/test`, а не ветка и не набор автотестов.

Задача коллеги: брать из `/test` контракт и поведение, переносить в основной React UI с его оформлением.
Учебный `translation-text` и проверка перевода имеют разные последствия и разные маршруты.
Не превращать ответ ядра в старые моки с полями `correct`, `correctOrder` и готовыми правильными парами.
В API нет игровой экономики, XP, монет, рангов и рейтинга; их привязка к реальным попыткам требует отдельного решения.
Дневной босс ядра открывается после упражнений; ограничение по званию из мокового `/boss` не является правилом ядра.

## Минимальная карта кода

Все пути в этой таблице относительно корня репозитория.

| Вопрос | Файл / точные символы для поиска |
| --- | --- |
| Как показать упражнение и собрать ответ | `client/test/src/exercise-view.js`: `exerciseKinds`, `renderers`, `renderExercise`, `readExerciseAnswer` |
| Загрузить урок, отправить, обновить прогресс | `client/test/src/workspace.js`: `lesson`, `renderLesson`, действия `exercise-issue`, `exercise-answer` |
| Босс | Там же: `renderDailyBoss`, `lesson-boss`, `daily-boss-answer` |
| Проверки переводов | Там же: `renderAssignment`, `submitReview`, действие `receive` |
| Авторизация, 204, timeout, refresh | Там же: `request`, `refresh`, `redact`, `selectActor` |
| Формы и оформление кабинета | `client/test/src/workspace-layout.js`, `workspace.css`; навигация — `workspace-view.js` |
| Что менять при переносе в React | `client/src/api/index.ts`, `types/index.ts`, `pages/Learn.tsx`, `pages/Lesson.tsx`, `pages/Daily.tsx`, `pages/Boss.tsx`, `store/index.ts` |
| Входы и proxy | `client/src/main.jsx`, `client/vite.config.js` |
| DTO ядра | `src/main-api/src/BashkirSchool.Application/LearningContracts.cs`; выдача перевода — `Contracts.cs` |
| Виды, версии, проверка | `src/main-api/src/BashkirSchool.Application/ExerciseHandlers.cs`: `IExerciseHandler`, `ExerciseRegistry`, конкретные handlers |
| Сохранение урока и попыток | `src/main-api/src/BashkirSchool.Infrastructure/LearningWorkflow.cs` |
| Возобновление активной проверки | `src/main-api/src/BashkirSchool.Infrastructure/ReviewWorkflow.cs` |
| Маршруты / DI | `src/main-api/src/BashkirSchool.Host/Program.cs`, `BashkirSchool.Infrastructure/DependencyInjection.cs` |

Более глубокий договор: [EXERCISE-API.md](../../src/main-api/docs/EXERCISE-API.md).
Подготовка аккаунтов и материалов: [USAGE.md](USAGE.md).

## Готовый запрос для следующей ИИ-сессии

> Подключаем основной React UI к реальным урокам main-api. Прочитай `client/test/AI-HANDOFF.md`, `LESSON-INTEGRATION.md` и `BACKLOG.md` рядом с ним. Используй `/test` как работающий пример контракта, сохрани основной дизайн. Сначала проверь актуальность указанных символов и состояние рабочей директории. Выбирай отображение по `(kind, schemaVersion)`, собирай только разрешённый answer, отправляй его в API и перечитывай урок. Правильные ответы и прогресс определяет сервер. Поддержи неизвестные виды/версии без отправки и без подмены другим заданием. Не смешивай упражнения, reviews, LMS и игровую экономику. Бери задачи из backlog по порядку, отмечай сделанное и обновляй документацию. Не сканируй весь репозиторий: карта нужных файлов уже здесь.

## Как поддерживать этот контекст

При добавлении вида обновлять контракт, renderer/сборщик ответа, локальный preview и эту документацию.
При изменении сценария босса обновлять также таблицу состояний и backlog.
Не записывать токены, пароли и подписанные аудиоссылки в handoff или примеры.
Снимки JSON иллюстративны: ID вымышлены, `example.invalid` не проигрывается, дата не означает «сегодня».
