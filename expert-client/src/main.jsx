import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import CsvImport from './CsvImport.jsx';
import './style.css';

const tokenKey = 'bashperevod_expert_token';
const refreshKey = 'bashperevod_expert_refresh';
const number = value => new Intl.NumberFormat('ru-RU').format(value ?? 0);
const percent = value => value == null ? '—' : `${Math.round(Number(value) * 100)}%`;
const date = value => value ? new Date(value).toLocaleDateString('ru-RU', { day: 'numeric', month: 'short', year: 'numeric' }) : '—';
let refreshing;

function saveSession(session) {
  localStorage.setItem(tokenKey, session.accessToken);
  localStorage.setItem(refreshKey, session.refreshToken);
}
async function api(path, options = {}, retry = true) {
  const send = token => fetch(path, { ...options, headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...options.headers } });
  let response = await send(localStorage.getItem(tokenKey));
  if (response.status === 401 && retry && localStorage.getItem(refreshKey)) {
    refreshing ||= fetch('/auth/refresh', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: localStorage.getItem(refreshKey) }) })
      .then(async r => { if (!r.ok) { const issue = new Error('Сессия завершилась. Войдите снова.'); issue.status = 401; throw issue; } const session = await r.json(); saveSession(session); return session.accessToken; })
      .finally(() => { refreshing = null; });
    response = await send(await refreshing);
  }
  if (response.status === 204) return null;
  const data = await response.json().catch(() => null);
  if (!response.ok) { const issue = new Error(data?.detail || data?.title || `Ошибка ${response.status}`); issue.status = response.status; throw issue; }
  return data;
}
async function loadQueue() {
  const result = [];
  for (let offset = 0; ; offset += 100) {
    const page = await api(`/api/expert/cases?offset=${offset}&limit=100`);
    result.push(...page);
    if (page.length < 100) return result;
  }
}

function Login({ onLogin }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function submit(event) {
    event.preventDefault(); setBusy(true); setError('');
    try { const session = await api('/auth/sign-in', { method: 'POST', body: JSON.stringify({ username, password }) }, false); saveSession(session); onLogin(); }
    catch (issue) { setError(issue.message); } finally { setBusy(false); }
  }
  return <main className="loginPage"><section className="loginIntro"><div className="brand"><span className="brandMark">б.</span><span>башперевод<small>экспертный кабинет</small></span></div><div className="loginText"><span className="eyebrow">ПРОСТРАНСТВО ЭКСПЕРТИЗЫ</span><h1>Ваше слово<br/><em>делает перевод</em><br/>точнее.</h1><p>Проверяйте запросы преподавателей, изучайте мнение сообщества и выбирайте лучший вариант.</p></div><span className="loginGlyph" aria-hidden="true">Ә</span><div className="loginFoot">Каждый участник может стать экспертом.</div></section><form className="loginCard" onSubmit={submit}><span className="eyebrow">ДОБРО ПОЖАЛОВАТЬ</span><h2>Войти в кабинет</h2><p>Используйте аккаунт Башперевода.</p><label>Логин<input autoComplete="username" autoFocus value={username} onChange={e => setUsername(e.target.value)} required/></label><label>Пароль<input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required/></label>{error && <p className="error" role="alert">{error}</p>}<button className="primary" disabled={busy}>{busy ? 'Входим…' : 'Продолжить'} <span>↗</span></button><a href="https://bashperevod.ru">На основной сайт ↗</a></form></main>;
}

function CaseList({ title, eyebrow, rows, empty, onOpen }) {
  return <section className="card listCard"><div className="sectionHead"><div><span className="eyebrow">{eyebrow}</span><h2>{title}</h2></div><span className="badge">{number(rows.length)}</span></div>{rows.length ? <div className="list">{rows.map(item => <button className="listItem" key={item.caseId} onClick={() => onOpen(item.caseId)}><span className={`listIcon ${item.teacherRequested ? 'teacher' : ''}`}>{item.teacherRequested ? 'П' : 'Ә'}</span><span className="listMain"><strong>{item.original || `Перевод ${item.caseId.slice(0, 8)}`}</strong><small>{item.teacherRequested ? <b>Запрос преподавателя</b> : item.completed ? 'Проверено' : item.untranslated ? 'Перевод неизвестен' : 'Спорный перевод'} <i>·</i> {number(item.observations)} ответов <i>·</i> лидер {percent(item.leadingSupport)}</small></span><span className="listMeta">{date(item.completedAt || item.takenAt || item.createdAt)}</span><span className="rowArrow">↗</span></button>)}</div> : <div className="empty"><span>✓</span><strong>Здесь пока пусто</strong><p>{empty}</p></div>}</section>;
}

