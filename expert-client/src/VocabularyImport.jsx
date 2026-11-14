import React, { useEffect, useRef, useState } from 'react';
import { prepareVocabularyCsv, VOCABULARY_BATCH_SIZE } from './vocabulary-import.js';
import './vocabulary-import.css';

// Temporary import screen: remove this module and its navigation entry after loading.
export default function VocabularyImport({ api }) {
  const [fileName, setFileName] = useState('');
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState({ done: 0, total: 0 });
  const stop = useRef(false);
  useEffect(() => {
    stop.current = false;
    return () => { stop.current = true; };
  }, []);

  async function selectFile(event) {
    stop.current = false;
    const file = event.target.files[0];
    setPreview(null); setError(''); setProgress({ done: 0, total: 0 });
    setFileName(file?.name || '');
    if (!file) return;
    setBusy(true);
    try {
      if (file.size > 25 * 1024 * 1024) throw new Error('Размер файла должен быть не больше 25 МБ.');
      const text = new TextDecoder('utf-8', { fatal: true }).decode(await file.arrayBuffer());
      const result = prepareVocabularyCsv(text);
      if (!stop.current) setPreview(result);
    } catch (issue) {
      if (!stop.current) setError(issue instanceof TypeError ? 'Не удалось прочитать UTF-8. Сохраните CSV в кодировке UTF-8.' : issue.message);
    } finally { if (!stop.current) setBusy(false); }
  }

  async function upload() {
    if (busy || !preview?.items.length || preview.errors.length) return;
    stop.current = false; setBusy(true); setError('');
    let done = 0;
    const total = preview.items.length;
    setProgress({ done, total });
    try {
      while (done < total && !stop.current) {
        const batch = preview.items.slice(done, done + VOCABULARY_BATCH_SIZE);
        try {
          await api('/api/vocabulary', {
            method: 'POST',
            body: JSON.stringify({
              languagePair: 'ba-ru',
              entries: batch.map(({ word, translation }) => ({ word, translation }))
            })
          });
        } catch (issue) {
          throw new Error(`Строки ${batch[0].line}–${batch.at(-1).line}: ${issue.message}`);
        }
        done += batch.length;
        if (!stop.current) setProgress({ done, total });
      }
    } catch (issue) { if (!stop.current) setError(issue.message); }
    finally {
      // A completed in-flight batch is saved even when the user stops the import.
      setProgress({ done, total });
      setBusy(false);
    }
  }

  return <section className="card csvImport vocabularyImport">
    <h2>Загрузить слова для сопоставления</h2>
    <p>CSV в UTF-8: первый столбец — слово на башкирском, второй — перевод на русский. Разделитель — запятая или точка с запятой.</p>
    <p>Заголовки word,translation или text_ba,text_ru необязательны. В каждой ячейке должно быть одно слово без пробелов, до 200 символов.</p>
    <label>Файл со словами<input type="file" accept=".csv,text/csv" disabled={busy} onChange={selectFile}/></label>
    {preview && <>
      <p><strong>{fileName}</strong>: строк {preview.total.toLocaleString('ru-RU')}, уникальных пар {preview.items.length.toLocaleString('ru-RU')}, ошибок {preview.errors.length}.</p>
      {preview.duplicates > 0 && <p>Одинаковые пары в файле пропущены: {preview.duplicates.toLocaleString('ru-RU')}.</p>}
      {preview.errors.length > 0 && <div className="error" role="alert"><p>Исправьте файл перед загрузкой. Первые ошибки:</p><ul>{preview.errors.slice(0, 10).map((message, i) => <li key={i}>{message}</li>)}</ul></div>}
      <div className="importPreview"><table><caption>Первые пять пар · башкирский → русский</caption><thead><tr><th>Слово</th><th>Перевод</th></tr></thead><tbody>{preview.items.slice(0, 5).map(item => <tr key={item.line}><td>{item.word}</td><td>{item.translation}</td></tr>)}</tbody></table></div>
      <div className="row">
        <button className="primary" disabled={busy || preview.errors.length > 0 || !preview.items.length} onClick={upload}>{busy && progress.total ? 'Загружаем…' : progress.total ? 'Повторить загрузку' : 'Загрузить слова'}</button>
        {busy && progress.total > 0 && <button className="ghost" onClick={() => { stop.current = true; }}>Остановить после текущей порции</button>}
      </div>
      {progress.total > 0 && <div role="status" aria-live="polite"><progress max={progress.total} value={progress.done}/><p>Обработано {progress.done} из {progress.total}.{!busy && progress.done === progress.total ? ' Загрузка завершена.' : !busy ? ' Загрузка остановлена. Можно повторить.' : ''}</p><p className="muted">Уже загруженные пары сохраняются. Повторная загрузка не создаёт копий; другой перевод того же слова добавляется отдельной парой.</p></div>}
    </>}
    {error && <p className="error" role="alert">{error}</p>}
  </section>;
}
