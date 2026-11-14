# Примеры ответов main-api

Идентификаторы вымышлены. [current-level.json](current-level.json) — ответ `POST /api/learning/current`: только текущий уровень и один вопрос. [review-choice.json](review-choice.json) и [review-confirm.json](review-confirm.json) — самостоятельные проверочные выдачи; мнения по ним участвуют в согласовании, обычные учебные ответы — нет.

Компонент выбирается по `question.kind` и `schemaVersion`. Для неподдерживаемой версии нельзя автоматически отправлять ответ.

## Отправка учебного ответа

`POST /api/learning/levels/{levelId}/questions/{questionId}/answer`:

```json
{
  "attemptToken": "00000000-0000-4000-8000-000000000102",
  "answer": { "text": "Здравствуй!" }
}
```

Ответ содержит `attemptToken`, `recognized` и `levelCompleted`. Затем клиент запрашивает `/api/learning/current`: ошибка появится в конце очереди, а следующий уровень — только после всех правильных ответов. Для новой попытки используется новый токен. Другие виды объектов `answer` перечислены в [договоре заданий](../EXERCISE-API.md).

## Отправка мнения о переводе

POST /api/reviews/{id}/response:

- Выбор из review-choice: {"action":"Confirm","selectedVariantId":"00000000-0000-4000-8000-000000000311"}.
- Подтверждение review-confirm: {"action":"Confirm"}.
- Свой текст в любом из двух режимов: {"action":"Correct","correction":"Свой перевод"}.
- Пропуск: {"action":"Skip"}.

Ответ — {"responseId":"идентификатор ответа","archived":false}, без учебного recognized.

Полный договор: [EXERCISE-API.md](../EXERCISE-API.md).
