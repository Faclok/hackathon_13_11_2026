# Проверка школьного MVP ядра

## Telegram, уровни и Mini App — 15 ноября 2026

Проверены изменения бота, общего клиента и входа Mini App. Бот и сайт используют `/learning/*`, шесть видов вопросов и три перевода босса. Рейтинг и полный сайт запускаются inline-кнопками Mini App с входом в существующую Telegram-привязку. OpenAPI заново получен из локально запущенного Host; найден `/auth/telegram-mini-app`.

Команды:

- Из корня: `dotnet test src/telegram-bot.tests/TelegramBot.Tests.csproj --no-restore --logger "trx;LogFileName=telegram-bot.trx" --results-directory .data/telegram-results` — **30 пройдено, 0 ошибок, 0 пропусков**.
- Из `src/main-api`: `dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-restore --filter "FullyQualifiedName~TelegramMiniAppTests|FullyQualifiedName~TelegramStudentsTests|FullyQualifiedName~SessionAndDirectoryTests|FullyQualifiedName~LevelFlowTests" --logger "trx;LogFileName=telegram-integration.trx" --results-directory .data/telegram-results` — **30 пройдено, 0 ошибок, 0 пропусков**.
- `client`: `npm run build` — TypeScript и production-сборка прошли.

| Requirement | Evidence |
| --- | --- |
| Текущий уровень, номер и общий серверный прогресс; перезапуск без ответа за следующий вопрос | `CurrentLevelSurvivesRestartAndRedeliveryNeverAnswersNextQuestion`; `SignedLaunchUsesLinkedAccountAndUnlinkRevokesMiniAppButKeepsWebsiteSession` |
| Шесть учебных форматов и структурированные Telegram-кнопки | `AllSixLevelKindsUseServerQuestionAndAttemptToken`; `NativeButtonsBuildAnswersAndRejectStaleCallbacks` |
| Сохранение точного ответа после обрыва; старые кнопки той же задачи не используют новый токен | `LostResponseRetriesIdenticalTokenAndAnswerAfterRestart`; `ButtonsFromWrongAttemptCannotAnswerSameQuestionWithNewToken` |
| Босс из трёх последовательных настоящих проверок; неправильный ответ и отсутствие материала не увеличивают прогресс | `BossHasThreeRealReviewsAndResumesBetweenSteps`; `IncorrectAndEmptyStatesDoNotInventProgress` |
| Рейтинг и полный сайт через inline `web_app`; группы игнорируются | `MenuUsesMiniAppButtonsForRatingAndFullSiteAndGroupsAreIgnored` |
| Вход по подписанному Telegram ID, без создания дубликатов и без повторного ввода данных | `SignedLaunchUsesLinkedAccountAndUnlinkRevokesMiniAppButKeepsWebsiteSession`; `ValidUnlinkedLaunchDoesNotRegisterAccountAndMissingTokenDisablesLogin` |
| Подмена, другой бот, возраст, дубли полей, неверный JSON/ID/hash и блокировка не дают сессию | `InvalidLaunchCannotOpenSession`; `ValidTimestampAtAcceptedBoundaryReachesLinkLookup`; `BlockedLinkedAccountCannotSignIn` |
| Отвязка отзывает access/refresh Mini App, сохраняя обычную сессию сайта | `SignedLaunchUsesLinkedAccountAndUnlinkRevokesMiniAppButKeepsWebsiteSession` |

Браузерная проверка production-сборки выполнена в headless Edge на ширине 390px с подставными HTTP-ответами и launch-контекстом Telegram: вход рейтинга без формы, изоляция от другого браузерного аккаунта, переходы по сайту без нового входа, шесть видов упражнений, повтор сохранённого ответа после перезагрузки и обрыва, три шага босса, экран непривязанного аккаунта. Горизонтального скролла нет. Результаты и скриншоты находятся в игнорируемом `.data/telegram-results/` корня репозитория. Убрана зависимость загрузки React-приложения от доступности Google Fonts.

