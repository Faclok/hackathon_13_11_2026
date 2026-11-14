# Telegram-бот ученика

Бот использует установленные **TelegramBotKit и TelegramBotKit.Hosting 0.4.0-preview.1**. Запуск построен по [Quick Start](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/quickstart.md) и [примеру ConsolePolling](https://github.com/NickFeD/TelegramBotKit/blob/master/samples/TelegramBotKit.Sample.ConsolePolling/Program.cs): `Host.CreateApplicationBuilder`, `AddTelegramBotKit`, `AddTelegramBotKitPolling`, `RunAsync`.

Команды регистрируются штатными `AddMessageCommand`, `AddTextCommand`, `AddCallbackCommand` и реализуют `IMessageCommand`, `ITextCommand`, `ICallbackCommand`; схема взята из [Commands and routing](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/commands-and-routing.md). `IDefaultMessageHandler` обрабатывает свободные текстовые ответы. Неизвестные callback-запросы получают ответ через `IDefaultCallbackHandler`.

Кнопки создаются через `TelegramBotKit.Keyboards.Keyboard`: `Reply`, `Text`, `Inline`, `Row`, `Callback` по [инструкции Keyboards](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/keyboards.md). Библиотека кодирует callback-аргументы и проверяет ограничение 64 байта. Отправка, редактирование сообщений и подтверждение нажатий идут через `BotContext.Sender`; очередь отправки и повторные попытки включены через `UseQueuedMessageSender`.

Состояние упражнения, выбранные слова/пары и текущая проверка перевода сохраняются в PostgreSQL. Это вариант **State machine**, описанный в [Conversations](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/conversations.md), позволяющий продолжать задания после перезапуска. Получением обновлений, polling offset в памяти и планированием занимается Hosting библиотеки. При аварийной остановке библиотека не обещает сохранять ещё не обработанную очередь обновлений; обработанные ответы и выбор пользователя хранятся приложением. Ручного цикла `GetUpdates` в приложении нет.

## Параметры библиотеки

`Program.cs` читает секцию `TelegramBotKit` стандартным `Bind`. В Compose токен передаётся прежним `TELEGRAM_BOT_TOKEN`; также поддерживается `TelegramBotKit__Token`.

При локальной сборке существующий `appsettings.json` копируется рядом с приложением (`PreserveNewest`) и загружается стандартным Host независимо от рабочей папки IDE. Настройки окружения имеют приоритет. Параметры `MAIN_API_URL`, `INTERNAL_API_KEY`, `STUDENT_PORTAL_URL` можно задавать в этом файле; подключение к базе — в `ConnectionStrings:Default`. При публикации локальный файл не включается: в Compose используются переменные окружения.

| Параметр | Настройка приложения |
| --- | --- |
| `Polling.Limit` | По умолчанию 100; можно задать `TelegramBotKit__Polling__Limit` |
| `Polling.TimeoutSeconds` | По умолчанию 25; можно задать `TelegramBotKit__Polling__TimeoutSeconds` |
| `Polling.MaxDegreeOfParallelism` | 1: сообщения и callback одного ученика последовательно изменяют общий сохранённый выбор |
| `Polling.AllowedUpdates` | `Message`, `CallbackQuery` |
| `GlobalMaxPerSecond` | 25 |
| `PerChatMinDelay` | 1 секунда |
| `MaxRetryAttempts` | 5 |

Фильтр личных чатов подключён штатным `UseMiddleware<PrivateStudentChat>()`. См. [Hosting](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/hosting.md) и [Messaging](https://github.com/NickFeD/TelegramBotKit/blob/master/docs/messaging.md).

## Запуск

`/daily` продолжает дневной урок: сначала учебные упражнения, затем финальный «Босс» —
подтверждение или исправление перевода реального фрагмента. Урок завершён после ответа боссу.
Если подходящих переводов пока нет, бот предлагает вернуться позже. Прогресс босса сохраняется
на сервере, а начатое исправление — в состоянии диалога. После перезапуска `/daily` возобновляет задание.

В корневом `.env` задайте:

```dotenv
TELEGRAM_BOT_TOKEN=токен-от-BotFather
TELEGRAM_BOT_USERNAME=имя_бота_без_собачки
INTERNAL_API_KEY=отдельный-длинный-случайный-секрет
STUDENT_PORTAL_URL=https://bashperevod.ru/telegram.html
```

Также нужны обычные настройки приложения: `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY` и данные администратора первого запуска. Один `INTERNAL_API_KEY` Compose передаёт боту и ядру (`Telegram:InternalKey`). Вне Compose задайте эти настройки обоим процессам самостоятельно, а боту — `MAIN_API_URL` и `ConnectionStrings__Default`.

```sh
docker compose up -d --build
```

Compose запускает бот и PostgreSQL с базой `bot` и постоянным томом `bot_data_v2`. Миграции базы бота применяются при старте. Новая миграция добавляет привязку входящего ответа к исходному упражнению: повторная доставка сообщения после перезапуска не отвечает за ученика на следующее упражнение. Старые данные не удаляются. В ядре используются существующие таблицы Identity для одноразовых кодов и привязок; новая миграция ядра для Telegram не требуется.

## Вход и задания

1. Учитель выдаёт ученику действующую учётную запись; самостоятельный участник также может использовать свою учётную запись.
2. Ученик открывает `/telegram.html` основного сайта и вводит логин и пароль.
3. Нажимает «Открыть бота в Telegram», затем «Старт». Одноразовая ссылка действует десять минут; новая ссылка отменяет предыдущую. Пароль в Telegram не отправляется.
4. В меню доступны **Профиль**, **Задания на сегодня**, **Виды заданий**. `/profile` показывает профиль, `/daily` продолжает урок, `/tasks` открывает выбор вида задания, `/cancel` возвращает в меню.
5. Ответы и прогресс сохраняются в ядре и совпадают с `/api/lessons/today`. Уже выполненное упражнение не выдаётся повторно. После последнего ответа бот показывает завершение дня; если материалов нет — сообщает об этом.

Страница привязки хранит токен только в памяти и отзывает созданную для неё сессию при выходе со страницы. На ней же можно отвязать Telegram; отвязка также аннулирует ожидающую ссылку. Для смены Telegram сначала отвяжите старый аккаунт, затем получите новую ссылку.

Поддерживаются все текущие типы заданий:

| Тип | Ответ в боте |
| --- | --- |
| Перевод текста | Текст перевода |
| Аудиодиктант | Услышанный текст |
| Перевод аудио | Текст перевода |
| Пропущенное слово | Кнопка со словом или свой текст |
| Порядок слов | Нажатия на слова в нужном порядке, затем «Отправить ответ» |
| Соответствия слов | Выбор слова, затем перевода для каждой пары, затем «Отправить ответ» |
| Выбор перевода | Кнопка с одним из показанных переводов или «Свой перевод» и текст |
| Подтверждение перевода | «Подтвердить перевод» или «Свой перевод» и текст |

При сборке слов и пар бот редактирует сообщение и показывает текущий выбор. Последнее действие можно отменить. Устаревшая кнопка другого упражнения не отправляет ответ; повторная доставка не применяет его к следующему заданию. Незавершённый выбор сохраняется при перезапуске. Текстовые форматы `3 1 2` и `1-3 2-1 ...` также поддержаны.

Для аудиозаданий ядро выдаёт подписанную ссылку на готовую запись. При выборе конкретного аудиорежима вызывается штатная выдача ядра, которая подготавливает недостающее аудио. Бот скачивает файл отдельным клиентом без внутреннего ключа API. WAV от Silero загружается в Telegram как файл, MP3/M4A — как аудиозапись: [ограничения sendAudio](https://core.telegram.org/bots/api#sendaudio). URL хранилища должен быть доступен боту. Исходный текст диктанта ученику не показывается.

Бот использует состав урока и дату ядра. Нераспознанный учебный ответ сохраняется как попытка, как и на сайте. Проверка перевода идёт через отдельный процесс reviews: бот сообщает о сохранении мнения, не объявляя популярный вариант доказанно верным. Сервер может вернуть уже начатую проверку другого режима — бот отображает фактически полученный `kind`.

## Доступ

Обрабатываются только личные чаты. Привязка не создаёт аккаунт и не выдаёт новых прав. Ядро на каждом запросе проверяет активность аккаунта, наличие участника и собственные разрешения `profile.read`, `lessons.receive`, `exercises.submit`.

Для сайта: `POST /api/me/telegram-link` (JWT владельца) возвращает `{code, expiresAt, url}`; `DELETE /api/me/telegram-link` отзывает привязку и ожидающий код. Если имя бота не настроено, `url` будет `null`, а страница предложит отправить `/start код` своему боту вручную.

Боту доступны только следующие маршруты с `X-Internal-Key`:

- `POST /internal/telegram/{telegramId}/link` с `{code}`;
- `GET /internal/telegram/{telegramId}/profile`;
- `GET /internal/telegram/{telegramId}/lesson`;
- `POST /internal/telegram/{telegramId}/lesson/exercises` с `{kind, languagePair}`;
- `POST /internal/telegram/{telegramId}/exercises/{exerciseId}/answer` с `{answer: ...}`.
- `POST /internal/telegram/{telegramId}/reviews/next?kind=translation-choice` (либо `translation-confirm`);
- `POST /internal/telegram/{telegramId}/reviews/{assignmentId}/response` с ответом проверки перевода.

Внутренний ключ не является JWT и не открывает административные или экспертные маршруты. При пустом ключе внутренние маршруты закрыты. Публичный nginx не проксирует `/internal/`; обращение к ядру выполняется внутри сети Compose. Telegram ID берётся из отправителя сообщения, автор упражнения определяется ядром по привязке.

## Проверка

Из корня репозитория:

```sh
dotnet test src/telegram-bot.tests/TelegramBot.Tests.csproj
```

Из `src/main-api` (для выбора закреплённого SDK):

```sh
dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --filter FullyQualifiedName~TelegramStudentsTests
```

| Требование | Тест |
| --- | --- |
| TelegramBotKit действительно маршрутизирует команды и ответы | `TelegramBotKitDispatchesSlashCommandsAndPlainAnswersToConversation` |
| Только собственный профиль, без прав администратора | `BotRequiresKeyAndSingleUseLinkAndCannotUseStudentTokenAsBotCredentials` |
| Одноразовые ссылки, срок действия, отвязка | `ExpiredAndReplacedLinksAreRejectedAndUnlinkRevokesBothBindingAndPendingCode` |
| Ежедневные задания, сохранение прогресса, запрет чужих ответов, блокировка аккаунта | `DailyAnswersBelongToLinkedStudentAndProgressPersistsAcrossWebsiteAndBot` |
| Продолжение диалога и защита от повторной доставки | `ChatResumesAfterRestartAndRedeliveryNeverAnswersTheNextExercise` |
| Личные данные не раскрываются в группах | `GroupMessagesNeverRequestStudentDataOrCreateConversationState` |
| Текстовые и структурированные ответы всех текущих типов | `TextAnswerPreservesBashkirLettersAndPunctuation`, `NumberedAnswersUseIssuedIdentifiersForGapsOrderAndPairs`, `InvalidOrderDoesNotSubmitAnAnswer`, `InvalidPairsDoNotSubmitAnAnswer` |
| Выбор и прохождение всех шести видов, загрузка WAV | `EveryExerciseKindCanBeSelectedDisplayedAndCompleted` |
| Кнопки сборки/сопоставления/пропуска, сохранение выбора, старые и повторные кнопки | `NativeButtonsCompleteExercisesAndRejectOldButtons` |
| Команды и callback направляются библиотекой | `LibraryRoutesMentionedSlashCommandAndAcknowledgesUnknownCallback` |
| Кнопки выбора/подтверждения перевода и собственный текст после перезапуска | `ReviewModesPreserveIssuedOptionsAndCustomInputAcrossRestart` |
| Новые маршруты ядра, чужая выдача и неверный вариант | `BotIssuesRequestedExerciseAndReviewAndOnlyLinkedOwnerCanSubmit` |

Автоматические проверки используют подставной транспорт Telegram и SQLite; живой Telegram и PostgreSQL требуют запуска с настоящими настройками.
