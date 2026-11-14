import React, { useEffect, useRef, useState } from 'react';
import { deliveryKey, prepareCsv } from './csv-import.js';

export default function CsvImport({ api, profile, administrator }) {
  const [fileName, setFileName] = useState('');
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [access, setAccess] = useState('');
  const [progress, setProgress] = useState({ done: 0, total: 0 });
  const stop = useRef(false);
  useEffect(() => () => { stop.current = true; }, []);

  async function selectFile(event) {
    const file = event.target.files[0];
    setPreview(null); setError(''); setProgress({ done: 0, total: 0 });
    setFileName(file?.name || '');
    if (!file) return;
    setBusy(true);
    try {
      if (file.size > 25 * 1024 * 1024) throw new Error('Размер файла должен быть не больше 25 МБ.');
      const text = new TextDecoder('utf-8', { fatal: true }).decode(await file.arrayBuffer());
      setPreview(prepareCsv(text));
    } catch (issue) { setError(issue instanceof TypeError ? 'Не удалось прочитать UTF-8. Сохраните CSV в кодировке UTF-8.' : issue.message); }
    finally { setBusy(false); }
  }
  async function enableImport() {
    setBusy(true); setError('');
    try {
      await api(`/api/accounts/${profile.id}/access`, { method: 'PUT', body: JSON.stringify({ profile: 'producer' }) });
      setAccess('Загрузка для вашего аккаунта включена.');
    } catch (issue) { setError(issue.message); }
    finally { setBusy(false); }
  }
  async function upload() {
    stop.current = false; setBusy(true); setError('');
    let done = 0;
    setProgress({ done, total: preview.items.length });
    try {
      for (const item of preview.items) {
        if (stop.current) break;
        try {
          await api('/api/deliveries', { method: 'POST', headers: { 'Idempotency-Key': await deliveryKey(item.request) }, body: JSON.stringify(item.request) });
        } catch (issue) {
          throw new Error(`Строка ${item.line} (${item.request.fragment_id}): ${issue.status === 403 ? 'нет права загрузки. Администратор должен назначить аккаунту роль поставщика.' : issue.message}`);
        }
        done++; setProgress({ done, total: preview.items.length });
      }
    } catch (issue) { setError(issue.message); }
    finally { setBusy(false); }
  }
  return <section className="card csvImport">
    <h2>Загрузить переводы из CSV</h2>
    <p>Выберите файл в UTF-8 с колонками fragment_id, text_ba, text_ru и source_dataset. Будут добавлены переводы с башкирского на русский для проверки участниками.</p>
    <p>Все записи поступают как непроверенные. Оценка качества из файла не заменяет экспертное решение. Дополнительные колонки сохраняются в сведениях об источнике.</p>
    {administrator && <div className="importAccess"><button className="ghost" disabled={busy || !profile || Boolean(access)} onClick={enableImport}>Включить загрузку для моего аккаунта</button><p className="muted">Назначает вашему аккаунту роль поставщика данных.</p>{access && <p role="status" className="success">{access}</p>}</div>}
    <label>Файл с переводами<input type="file" accept=".csv,text/csv" disabled={busy} onChange={selectFile}/></label>
    {preview && <>
      <p><strong>{fileName}</strong>: записей {preview.total.toLocaleString('ru-RU')}, готово к загрузке {preview.items.length.toLocaleString('ru-RU')}, ошибок {preview.errors.length}.</p>
      {preview.repeatedPairs > 0 && <p>Повторяющихся пар текстов: {preview.repeatedPairs}. Записи с разными ID сохраняются отдельно, с их источниками.</p>}
      {preview.errors.length > 0 && <div className="error" role="alert"><p>Исправьте файл перед загрузкой. Первые ошибки:</p><ul>{preview.errors.slice(0, 10).map((message, i) => <li key={i}>{message}</li>)}</ul></div>}
      <div className="importPreview"><table><caption>Первые пять записей · башкирский → русский</caption><thead><tr><th>ID</th><th>Оригинал</th><th>Перевод</th></tr></thead><tbody>{preview.items.slice(0, 5).map(item => <tr key={item.request.fragment_id}><td>{item.request.fragment_id}</td><td>{item.request.text_ba}</td><td>{item.request.translation}</td></tr>)}</tbody></table></div>
      <div className="row"><button className="primary" disabled={busy || preview.errors.length > 0 || !preview.items.length} onClick={upload}>{progress.total ? 'Повторить загрузку' : 'Загрузить для проверки'}</button>{busy && progress.total > 0 && <button className="ghost" onClick={() => { stop.current = true; }}>Остановить после текущей записи</button>}</div>
      {progress.total > 0 && <div role="status" aria-live="polite"><progress max={progress.total} value={progress.done}/><p>Обработано {progress.done} из {progress.total}.{!busy && progress.done === progress.total ? ' Загрузка завершена.' : !busy ? ' Загрузка остановлена. Можно повторить.' : ''}</p><p className="muted">Повторная загрузка тех же записей под тем же аккаунтом не создаёт копий. Уже загруженные строки сохраняются при остановке.</p></div>}
    </>}
    {error && <p className="error" role="alert">{error}</p>}
  </section>;
}