Ограничения: настоящий Telegram/WebView, Main Mini App в BotFather и серверный HTTPS-домен не проверялись. В тестах БД — SQLite; применение миграций и конкурирующие запросы на промышленном PostgreSQL этим запуском не подтверждены. NU1900 сообщает недоступность NuGet для проверки уязвимостей, но сборка и тесты завершились успешно.


## Автоматическая подготовка озвучки уровней — 14 ноября 2026

Из `src/main-api` выполнено: `dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-restore --filter 'FullyQualifiedName~LevelAudioTests|FullyQualifiedName~LevelFlowTests|FullyQualifiedName~ExerciseWorkflowTests|FullyQualifiedName~MergedPlayerLevelsTests' --verbosity minimal`. Результат: **25 пройдено, 0 ошибок, 2 пропущено**; код выхода 0. Пропущены две проверки миграций на настоящем PostgreSQL без тестового подключения. Шесть новых случаев `LevelAudioTests` прошли. Сборка сообщает NU1900: источник NuGet недоступен для проверки уязвимостей; компиляция и запуск тестов завершились успешно.

| Requirement | Evidence |
| --- | --- |
| Диктант и перевод на слух автоматически получают озвучку без ручного POST; повторные вопросы и уровни используют кеш | `LevelAutomaticallyPreparesAudioAndReusesItAcrossQuestionsAndLevels` — оба вида |
| Озвучиваются только выбранные вопросы, а не весь корпус | `LevelPreparesOnlySelectedAudioInsteadOfTheWholeCorpus` |
| Ошибки синтеза и загрузки не блокируют текстовые упражнения; следующий уровень повторяет подготовку | `AudioFailureFallsBackToTextAndNextLevelRetriesPreparation` — оба сбоя |
| Если доступны только аудиоупражнения, сбой не сохраняет пустой уровень и не меняет его номер; повтор может завершиться успешно | `FailedAudioOnlyLevelStaysEmptyAndCanBeRetriedWithoutAdvancingProgress` |

Синтезатор и объектное хранилище в новых проверках подменены. Реальный синтез Silero, сетевой доступ к Garage и развёртывание в рамках этого изменения не проверялись. Ниже сохранены предыдущие результаты.

## Слияние уровней и экономики игрока — 14 ноября 2026

Текущий итог main-api: **113 пройдено, 0 ошибок, 1 пропущен** (настоящий Silero без настроенной модели). PostgreSQL-проверки включены на отдельном временном кластере `127.0.0.1:55436`; каждая создаёт и удаляет только свою тестовую базу. LMS: **5 пройдено, 0 ошибок, 0 пропусков**. TRX `merge-final.trx` проверен: total=114, executed=passed=113, failed=0; `merge-lms.trx`: passed=5, failed=0. Ниже сохранены исторические запуски.

Сборка `dotnet build BashkirSchool.slnx --no-restore --no-incremental -v:q`: 0 предупреждений/ошибок. Полный запуск из `src/main-api`: `dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-build --logger 'trx;LogFileName=merge-final.trx' --results-directory ../../.data/merge-results -v:q`, с `BASHKIR_TEST_POSTGRES` на тестовый кластер и `Logging__EventLog__LogLevel__Default=None`. LMS из корня: `dotnet test src/lms-api.tests/LmsApi.Tests.csproj --no-restore --logger 'trx;LogFileName=merge-lms.trx' --results-directory .data/merge-results -v:q`.

