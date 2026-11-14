# Примеры данных для сайта

Это демонстрационные ответы по текущим контрактам main-api. Идентификаторы вымышлены, адреса example.invalid не предназначены для проигрывания. В настоящем ответе audioUrl — подписанная ссылка MinIO. Пример урока показывает все шесть учебных видов вместе; конкретный состав зависит от доступных материалов и настроек.

- [daily-lesson.json](daily-lesson.json): ответ GET /api/lessons/today.
- [review-choice.json](review-choice.json): ответ POST /api/reviews/next?kind=translation-choice.
- [review-confirm.json](review-confirm.json): ответ POST /api/reviews/next?kind=translation-confirm.

Сейчас два режима проверки переводов приходят отдельно от exercises дневного урока. Для единого экрана сайт может показать их рядом с учебными упражнениями, сохраняя исходный тип и маршрут отправки ответа. Один общий серверный список со всеми семью механиками пока не реализован. Две незавершённые проверки одновременно также не выдаются: сервер возвращает активную проверку независимо от запрошенного режима.

Сайт выбирает компонент по kind и поддерживаемой schemaVersion, а не по индексу задания в массиве. Неизвестный вид или версию следует показать как неподдерживаемые; не отправлять за них автоматический ответ.

## Отправка учебного ответа

POST /api/exercises/{id}/answer, Content-Type: application/json:

| kind | Тело запроса |
| --- | --- |
| word-match | {"answer":{"pairs":[{"leftId":"l1","rightId":"r1"},{"leftId":"l2","rightId":"r2"},{"leftId":"l3","rightId":"r3"},{"leftId":"l4","rightId":"r4"},{"leftId":"l5","rightId":"r5"}]}} |
| translation-text | {"answer":{"text":"Здравствуй!"}} |
| audio-dictation | {"answer":{"text":"Услышанный текст"}} |
| audio-translation | {"answer":{"text":"Перевод услышанного"}} |
| missing-word | {"answer":{"optionId":"g2"}} или {"answer":{"text":"мәктәпкә"}} |
| sentence-order | {"answer":{"tokenIds":["t1","t2","t3"]}} |

Ответ сервера: {"id":"идентификатор попытки","recognized":true}. После сохранения обновите урок или локальное completed. Проверку правильности выполняет сервер.

## Отправка мнения о переводе

POST /api/reviews/{id}/response:

- Выбор из review-choice: {"action":"Confirm","selectedVariantId":"00000000-0000-4000-8000-000000000311"}.
- Подтверждение review-confirm: {"action":"Confirm"}.
- Свой текст в любом из двух режимов: {"action":"Correct","correction":"Свой перевод"}.
- Пропуск: {"action":"Skip"}.

Ответ — {"responseId":"идентификатор ответа","archived":false}, без учебного recognized.

Полный договор: [EXERCISE-API.md](../EXERCISE-API.md).