function CasePanel({ caseId, onBack, onChange, isTaken }) {
  const [item, setItem] = useState(null);
  const [primary, setPrimary] = useState('');
  const [additional, setAdditional] = useState([]);
  const [rejected, setRejected] = useState([]);
  const [comment, setComment] = useState('');
  const [proposal, setProposal] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  async function load() { const value = await api(`/api/expert/cases/${caseId}`); setItem(value); setPrimary(current => current && value.variants.some(v => v.id === current) ? current : value.resolution?.primaryVariantId || ''); }
  useEffect(() => { setItem(null); setError(''); load().catch(e => setError(e.message)); }, [caseId]);
  async function action(task) { setBusy(true); setError(''); try { await task(); } catch (e) { setError(e.message); if (e.status === 409) await load(); } finally { setBusy(false); } }
  const claim = () => action(async () => { await api(`/api/expert/cases/${caseId}/claim`, { method: 'POST' }); await onChange(); });
  const release = () => action(async () => { await api(`/api/expert/cases/${caseId}/claim`, { method: 'DELETE' }); await onChange(); onBack(); });
  const addVariant = event => { event.preventDefault(); action(async () => { await api(`/api/expert/cases/${caseId}/variants`, { method: 'POST', body: JSON.stringify({ text: proposal.trim(), expectedRevision: item.revision }) }); setProposal(''); await load(); }); };
  const resolve = event => { event.preventDefault(); action(async () => { await api(`/api/expert/cases/${caseId}/resolution`, { method: 'POST', body: JSON.stringify({ expectedRevision: item.revision, primaryVariantId: primary, additionalVariantIds: additional.filter(id => id !== primary), rejectedVariantIds: rejected.filter(id => id !== primary), comment: comment.trim() || null }) }); await onChange(); onBack(); }); };
  const translateNow = event => { event.preventDefault(); action(async () => { await api(`/api/expert/fragments/${item.fragmentId}/translation`, { method: 'POST', body: JSON.stringify({ translation: proposal.trim() }) }); await onChange(); onBack(); }); };
  const toggle = (id, list, set) => set(list.includes(id) ? list.filter(x => x !== id) : [...list, id]);
  if (!item) return <div className="detail"><button className="back" onClick={onBack}>← К списку</button><section className="card">{error || 'Загружаем перевод…'}</section></div>;
  const untranslated = !item.closed && !item.resolution?.primaryVariantId && item.variants.some(v => v.text === '');
  const variants = [...item.variants].filter(v => v.text.trim()).sort((a, b) => b.supports - a.supports || a.text.localeCompare(b.text, 'ru'));
  const max = Math.max(1, ...variants.map(v => v.supports));
  return <div className="detail"><button className="back" onClick={onBack}>← К списку</button><div className="detailTitle"><div><span className="eyebrow">ЭКСПЕРТИЗА / {caseId.slice(0, 8).toUpperCase()}</span><h1>{item.closed ? 'Проверенный перевод' : untranslated ? 'Нужен перевод' : 'Выбор перевода'}</h1><p>Сравните варианты и зафиксируйте экспертное решение.</p></div><span className={`status ${item.teacherNote ? 'teacher' : ''}`}>{item.closed ? 'Решение принято' : item.teacherNote ? 'Запрос преподавателя' : isTaken ? 'У вас в работе' : 'Доступен'}</span></div><div className="detailGrid"><div><section className="card source"><span className="eyebrow">ИСХОДНЫЙ ТЕКСТ · {item.languagePair.toUpperCase()}</span><span className="quote">“</span><h2>{item.original}</h2><div className="sourceLine"><span>Ответов участников</span><strong>{number(variants.reduce((sum, v) => sum + v.supports, 0))}</strong></div>{item.source?.title && <div className="sourceLine"><span>Источник</span><strong>{item.source.title}</strong></div>}</section>{item.teacherNote && <section className="teacherNote"><span className="eyebrow">ОТ ПРЕПОДАВАТЕЛЯ</span><p>{item.teacherNote}</p></section>}</div><section className="card decision"><div className="sectionHead"><div><span className="eyebrow">МНЕНИЕ СООБЩЕСТВА</span><h2>Варианты перевода</h2></div><span className="badge">{variants.length}</span></div><p className="helper">{untranslated ? 'Перевода пока нет. Эксперт может сразу предложить правильный вариант.' : 'Популярность помогает сравнить ответы. Правильным может быть и редкий вариант.'}</p><div className="variantList">{variants.map((v, index) => <div className={`variant ${primary === v.id ? 'selected' : ''}`} key={v.id}><div className="variantTop"><span className="rank">{String(index + 1).padStart(2, '0')}</span><div className="variantText"><strong>{v.text}</strong><span className="bar"><i style={{ width: `${Math.max(3, v.supports / max * 100)}%` }}/></span></div><span className="votes"><strong>{percent(v.supportRatio)}</strong><small>{number(v.supports)} голосов</small></span></div>{!item.closed && isTaken && <div className="variantActions"><label><input type="radio" name="primary" checked={primary === v.id} onChange={() => { setPrimary(v.id); setAdditional(additional.filter(id => id !== v.id)); setRejected(rejected.filter(id => id !== v.id)); }}/> Основной</label><label><input type="checkbox" checked={additional.includes(v.id)} disabled={primary === v.id || rejected.includes(v.id)} onChange={() => toggle(v.id, additional, setAdditional)}/> Допустимый</label><label><input type="checkbox" checked={rejected.includes(v.id)} disabled={primary === v.id || additional.includes(v.id)} onChange={() => toggle(v.id, rejected, setRejected)}/> Неверный</label></div>}{item.closed && item.resolution?.primaryVariantId === v.id && <div className="approved">✓ Основной перевод</div>}</div>)}</div>{untranslated && !isTaken && <form onSubmit={translateNow} className="addVariant"><label>Правильный перевод<input value={proposal} onChange={e => setProposal(e.target.value)} maxLength={1000} placeholder="Введите перевод" required/></label><button className="primary" disabled={busy || !proposal.trim()}>Сразу подтвердить перевод ↗</button></form>}{item.closed ? <p className="success">Экспертиза завершена.</p> : !isTaken ? <div className="takeAction"><p>Возьмите перевод в работу, чтобы выбрать вариант или предложить свой.</p><button className="primary" disabled={busy} onClick={claim}>Взять в работу <span>↗</span></button></div> : <><form onSubmit={addVariant} className="addVariant"><label>Не нашли точный вариант? <small>Добавьте свой перевод</small><input value={proposal} onChange={e => setProposal(e.target.value)} maxLength={1000} placeholder="Новый вариант перевода" required/></label><button className="ghost" disabled={busy || !proposal.trim()}>Добавить</button></form><form onSubmit={resolve} className="resolveForm"><label>Комментарий к решению <small>По желанию</small><textarea value={comment} onChange={e => setComment(e.target.value)} maxLength={10000} rows={3} placeholder="Что помогло выбрать этот перевод?"/></label><div className="formActions"><button type="button" className="textButton" disabled={busy} onClick={release}>Вернуть в очередь</button><button className="primary" disabled={busy || !primary || additional.length > 4}>Подтвердить решение <span>↗</span></button></div>{additional.length > 4 && <p className="error">Можно выбрать не более четырёх дополнительных вариантов.</p>}</form></>}{error && <p className="error" role="alert">{error}</p>}</section></div></div>;
}