| Requirement | Evidence |
| --- | --- |
| Последняя миграция содержит общую модель экономики и уровней, без дневных сущностей и повторного создания таблиц | `LatestMigrationIncludesEconomyAndLevelsWithoutResurrectingDailyEntities` |
| Обновление версии экономики сохраняет баланс и добавляет уровни — настоящий PostgreSQL | `EconomyDeploymentUpgradesToMergedLevelsWithoutLosingBalance` |
| Версия уровней применяет ранее отсутствовавшую миграцию экономики, сохраняя прогресс — настоящий PostgreSQL | `LevelDeploymentAppliesEarlierEconomyMigrationWithoutLosingProgress` |
| Маршруты игрока используют боссов за пять уровней; три ответа дают однократную награду | `PlayerBossRoutesUseFiveLevelCreditsAndRewardThreeAnswersExactlyOnce` |
| Рейтинг считает правильные учебные вопросы, исключая ошибки и повторы HTTP | `LeaderboardCountsCorrectLevelQuestionsAndIgnoresFailedAndReplayedAttempts` |

`MergePlayerEconomyAndLevels` фиксирует объединённый TargetModel без SQL-изменений таблиц. Исправлена историческая подготовка `MigrationPreservesAccountsAndEnforcesOnePendingInvitationPerClass`: до обновления создаются только существующие на тот момент поля аккаунта; все прежние проверки сохранены. Этот сценарий, архив дневных уроков и остальные PostgreSQL-проверки прошли. OpenAPI повторно получен с локального Host и содержит `/learning/*`, профиль игрока и актуальные DTO без дневных маршрутов.

## Удаление дневного сценария — 14 ноября 2026

Текущий итог: **94 пройдено, 0 ошибок, 9 пропущено**. `retirement-final.trx` проверен отдельно: total=103, executed=passed=94, failed=0, девять `NotExecuted` — 8 проверок PostgreSQL без настроенного сервера и 1 настоящий Silero без модели/Python. Ниже сохранены исторические результаты до очистки.

Команды из `src/main-api`: `dotnet build BashkirSchool.slnx --no-restore --no-incremental -v:q` (0 предупреждений/ошибок); `dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-restore --logger 'trx;LogFileName=retirement-final.trx' --results-directory ../../.data/level-results -v:q`.

| Requirement | Evidence |
| --- | --- |
| Дневные маршруты и DTO удалены; через прежние ответы нельзя обойти текущий уровень | `OnlyLevelLearningRoutesRemainAndOldRoutesCannotBypassProgress` |
| Дневные сущности удалены из модели, миграция не удаляет таблицы/данные | `RetiredEntitiesLeaveTheRuntimeModelWithoutDroppingHistoricalTables` |
| Проверка словарных упражнений перенесена на текущий уровень | `WordPairsAreImportedIdempotentlyIssuedPrivatelyAndAnsweredOnlyByOwner` |
| Пропуск слова и сборка предложения сохраняют условия и проверяются в уровне | `GapAndOrderSnapshotsSurviveRepeatedRequestsAndValidateAnswers` — оба вида |
| Диктант и перевод аудио используют готовую запись и сохраняют ключ при смене решения | `AudioIsLazyDeduplicatedAndTranslationWaitsForAnAcceptedRendering` — оба вида |
| Владение и общий прогресс сайта/бота проверяются через новые маршруты | `LevelAnswersBelongToLinkedStudentAndProgressPersistsAcrossWebsiteAndBot` |

Прежние сценарии `DailyBossTests` удалены как проверки отменённого поведения; актуальное прохождение боссов покрывает `LevelFlowTests`. Проверка архива `RetirementPreservesDailyLearningArchiveWithoutRuntimeEntities` требует PostgreSQL и в текущем запуске пропущена. Схема миграции сверена с моделью и сгенерированным SQL; применение на настоящей БД в этом запуске не проверено. Сохранённый OpenAPI заново получен с локального Host: дневные пути/DTO отсутствуют, `/learning/*` присутствуют.

## Историческая проверка уровней до очистки — 14 ноября 2026

