# Проверка школьного MVP ядра

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

