import test from 'node:test';
import assert from 'node:assert/strict';
import { prepareVocabularyCsv } from './vocabulary-import.js';

test('supports the supplied two-column format, BOM and Bashkir letters', () => {
  const result = prepareVocabularyCsv('\uFEFFtext_ba,text_ru\r\nбер,один\r\nике,два\r\nөс,три\r\nдүрт,четыре\r\nбиш,пять\r\n');
  assert.equal(result.hasHeader, true);
  assert.equal(result.delimiter, ',');
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.items.map(({ word, translation }) => [word, translation]), [
    ['бер', 'один'], ['ике', 'два'], ['өс', 'три'], ['дүрт', 'четыре'], ['биш', 'пять']
  ]);
  assert.equal(result.items[0].line, 2);
});

test('canonical headers and Excel semicolons are accepted', () => {
  const result = prepareVocabularyCsv('\n"word";"translation"\r\n"Ҡояш";"Солнце"\r\n');
  assert.deepEqual(result.errors, []);
  assert.equal(result.hasHeader, true);
  assert.equal(result.delimiter, ';');
  assert.equal(result.items[0].word, 'Ҡояш');
  assert.equal(result.items[0].line, 3);
});

test('headerless input preserves the first pair and quoted separators', () => {
  const result = prepareVocabularyCsv('бер,один\n"а;б","в;г"\n');
  assert.equal(result.hasHeader, false);
  assert.equal(result.items.length, 2);
  assert.equal(result.items[0].word, 'бер');
  assert.equal(result.items[1].translation, 'в;г');
});

test('identical pairs are deduplicated while another translation is retained', () => {
  const result = prepareVocabularyCsv('word,translation\nбер,один\nбер,один\nбер,единственный\n');
  assert.equal(result.total, 3);
  assert.equal(result.duplicates, 1);
  assert.equal(result.items.length, 2);
  assert.deepEqual(result.errors, []);
});

test('the entire file is checked, including invalid rows after a full batch', () => {
  const valid = Array.from({ length: 501 }, (_, i) => `слово${i},перевод${i}`).join('\n');
  const result = prepareVocabularyCsv('word,translation\n' + valid + '\nпара,два слова\nпусто,\nа,б,в\n' + 'а'.repeat(201) + ',б\nа,\uFFFD\n');
  assert.equal(result.items.length, 501);
  assert.equal(result.errors.length, 5);
  assert.match(result.errors[0], /Строка 503:.*без пробелов/);
  assert.match(result.errors[1], /заполните/);
  assert.match(result.errors[2], /два столбца/);
  assert.match(result.errors[3], /200 символов/);
  assert.match(result.errors[4], /кодировка/);
});

test('quoted newlines are invalid words and line numbers remain useful', () => {
  const result = prepareVocabularyCsv('word,translation\n"два\nслова",перевод\nбер,один\n');
  assert.equal(result.errors.length, 1);
  assert.match(result.errors[0], /Строка 2/);
  assert.equal(result.items[0].line, 4);
});

test('malformed, reversed and empty files are rejected', () => {
  assert.throws(() => prepareVocabularyCsv(''), /пуст/);
  assert.throws(() => prepareVocabularyCsv('word,translation'), /нет пар/);
  assert.throws(() => prepareVocabularyCsv('translation,word\nодин,бер'), /Поменяйте столбцы/);
  assert.throws(() => prepareVocabularyCsv('text_ru,text_ba\nодин,бер'), /Поменяйте столбцы/);
  assert.throws(() => prepareVocabularyCsv('word,translation\n"abc,b'), /кавычки/);
  const tooMany = 'word,translation\n' + 'бер,один\n'.repeat(20001);
  assert.throws(() => prepareVocabularyCsv(tooMany), /20 000/);
});