Итог текущего запуска: **95 пройдено, 0 ошибок, 9 пропущено** (8 проверок настоящего PostgreSQL без `BASHKIR_TEST_POSTGRES`, 1 проверка настоящего Silero без модели/Python). Новые `LevelFlowTests`: **9 пройдено, 0 ошибок, 0 пропусков**. TRX `levels-final.trx` разобран отдельно: total=104, executed=passed=95, failed=0; девять результатов `NotExecuted` соответствуют перечисленным внешним проверкам. Приведённые ниже более ранние запуски являются историческими.

Команды из `src/main-api`:

```powershell
dotnet build BashkirSchool.slnx --no-restore --no-incremental -v:q
dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-build --logger 'trx;LogFileName=levels-final.trx' --results-directory ../../.data/level-results -v:q
```

Сборка: 0 предупреждений, 0 ошибок. OpenAPI заново получен с локально запущенного Host без подключения к БД; наличие новых публичных и внутренних маршрутов проверено. Миграция проверена по модели и сгенерированному SQL; её применение и параллельные запросы на настоящем PostgreSQL в этом запуске не проверялись. Сайт и реализация Telegram-бота не менялись.

| Requirement | Evidence |
| --- | --- |
| 3–6 вопросов, ошибки в конце очереди, завершение только после всех правильных, статистика и повтор HTTP | `MistakesMoveToTailUntilEveryQuestionIsCorrectAndRetriesAreIdempotent` |
| Только текущий вопрос, владение, сохранение между датами и каналами, независимый прогресс | `CurrentQuestionIsPrivateOwnedPersistentAndIndependentOfOtherPlayers` |
| Учёба только с Community/Expert; Pending возвращается в проверки, выданный учебный ключ сохраняется | `EmptyAndUnresolvedCorpusCannotCreateLearningButCommunityAndExpertCan` |
| До пятого уровня босса нет; после пяти — три разных реальных проверки, откладывание и накопление | `EveryFiveLevelsEarnAnOptionalBossWithThreeDistinctRealReviews` |
| Недостаток материала, пропуск и истечение не завершают босса | `BossResumesWhenMaterialArrivesAndSkipsDoNotCompleteIt` |
| Ввод первого перевода, восстановление по сохранённому ответу другого канала, разные фрагменты даже после интервала повтора | `BossCreationAnswersReconcileAcrossChannelsAndNeverRepeatACompletedFragment` |
| Несколько исторических активных выдач при возврате в Pending не вызывают ошибку или новую выдачу | `RegainedPendingStateWithAnotherLiveAssignmentResumesWithoutDuplicateIssuance` |
| Неверный JSON/токен не меняет прогресс; HTTP-схема и авторизация отражены в OpenAPI | `InvalidAnswersCannotAdvanceProgressAndLevelContractsDescribeActualRoutes` |
| Модель соответствует миграции, новые таблицы и ограничения добавляются без удаления старых данных | `LevelMigrationMatchesModelAndKeepsLegacyLearningData` |

Статическая проверка тестов: ожидаемые переводы заданы независимо от генератора ключей; проверяются конкретные номера, идентификаторы, счётчики и записи голосов, а также запреты доступа и отсутствие приватных ключей. Реальные мутации исходников и сбор покрытия не выполнялись.

## Историческая проверка первого MVP

14 ноября 2026. Итог: **51 тест пройден, 0 ошибок, 0 пропусков**. TRX проверен отдельно: total=executed=passed=51, failed=notExecuted=0.

Команда: `dotnet test BashkirSchool.slnx --no-restore --logger "trx;LogFileName=core.trx" --results-directory TestResults`. Для PostgreSQL задан `BASHKIR_TEST_POSTGRES` на переносимый PostgreSQL 18.7, localhost:55432, отдельный тестовый кластер. Служба/Compose/Aspire не используются.

