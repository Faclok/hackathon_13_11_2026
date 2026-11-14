import { parseCsvRecords } from './csv-import.js';

export const VOCABULARY_BATCH_SIZE = 500;

// Temporary two-column import. It uses the existing vocabulary endpoint only.
export function prepareVocabularyCsv(text) {
  // Detect the separator in the first logical record, ignoring quoted values.
  let quoted = false, commas = 0, semicolons = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"') {
      if (quoted && text[i + 1] === '"') i++;
      else quoted = !quoted;
    } else if (!quoted) {
      if (c === ',') commas++;
      if (c === ';') semicolons++;
      if ((c === '\n' || c === '\r') && (commas || semicolons)) break;
    }
  }
  const delimiter = semicolons > commas ? ';' : ',';
  const rows = parseCsvRecords(text, delimiter);
  if (!rows.length) throw new Error('Файл пуст.');
  const first = rows[0].cells.map(value => value.trim().toLowerCase());
  if (first.length === 2 && (
    first[0] === 'translation' && first[1] === 'word' ||
    first[0] === 'text_ru' && first[1] === 'text_ba'
  )) throw new Error('Первый столбец должен содержать башкирские слова, второй — русские переводы. Поменяйте столбцы местами.');
  const hasHeader = first.length === 2 && (
    first[0] === 'word' && first[1] === 'translation' ||
    first[0] === 'text_ba' && first[1] === 'text_ru'
  );
  const data = hasHeader ? rows.slice(1) : rows;
  if (!data.length) throw new Error('В файле нет пар слов.');
  if (data.length > 20000) throw new Error('За один раз можно загрузить не более 20 000 пар.');

  const items = [], errors = [], seen = new Set();
  let duplicates = 0;
  for (const row of data) {
    const fail = message => errors.push(`Строка ${row.line}: ${message}`);
    if (row.cells.length !== 2) { fail('нужно ровно два столбца: слово и перевод.'); continue; }
    const [word, translation] = row.cells.map(value => value.trim());
    if (!word || !translation) { fail('заполните слово и перевод.'); continue; }
    if (word.length > 200 || translation.length > 200) { fail('слово и перевод должны быть не длиннее 200 символов.'); continue; }
    if (/[\s\u0085]/u.test(word) || /[\s\u0085]/u.test(translation)) { fail('в каждой ячейке должно быть одно слово без пробелов и переносов строк.'); continue; }
    if (word.includes('\uFFFD') || translation.includes('\uFFFD')) { fail('повреждённая кодировка; сохраните CSV в UTF-8.'); continue; }
    const key = JSON.stringify([word, translation]);
    if (seen.has(key)) { duplicates++; continue; }
    seen.add(key);
    items.push({ line: row.line, word, translation });
  }
  return { items, errors, total: data.length, duplicates, hasHeader, delimiter };
}
