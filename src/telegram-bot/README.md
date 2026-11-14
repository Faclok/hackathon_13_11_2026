# Telegram-бот ученика

Бот использует установленный **TelegramBotKit 0.3.2**: DI, `IUpdateDispatcher`, `IDefaultMessageHandler`, `BotContext` и `IMessageSender`. Получение обновлений идёт последовательно через типизированный клиент библиотеки; позиция polling и диалог сохраняются в PostgreSQL. Прежние ручные HTTP-вызовы Telegram и обращения к удалённым `/api/internal/telegram/*` заменены.

## Запуск

В корневом `.env` задайте:

```dotenv
TELEGRAM_BOT_TOKEN=токен-от-BotFather
TELEGRAM_BOT_USERNAME=имя_бота_без_собачки
INTERNAL_API_KEY=отдельный-длинный-случайный-секрет
STUDENT_PORTAL_URL=https://bashperevod.ru/telegram.html
```

Также нужны обычные настройки приложения: `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY` и данные администратора первого запуска. Один `INTERNAL_API_KEY` Compose передаёт боту и ядру (`Telegram:InternalKey`). Вне Compose задайте эти настройки обоим процессам самостоятельно, а боту — `MAIN_API_URL` и `ConnectionStrings__Default`.

```sh
docker compose --profile telegram up -d --build
```

Профиль `telegram` включает бот и его базу. Миграции базы бота применяются при старте. Новая миграция добавляет привязку входящего ответа к исходному упражнению: повторная доставка сообщения после перезапуска не отвечает за ученика на следующее упражнение. Старые данные не удаляются. В ядре используются существующие таблицы Identity для одноразовых кодов и привязок; новая миграция ядра для Telegram не требуется.

## Вход и задания

1. Учитель выдаёт ученику действующую учётную запись; самостоятельный участник также может использовать свою учётную запись.
2. Ученик открывает `/telegram.html` основного сайта и вводит логин и пароль.
3. Нажимает «Открыть бота в Telegram», затем «Старт». Одноразовая ссылка действует десять минут; новая ссылка отменяет предыдущую. Пароль в Telegram не отправляется.
4. В меню бота доступны **Профиль** и **Задания на сегодня**. Команды: `/profile`, `/daily`, `/cancel`.
5. Ответы и прогресс сохраняются в ядре и совпадают с `/api/lessons/today`. Уже выполненное упражнение не выдаётся повторно. После последнего ответа бот показывает завершение дня; если материалов нет — сообщает об этом.

Страница привязки хранит токен только в памяти и отзывает созданную для неё сессию при выходе со страницы. На ней же можно отвязать Telegram; отвязка также аннулирует ожидающую ссылку. Для смены Telegram сначала отвяжите старый аккаунт, затем получите новую ссылку.

Поддерживаются все текущие типы заданий:

| Тип | Ответ в боте |
| --- | --- |
| Перевод текста | Текст перевода |
| Аудиодиктант | Услышанный текст |
| Перевод аудио | Текст перевода |
| Пропущенное слово | Номер варианта или слово |
| Порядок слов | Номера слов через пробел, например `3 1 2` |
| Соответствия слов | Пары номеров, например `1-3 2-1 3-2 4-5 5-4` |

Для аудиозаданий должны быть подготовлены аудиоматериалы ядра, а URL хранилища должен быть доступен серверам Telegram. Бот использует состав урока и дату, назначенные ядром; свою подборку и баллы не создаёт. Нераспознанный ответ сохраняется как попытка, как и на сайте: это не экспертная оценка лингвистической правильности.

## Доступ

Обрабатываются только личные чаты. Привязка не создаёт аккаунт и не выдаёт новых прав. Ядро на каждом запросе проверяет активность аккаунта, наличие участника и собственные разрешения `profile.read`, `lessons.receive`, `exercises.submit`.

Для сайта: `POST /api/me/telegram-link` (JWT владельца) возвращает `{code, expiresAt, url}`; `DELETE /api/me/telegram-link` отзывает привязку и ожидающий код. Если имя бота не настроено, `url` будет `null`, а страница предложит отправить `/start код` своему боту вручную.

Боту доступны только следующие маршруты с `X-Internal-Key`:

- `POST /internal/telegram/{telegramId}/link` с `{code}`;
- `GET /internal/telegram/{telegramId}/profile`;
- `GET /internal/telegram/{telegramId}/lesson`;
- `POST /internal/telegram/{telegramId}/exercises/{exerciseId}/answer` с `{answer: ...}`.

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

Автоматические проверки используют подставной транспорт Telegram и SQLite; живой Telegram и PostgreSQL требуют запуска с настоящими настройками.