| Requirement | Evidence |
| --- | --- |
| Регистрация без школы создаёт только собственный аккаунт/участника; доступны вход и профиль без групп | `RegistrationCreatesOnlyParticipantAndOwnAccountWithoutSchoolAndCanSignIn` |
| Слабый пароль, повтор логина и подмена привилегий не оставляют частичный аккаунт | `RegistrationRejectsWeakPasswordDuplicateLoginAndPrivilegeInjectionWithoutPartialAccount` |
| Ограничение регистрации возвращает 429 и не создаёт отклонённый аккаунт | `PublicRegistrationIsRateLimitedWithoutCreatingRejectedAccounts` |
| Самостоятельные участники входят в N/процент, но без школьного охвата результат требует эксперта | `IndependentVotesCountButCannotInventSchoolCoverageOrCommunityResult`, `IndependentChoicesContributeToPercentageButNeverToSchoolOrClassCount` |
| Общий поток школьных и самостоятельных участников даёт единый результат, доверие и урок | `MixedSchoolAndIndependentVotesShareOneResultAndKeepRealGroupSnapshots` |
| Самостоятельные принятия не разбавляют превышенную долю школы | `IndependentVotesCannotDiluteAnOverrepresentedSchool` |
| Снимок группы содержит школу и класс вместе либо два null, без пустых GUID | `GroupSnapshotIsEitherRealSchoolAndClassOrNeitherNeverAnEmptyGuid` |
| Восстановление администратором сохраняет ID, аудит причины; ссылки вращаются, прежние сессии отзываются | `AdministratorRecoveryIsAuditedRotatesLinksAndRevokesPreviousSessionsOnCompletion` |
| Учитель не восстанавливает самостоятельного участника; этот маршрут не сбрасывает школьный или привилегированный аккаунт | `IndependentRecoveryCannotResetSchoolStudentsStaffOrPromotedIndependentAccounts` |
| PostgreSQL: регистрация, nullable FK выдачи, отсутствие ложного охвата и восстановление администратора | `IndependentParticipantUsesNullGroupsAndAdministratorRecoveryOnRealPostgres` |
| Последние принятия: 85 за A / 15 за B = 85%/15%; без принятий процент null | `EightyFiveChoicesForAAndFifteenForBGiveEightyFivePercent`, `NoOpinionsHaveUndefinedSupport` |
| Новое принятие заменяет прежнее по случаю; исторические отказы не участвуют | `LatestAcceptanceReplacesPriorChoiceAndLegacyRejectionsAreIgnored` |
| Границы N и 80% включительны; кандидат не наследует чужую выборку | `ExactNAndEightyPercentAreInclusiveAndInsufficientCandidateCannotInheritSample` |
| Нужны охват и независимые от автора наблюдения | `CoverageAndAuthorIndependenceAreRequiredEvenWithUnanimousChoices` |
| Повторы не множат людей; маленькая коррекция не отменяет ещё достаточное большинство | `RepeatChoicesDoNotMultiplyPeopleAndSmallCorrectionDoesNotRemoveAcceptedMajority` |
| Импорт → выдача → принятие → статистика; повторы без дублей; историческое доверие/Outbox | `DeliveryAssignmentConfirmationAndRetryPersistOneVoteAndOutboxMessage` |
| Другой payload с тем же ключом не создаёт второй случай | `DeliveryKeyWithDifferentPayloadConflictsWithoutCreatingAnotherCase` |
| Исправление сохраняет отдельный вариант и отдельную статистику | `CorrectionKeepsSeparateVariantStatisticsAndDoesNotChangeShownCandidate` |
| Пропуск не создаёт принятие, не стирает старое; новая дневная выдача сохраняет историю | `SkipDoesNotCountAndExpiredReservationAllowsAnotherParticipant`, `NewDayAssignmentKeepsHistoryButOneUniqueOpinionAndSkipDoesNotEraseIt` |
| Чужая выдача запрещена; точная граница истечения; резервы освобождают квоту | `ParticipantCannotSubmitAnotherParticipantsAssignment`, `AssignmentRejectsOtherParticipantAndExactExpirationBoundary`, `ReservationsEnforceClassCapAndExpirationReleasesCapacity` |
| Экспертный синоним правилен для доверия и учебного ключа; повтор обработки не удваивает вклад; поздний ответ архивный | `ExpertSynonymIsCorrectReprocessingIsIdempotentAndLateFeedbackIsArchived` |
| A и предложенный B неверны: одна ошибка за B, без бонуса за A | `RejectingAAndProposingWrongBProducesOneErrorWithoutRejectionBonus` |
| Pending сохраняет прежний вклад; новое определённое решение заменяет его дельтой | `PendingRetainsTrustUntilExpertReplacesOldContributionAndCorrectionHasOnlyOneClaim` |
| Чужие/дублированные варианты и устаревшее экспертное решение отклоняются; администратор не эксперт | `ExpertRejectsForeignAndDuplicateVariantsAndAdministratorIsNotAnExpert`, `ExpertSynonymIsCorrectReprocessingIsIdempotentAndLateFeedbackIsArchived` |
| Case-области эксперта/экспорта и пагинация не раскрывают чужой случай; отзыв назначения действует сразу | `CaseScopedExpertAndExporterCannotReadOtherCaseAndRevocationTakesEffect` |
| Экспорт выдаёт основной текст, числовой confirmed и status; ученик не получает экспорт | `ExportAndLessonUseExpertRenderingWithoutLeakingKeysOrAddingTrust` |
| Дневной урок стабилен, ключ скрыт, правильная учебная попытка повторобезопасна и не меняет доверие | `ExportAndLessonUseExpertRenderingWithoutLeakingKeysOrAddingTrust` |
| Чужое упражнение запрещено, неизвестная формулировка не создаёт голос/оценку доверия | `AttemptIsOwnOnlyUnknownTextIsUnrecognizedAndDoesNotBecomeAVote` |
| Refresh вращается; повтор старого секрета отзывает сессию | `RefreshRotatesAndReplayRevokesTheSessionFamily` |
| Сессии только свои; списки классов/учеников ограничены и не содержат доверия | `SessionRevocationIsOwnOnlyAndSchoolListsAreScopedWithoutTrust` |
| Смена своего пароля проверяет прежний и отзывает access/refresh | `OwnPasswordChangeChecksOldPasswordAndRevokesAccessAndRefresh` |
| Учитель не восстанавливает чужой класс; перевыпуск ссылки/успешное использование гасят старую | `TeacherCannotResetStudentInAnotherTeachersClass`, `ReissuedPasswordLinkInvalidatesOldLinkAndPasswordChangeRevokesJwt` |
| Права разных назначений не смешиваются; блокировка отзывает JWT; гость/подмена доверия запрещены | `PermissionAndGlobalScopeCannotBeCombinedFromDifferentAssignments`, `BlockingAccountRevokesExistingJwtAndBootstrapCannotRunAgain`, `AnonymousAndForgedTrustInputsAreRejected` |
| PostgreSQL: пустая база мигрируется/bootstrap; ссылки переживают restart; без bootstrap-секретов запуск пустой базы запрещён | `EmptyDatabaseMigratesBootstrapsAndKeepsPasswordTokenAcrossRestart`, `EmptyDatabaseWithoutBootstrapSecretsRefusesStartupWithoutCreatingAccounts` |
| PostgreSQL: обновление старой базы сохраняет администратора и добавляет собственные права сессий | `UpgradePreservesExistingAdministratorAndBackfillsOwnSessionPermission` |
| PostgreSQL: настоящий BackgroundService обрабатывает два случая без повторного начисления | `BackgroundWorkerProcessesTwoCasesAndTrustContributionsAreNotDuplicated` |
| PostgreSQL: параллельные случаи/повторные события сохраняют итог доверия одного человека | `ParallelCasesAndRepeatedProcessingPreserveOneParticipantsTrustTotals` |
| Обёрнутый 40001 классифицируется как конфликт; уникальность не вызывает слепой retry | `WrappedSerializationFailureIsAConflictButDuplicateKeyIsNotAutomaticallyRetried` |
| Дробные типы PostgreSQL и уникальность; отсутствие обязательной конфигурации | `PostgresSchemaPreservesFractionalTrustAndPolicyShares`, `MissingConnectionStringFailsBeforeResolvingDatabase`, `MissingJwtSecretFailsBeforeDatabaseInitialization` |
| SQLite-тесты явно отключают инициализацию | `ExplicitlyDisabledInitializationDoesNotResolveOrMigrateDatabase` |
| OpenAPI описывает bearer и фактические DTO/статусы | `OpenApiDocumentsBearerSecurityAndActualIntakeAndAssignmentResponses` |
| Модель соответствует миграциям | `dotnet ef migrations has-pending-model-changes --project src/BashkirSchool.Infrastructure --startup-project src/BashkirSchool.Host --no-build` — изменений нет |
| Release и обычный автономный запуск | `dotnet publish ... --configuration Release --output .data/publish /p:UseAppHost=false`; отдельный Release Host сам создал схему/администратора, `/health/ready` → ready |
| Артефакт OpenAPI | `docs/openapi.json`: OpenAPI 3.1.1, 32 пути, bearer; получен с работающего Release Host |