function Settings({ value, onSave }) {
  const [form, setForm] = useState(value);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [error, setError] = useState('');
  useEffect(() => setForm(value), [value]);
  if (!form) return <section className="card">Загружаем настройки…</section>;
  async function submit(event) { event.preventDefault(); setBusy(true); setNotice(''); setError(''); try { await onSave(form); setNotice('Ваши настройки сохранены. Очередь обновлена.'); } catch (e) { setError(e.message); } finally { setBusy(false); } }
  return <section className="card settingsCard"><span className="eyebrow">ЛИЧНЫЕ НАСТРОЙКИ</span><h2>Какие переводы показывать</h2><p>Задайте границу для спорных переводов в своей очереди. Запросы преподавателей всегда идут первыми и не зависят от этих значений.</p><form onSubmit={submit}><div className="settingRow"><label htmlFor="required">Минимум ответов<small>Сколько участников должны ответить, прежде чем перевод попадёт в вашу очередь.</small></label><div className="numberInput"><input id="required" type="number" min="1" max="100000" step="1" value={form.requiredParticipants} onChange={e => setForm({ ...form, requiredParticipants: Number(e.target.value) })} required/><span>ответов</span></div></div><div className="settingRow"><label htmlFor="threshold">Поддержка лидера ниже<small>Если самый популярный вариант набрал меньше этого процента, перевод требует проверки.</small></label><div className="numberInput"><input id="threshold" type="number" min="1" max="100" step="1" value={Math.round(form.supportThreshold * 100)} onChange={e => setForm({ ...form, supportThreshold: Number(e.target.value) / 100 })} required/><span>%</span></div></div><div className="settingsActions"><span>Настройки действуют только для вашего кабинета.</span><button className="primary" disabled={busy}>Сохранить настройки <span>↗</span></button></div>{error && <p className="error" role="alert">{error}</p>}{notice && <p className="success" role="status">{notice}</p>}</form></section>;
}

