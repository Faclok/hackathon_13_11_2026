// CSV records can contain quoted commas, quotes and line breaks.
function records(text) {
  const result = [];
  let cells = [], cell = '', quoted = false, closed = false, line = 1, start = 1;
  const endCell = () => { cells.push(cell); cell = ''; closed = false; };
  const endRecord = () => {
    endCell();
    if (cells.some(value => value.trim())) result.push({ cells, line: start });
    cells = [];
  };
  text = text.replace(/^\uFEFF/, '');
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') { cell += '"'; i++; }
        else { quoted = false; closed = true; }
      } else { cell += c; if (c === '\n') line++; }
    } else if (c === ',') endCell();
    else if (c === '\r' || c === '\n') {
      endRecord();
      if (c === '\r' && text[i + 1] === '\n') i++;
      line++; start = line;
    } else if (c === '"' && !cell && !closed) quoted = true;
    else {
      if (closed || c === '"') throw new Error(`Строка ${line}: неверные кавычки в CSV.`);
      cell += c;
    }
  }
  if (quoted) throw new Error(`Строка ${start}: незакрытые кавычки в CSV.`);
  endRecord();
  return result;
}

export function prepareCsv(text) {
  const [header, ...rows] = records(text);
  if (!header) throw new Error('Файл пуст.');
  const names = header.cells.map(value => value.trim());
  if (new Set(names).size !== names.length) throw new Error('В заголовке повторяются названия колонок.');
  const required = ['fragment_id', 'text_ba', 'text_ru', 'source_dataset'];
  const missing = required.filter(name => !names.includes(name));
  if (missing.length) throw new Error(`Нет обязательных колонок: ${missing.join(', ')}.`);
  if (!rows.length) throw new Error('В файле нет данных.');
  if (rows.length > 20000) throw new Error('За один раз можно загрузить не более 20 000 записей.');
  const items = [], errors = [], seen = new Set();
  let repeatedPairs = 0;
  const pairs = new Set();
  for (const row of rows) {
    const fail = message => errors.push(`Строка ${row.line}: ${message}`);
    if (row.cells.length !== names.length) { fail('число значений не совпадает с заголовком.'); continue; }
    const data = Object.fromEntries(names.map((name, i) => [name, row.cells[i]]));
    if (required.some(name => !data[name].trim())) { fail('не заполнены обязательные поля.'); continue; }
    if (data.fragment_id.length > 200 || data.source_dataset.length > 200 || data.text_ba.length > 10000 || data.text_ru.length > 10000) {
      fail('ID/источник длиннее 200 символов или текст длиннее 10 000 символов.'); continue;
    }
    if (required.some(name => data[name].includes('\uFFFD'))) { fail('повреждённая кодировка текста; сохраните файл в UTF-8.'); continue; }
    // Keep supplier IDs unchanged; the core enforces uniqueness per producer and direction.
    if (seen.has(data.fragment_id)) { fail(`повторяется fragment_id «${data.fragment_id}».`); continue; }
    seen.add(data.fragment_id);
    const pair = JSON.stringify([data.text_ba, data.text_ru]);
    if (pairs.has(pair)) repeatedPairs++;
    pairs.add(pair);
    const source = {
      document_id: data.source_dataset,
      title: data.attribution || data.source_dataset,
      // Preserve the supplier's additional columns without interpreting quality as approval.
      chapter: JSON.stringify(Object.fromEntries(Object.entries(data).filter(([name]) => !['text_ba', 'text_ru', 'fragment_id', 'attribution'].includes(name))))
    };
    if (JSON.stringify(source).length > 9000) { fail('слишком много сведений об источнике.'); continue; }
    items.push({ line: row.line, request: { fragment_id: data.fragment_id, text_ba: data.text_ba, lang_pair: 'ba-ru', translation: data.text_ru, source } });
  }
  return { items, errors, total: rows.length, repeatedPairs };
}

export async function deliveryKey(request) {
  const bytes = new TextEncoder().encode(JSON.stringify([request.source.document_id, request.fragment_id, request.lang_pair]));
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return `csv-v1-${Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('')}`;
}