При проверке найдена реальная обёртка Npgsql InvalidOperationException → PostgresException(40001). Обработка конфликтов исправлена, параллельный сценарий повторно прошёл. Проверены конкретные счётчики/редакции, ошибки доступа, отсутствие лишних записей и приватных полей; нет подмены аутентификации. Реальные мутации исходников и измерение процента покрытия не выполнялись.

Границы: Docker Engine отсутствует, именно docker build/запуск Linux-образа ещё не выполнялись. Нет нагрузочного теста, браузерного cookie-refresh-протокола и проверки истечения токенов на реально прошедшем времени. Расширенные игровые механики и экономические правила не входят в этот этап. SQLite и PostgreSQL результаты не подменяют друг друга; PostgreSQL-сценарии отдельно используют настоящие миграции/блокировки.

## Расширяемые задания — 14 ноября 2026

Проверена текущая ветка расширения: полная сборка .NET 10 без инкрементальности — 0 предупреждений/ошибок. Полный запуск xUnit: **71 пройден, 1 пропущен, 0 ошибок**, включая настоящий PostgreSQL на отдельном временном кластере. Пропущен только `RealSileroSynthesizesBashkirWavAndReusesTheCachedModel`: официальный models.silero.ai не отвечает из этого окружения. Попытка реального запуска отдельно завершилась сетевым тайм-аутом до загрузки весов; это не успешная проверка синтеза. Найденный официальный Windows-установщик содержит nostress и не использовался как подмена v5_cis_base.

