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
