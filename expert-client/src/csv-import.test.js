import test from 'node:test';
import assert from 'node:assert/strict';
import { deliveryKey, prepareCsv } from './csv-import.js';

const header = 'fragment_id,text_ba,text_ru,source_dataset';

test('quoted_csv_and_metadata', () => {
  const result = prepareCsv('\uFEFF' + header + ',quality_score,license\r\nf1,"Һаумы,\r\nдуҫ!","Он сказал ""привет""",books,1.0,CC-BY-4.0\r\n');
  assert.deepEqual(result.errors, []);
  assert.equal(result.items.length, 1);
  assert.deepEqual(result.items[0].request, {
    fragment_id: 'f1', text_ba: 'Һаумы,\r\nдуҫ!', translation: 'Он сказал "привет"', lang_pair: 'ba-ru',
    source: { document_id: 'books', title: 'books', chapter: '{"source_dataset":"books","quality_score":"1.0","license":"CC-BY-4.0"}' }
  });
});

test('duplicate_ids_and_missing_text', () => {
  const result = prepareCsv(header + '\nf1,Һаумы,Привет,books\nf2,Һаумы,Привет,books\nf1,Сәләм,Здравствуй,books\nf3,,Пусто,books');
  assert.equal(result.total, 4);
  assert.deepEqual(result.items.map(item => item.request.fragment_id), ['f1', 'f2']);
  assert.equal(result.repeatedPairs, 1);
  assert.equal(result.errors.length, 2);
  assert.match(result.errors[0], /Строка 4: повторяется fragment_id/);
  assert.match(result.errors[1], /Строка 5: не заполнены/);
});

test('malformed_csv', () => {
  for (const [input, message] of [
    ['', /Файл пуст/], [header, /нет данных/],
    ['fragment_id,text_ba,text_ru\nf1,a,b', /source_dataset/],
    [header + ',text_ba\nf1,a,b,books,c', /повторяются названия/],
    [header + '\nf1,"abc,b,books', /незакрытые кавычки/],
    [header + '\nf1,"a"x,b,books', /неверные кавычки/]
  ]) assert.throws(() => prepareCsv(input), message);
  const result = prepareCsv(header + '\nf1,a,b\nf2,�,b,books');
  assert.equal(result.items.length, 0);
  assert.match(result.errors[0], /число значений/);
  assert.match(result.errors[1], /кодировка/);
});

test('limits_and_blank_lines', () => {
  const result = prepareCsv('\n' + header + '\n\nf1,' + 'а'.repeat(10000) + ',b,books\nf2,' + 'а'.repeat(10001) + ',b,books\n');
  assert.equal(result.total, 2);
  assert.equal(result.items.length, 1);
  assert.equal(result.items[0].line, 4);
  assert.match(result.errors[0], /длиннее/);
  assert.throws(() => prepareCsv(header + '\n' + Array.from({ length: 20001 }, (_, i) => `${i},a,b,books`).join('\n')), /20 000/);
});

test('stable_delivery_keys', async () => {
  const request = prepareCsv(header + '\nf1,Һаумы,Привет,books').items[0].request;
  const key = await deliveryKey(request);
  assert.match(key, /^csv-v1-[a-f0-9]{64}$/);
  assert.equal(await deliveryKey({ ...request, translation: 'Исправлено' }), key);
  assert.notEqual(await deliveryKey({ ...request, fragment_id: 'f2' }), key);
  assert.notEqual(await deliveryKey({ ...request, source: { ...request.source, document_id: 'other' } }), key);
});