Команда из `src/main-api`: `dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-build '-bl:../../.data/exercises-final-tests-{}.binlog' --logger 'trx;LogFileName=exercises-final.trx' --results-directory ../../.data/exercise-results`. Для PostgreSQL задан `BASHKIR_TEST_POSTGRES`; Windows EventLog отключён переменной `Logging__EventLog__LogLevel__Default=None`, поскольку учётная запись песочницы не может писать в системный журнал.

| Requirement | Evidence |
| --- | --- |
| Пять пар, однозначность, проверка каждого ID | `WordMatchRequiresFiveUnambiguousPairsAndChecksBijection`, `WordPairsAreImportedIdempotentlyIssuedPrivatelyAndAnsweredOnlyByOwner` |
| До четырёх популярных переводов, снимок и ровно один голос | `ReviewChoicesAreTopFourSnapshotAndSelectedVariantGetsTheOnlyVote` |
| Один доступный вариант либо свой перевод | `SingleChoiceAndCustomTranslationRemainOneAcceptance`; прежний режим — `DeliveryAssignmentConfirmationAndRetryPersistOneVoteAndOutboxMessage` |
| Диктант без перевода, исходник скрыт | `DictationUsesOriginalWithoutTranslationAndHidesItFromPrompt` |
| Перевод на слух только с принятым переводом | `AudioTranslationRequiresAudioAndAcceptedResolutionAndAcceptsSynonym` |
| Создание аудио по запросу, повторное использование и отсутствие готового asset при сбое upload | `AudioIsLazyDeduplicatedAndTranslationWaitsForAnAcceptedRendering` — синтезатор/хранилище в этом тесте подменены |
| Пропущенное слово: выбор/текст, подмена ID запрещена | `MissingWordAcceptsChoiceOrTextAndRejectsForeignOrMixedInput`, `GapAndOrderSnapshotsSurviveRepeatedRequestsAndValidateAnswers` |
| Сборка предложения, повторяющиеся слова и уникальность вхождений | `SentenceOrderChecksAllTokensAndAllowsInterchangeableRepeatedWords` |
| Новый строковый вид и версии без изменения общего процесса | `RegistryAcceptsNewKindAndVersionsWithoutWorkflowChanges` |
| Неправильный JSON, подмена результата и пустые ответы | `TextAnswerRejectsMalformedOrForgedFields` |
| Миграция сохраняет старые текстовые упражнения, допускает задание без предложения | `UpgradePreservesLegacyExerciseAndAllowsVocabularyWithoutASentence` — настоящий PostgreSQL |
| Подписанная MinIO-ссылка приватного bucket | `MinioLinkIsSignedForPrivateBucketWithoutConnectingToStorage` — проверка подписи/URL, без сетевого MinIO |