function App() {
  const [signedIn, setSignedIn] = useState(Boolean(localStorage.getItem(tokenKey)));
  const [profile, setProfile] = useState(null);
  const [dashboard, setDashboard] = useState(null);
  const [queue, setQueue] = useState([]);
  const [work, setWork] = useState([]);
  const [preferences, setPreferences] = useState(null);
  const [tab, setTab] = useState('overview');
  const [caseId, setCaseId] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  async function load() {
    const [me, stats, cases, mine, prefs] = await Promise.all([api('/api/me'), api('/api/expert/dashboard'), loadQueue(), api('/api/expert/work'), api('/api/expert/preferences')]);
    setProfile(me); setDashboard(stats); setQueue(cases); setWork(mine); setPreferences(prefs); setError(''); setLoading(false);
  }
  useEffect(() => { if (signedIn) load().catch(e => { setError(e.message); setLoading(false); if (e.status === 401) logout(); }); }, [signedIn]);
  function logout() { localStorage.removeItem(tokenKey); localStorage.removeItem(refreshKey); setSignedIn(false); setProfile(null); setLoading(true); }
  async function savePreferences(value) { const saved = await api('/api/expert/preferences', { method: 'PUT', body: JSON.stringify(value) }); setPreferences(saved); await load(); }
  if (!signedIn) return <Login onLogin={() => setSignedIn(true)}/>;
  const taken = work.filter(item => !item.completed);
  const completed = work.filter(item => item.completed);
  const teachers = queue.filter(item => item.teacherRequested);
  const community = queue.filter(item => !item.teacherRequested);
  const open = id => { setCaseId(id); window.scrollTo(0, 0); };
  const navigate = id => { setTab(id); setCaseId(null); setError(''); window.scrollTo(0, 0); };
  const tabs = [['overview', '▦', 'Обзор'], ['available', '▤', 'Доступные'], ['taken', '◷', 'В работе'], ['completed', '✓', 'Мои решения'], ['settings', '⚙', 'Настройки'], ['import', '↑', 'Загрузка CSV']];
  const titles = { overview: 'Рабочий стол', available: 'Доступные переводы', taken: 'У меня в работе', completed: 'Мои решения', settings: 'Настройки очереди', import: 'Загрузка переводов' };
  return <div className="shell"><aside className="sidebar"><a className="brand" href="/"><span className="brandMark">б.</span><span>башперевод<small>экспертный кабинет</small></span></a><div className="navLabel">РАБОЧЕЕ ПРОСТРАНСТВО</div><nav>{tabs.map(([id, icon, label]) => <button key={id} className={tab === id && !caseId ? 'active' : ''} onClick={() => navigate(id)}><span>{icon}</span>{label}{id === 'available' && queue.length > 0 && <small>{queue.length}</small>}</button>)}</nav><div className="sideHint"><span>✳</span><strong>Точность начинается с вас</strong><p>Даже один проверенный перевод помогает всему сообществу.</p></div><div className="sidebarFoot"><span className="avatar">{profile?.username?.[0]?.toUpperCase() || 'Э'}</span><span>{profile?.username || 'Участник'}<small>Эксперт</small></span><button onClick={logout} title="Выйти" aria-label="Выйти">↗</button></div></aside><main className="content"><header className="topbar"><div>БАШПЕРЕВОД <span>/</span> ЭКСПЕРТИЗА</div><a href="https://bashperevod.ru">Основной сайт ↗</a></header>{caseId ? <CasePanel caseId={caseId} onBack={() => setCaseId(null)} onChange={load} isTaken={taken.some(item => item.caseId === caseId)}/> : <><div className="heading"><div><span className="eyebrow">{tab === 'overview' ? 'ВАШ ВКЛАД В ОБЩЕЕ ДЕЛО' : 'ЭКСПЕРТИЗА ПЕРЕВОДОВ'}</span><h1>{titles[tab]}</h1><p>{tab === 'settings' ? 'Настройте свою очередь и сосредоточьтесь на нужных проверках.' : tab === 'completed' ? 'Все переводы, для которых вы приняли экспертное решение.' : 'Выбирайте точные переводы на основе контекста и ответов сообщества.'}</p></div><span className="today">{new Date().toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' })}</span></div>{error && <div className="alert error" role="alert">{error} <button onClick={() => load().catch(e => setError(e.message))}>Повторить</button></div>}{loading ? <section className="card">Загружаем рабочее пространство…</section> : <>{tab === 'overview' && <><section className="hero"><div><span className="eyebrow">СЕГОДНЯ В ОЧЕРЕДИ</span><h2>Начните с запросов<br/>преподавателей.</h2><p>Затем переходите к переводам, где сообщество не пришло к единому мнению.</p><button className="heroButton" onClick={() => navigate('available')}>Открыть очередь ↗</button></div><span className="heroGlyph" aria-hidden="true">Ә</span></section><div className="stats"><button onClick={() => navigate('completed')}><span className="statIcon mint">✓</span><small>ПРОВЕРЕНО ВСЕГО</small><strong>{number(dashboard?.totalCompleted)}</strong><em>решений экспертов в проекте</em></button><button onClick={() => navigate('available')}><span className="statIcon coral">▤</span><small>ОСТАЛОСЬ В ОЧЕРЕДИ</small><strong>{number(dashboard?.available)}</strong><em>доступно вам сейчас</em></button><button onClick={() => navigate('completed')}><span className="statIcon lilac">✳</span><small>МОЙ ВКЛАД</small><strong>{number(dashboard?.completed)}</strong><em>ваших завершённых проверок</em></button></div><div className="overviewGrid"><CaseList title="Сначала — преподаватели" eyebrow="ПРИОРИТЕТНАЯ ОЧЕРЕДЬ" rows={teachers.slice(0, 4)} empty="Новых запросов преподавателей нет." onOpen={open}/><section className="insight"><span className="eyebrow">КАК РАБОТАЕТ ОЧЕРЕДЬ</span><div className="insightNumerals">01 <span>→</span> 02</div><h2>От запроса<br/>к решению</h2><p>Запросы преподавателей идут первыми. Остальные переводы появляются после {number(preferences.requiredParticipants)} ответов, если лидер набрал меньше {percent(preferences.supportThreshold)}.</p><button onClick={() => navigate('settings')}>Изменить параметры ↗</button></section></div><CaseList title="Спорные переводы" eyebrow="ПОСЛЕ ЗАПРОСОВ ПРЕПОДАВАТЕЛЕЙ" rows={community.slice(0, 5)} empty="Сообщество пока не передало спорных переводов." onOpen={open}/></>}{tab === 'available' && <><div className="queueNote"><span>✳</span><p><strong>Порядок проверки:</strong> сначала запросы преподавателей, затем переводы с низкой поддержкой лидера. Сейчас у вас {number(taken.length)} в работе.</p></div><CaseList title="Запросы преподавателей" eyebrow="ПРИОРИТЕТ · 01" rows={teachers} empty="Запросов от преподавателей пока нет." onOpen={open}/><CaseList title="Спорные переводы" eyebrow="СООБЩЕСТВО · 02" rows={community} empty="Под ваши параметры пока нет спорных переводов." onOpen={open}/></>}{tab === 'taken' && <CaseList title="Взятые мной" eyebrow="ТЕКУЩАЯ РАБОТА" rows={taken} empty="Вы пока не взяли перевод. Выберите задачу в доступной очереди." onOpen={open}/>} {tab === 'completed' && <CaseList title="Проверенные переводы" eyebrow="ИСТОРИЯ ВАШИХ РЕШЕНИЙ" rows={completed} empty="Ваши решения появятся здесь после первой проверки." onOpen={open}/>} {tab === 'settings' && <Settings value={preferences} onSave={savePreferences}/>} {tab === 'import' && <CsvImport api={api}/>}</>}</>}</main></div>;
}

createRoot(document.getElementById('root')).render(<App/>);