`dotnet-ef migrations has-pending-model-changes --project src/BashkirSchool.Infrastructure --startup-project src/BashkirSchool.Host --no-build`: модель соответствует миграциям. Обновлён живой OpenAPI-артефакт; договор новых условий/ответов — [EXERCISE-API.md](EXERCISE-API.md).

Пределы: настоящий синтез точной v5_cis_base, сетевой upload в MinIO и Docker build пока не подтверждены. Адрес и секреты MinIO в Coolify не предоставлены; добавлены конфигурация, SDK-адаптер и контейнерные зависимости. Реестр и проверка аудиоупражнений проверены отдельно от этих внешних зависимостей.
# Проверка приглашений в класс — 14 ноября 2026

`dotnet test tests/BashkirSchool.Tests/BashkirSchool.Tests.csproj --no-restore --logger "trx;LogFileName=invitations-regression.trx" --results-directory ../../.data/invitation-results` из `src/main-api`: 80 пройдено, 1 пропущен (реальный Silero, требуется отдельное окружение), ошибок нет. PostgreSQL-тесты включены через `BASHKIR_TEST_POSTGRES`, отдельный локальный тестовый кластер на порту 55435. `dotnet build src/main-api/BashkirSchool.slnx --no-restore --no-incremental -v:q`: 0 предупреждений и ошибок. EF `has-pending-model-changes`: расхождений нет. OpenAPI выгружен из запущенного Host.

| Требование | Проверка в `ClassInvitationTests` |
| --- | --- |
| Членство только после согласия, повтор без дубля, отмена остальных приглашений | `AcceptanceEnrollsOnlyAfterConsentAndCancelsOtherInvitations` |
| Отказ сохраняет самостоятельный статус, новое приглашение после отказа | `DeclineLeavesParticipantIndependentAndAllowsANewInvitation` |
| Только собственный класс учителя и собственное приглашение ученика | `OtherUsersCannotInviteReadOrAcceptOutsideTheirScope` |
| Перед подтверждением повторно проверяются права и состояние сторон | `AcceptanceRechecksTeacherPermissionAndParticipantPrivileges` |
| Обновление PostgreSQL сохраняет данные, индекс запрещает дубли ожидающих приглашений | `MigrationPreservesAccountsAndEnforcesOnePendingInvitationPerClass` |

