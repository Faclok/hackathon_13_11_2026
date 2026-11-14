import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';
import CsvImport from './CsvImport.jsx';

const tokenKey = 'bashperevod_expert_token';
const refreshKey = 'bashperevod_expert_refresh';
let refreshing;
async function api(path, options = {}, retry = true) {
  const token = localStorage.getItem(tokenKey);
  const send = bearer => fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}), ...options.headers }
  });
  let response = await send(token);
  if (response.status === 401 && retry && localStorage.getItem(refreshKey)) {
    refreshing ||= fetch('/auth/refresh', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: localStorage.getItem(refreshKey) }) })
      .then(async r => { if (!r.ok) throw new Error('Войдите снова.'); const session = await r.json(); saveSession(session); return session.accessToken; })
      .finally(() => { refreshing = null; });
    response = await send(await refreshing);
  }
  if (response.status === 204) return null;
  const data = await response.json().catch(() => null);
  if (!response.ok) { const issue = new Error(data?.detail || data?.title || `Ошибка ${response.status}`); issue.status = response.status; throw issue; }
  return data;
}
function saveSession(session) {
  localStorage.setItem(tokenKey, session.accessToken);
  localStorage.setItem(refreshKey, session.refreshToken);
}
const percent = value => value == null ? '—' : `${Math.round(value * 100)}%`;
const date = value => new Date(value).toLocaleString('ru-RU', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

function Login({ onLogin }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const submit = async event => {
    event.preventDefault(); setBusy(true); setError('');
    try { const session = await api('/auth/sign-in', { method: 'POST', body: JSON.stringify({ username, password }) }, false); saveSession(session); onLogin(); }
    catch (issue) { setError(issue.message); } finally { setBusy(false); }
  };
  return <div className="loginPage"><div className="loginIntro"><span className="mark">Б<span>↗</span></span><div className="eyebrow">БАШПЕРЕВОД / ЭКСПЕРТИЗА</div><h1>Сложные переводы<br/><em>нуждаются в вашем слове.</em></h1><p>Решения экспертов помогают выбрать точный перевод там, где мнения участников разделились.</p></div><form className="loginCard" onSubmit={submit}><span className="eyebrow">РАБОЧЕЕ ПРОСТРАНСТВО</span><h2>Вход для экспертов</h2><label>Логин<input autoComplete="username" value={username} onChange={e => setUsername(e.target.value)} required/></label><label>Пароль<input type="password" autoComplete="current-password" value={password} onChange={e => setPassword(e.target.value)} required/></label>{error && <p className="error" role="alert">{error}</p>}<button className="primary" disabled={busy}>Войти <span>↗</span></button><a href="https://bashperevod.ru">На основной сайт</a></form></div>;
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
  const load = async () => { const value = await api(`/api/expert/cases/${caseId}`); setItem(value); setPrimary(value.resolution?.primaryVariantId || value.variants[0]?.id || ''); };
  useEffect(() => { load().catch(e => setError(e.message)); }, [caseId]);
  const claim = async () => { setBusy(true); setError(''); try { await api(`/api/expert/cases/${caseId}/claim`, { method: 'POST' }); await onChange(); } catch (e) { setError(e.message); } finally { setBusy(false); } };
  const release = async () => { setBusy(true); setError(''); try { await api(`/api/expert/cases/${caseId}/claim`, { method: 'DELETE' }); await onChange(); onBack(); } catch (e) { setError(e.message); } finally { setBusy(false); } };
  const addVariant = async event => { event.preventDefault(); setBusy(true); setError(''); try { await api(`/api/expert/cases/${caseId}/variants`, { method: 'POST', body: JSON.stringify({ text: proposal.trim(), expectedRevision: item.revision }) }); setProposal(''); await load(); } catch (e) { setError(e.message); } finally { setBusy(false); } };
  const resolve = async event => { event.preventDefault(); setBusy(true); setError(''); try {
    await api(`/api/expert/cases/${caseId}/resolution`, { method: 'POST', body: JSON.stringify({ expectedRevision: item.revision, primaryVariantId: primary,
      additionalVariantIds: additional.filter(id => id !== primary && !rejected.includes(id)), rejectedVariantIds: rejected.filter(id => id !== primary), comment: comment.trim() || null }) });
    await onChange(); onBack();
  } catch (e) { setError(e.message); } finally { setBusy(false); } };
  const toggle = (value, list, set) => set(list.includes(value) ? list.filter(id => id !== value) : [...list, value]);
  if (!item) return <section className="card"><button className="back" onClick={onBack}>← Назад</button><p>{error || 'Загрузка…'}</p></section>;
  const votes = item.variants.reduce((sum, v) => sum + v.supports, 0);
  return <div className="detail"><button className="back" onClick={onBack}>← К списку</button><div className="detailGrid"><section className="card source"><span className="eyebrow">ИСХОДНЫЙ ФРАГМЕНТ · {item.languagePair.toUpperCase()}</span><h2>{item.original}</h2><p>Ответов участников: <strong>{votes}</strong></p>{item.teacherNote && <p><strong>Пометка преподавателя:</strong> {item.teacherNote}</p>}{item.source?.title && <p>Источник: {item.source.title}</p>}<div className="caseId">ID: {item.caseId}</div></section><section className="card"><div className="row"><div><span className="eyebrow">ЭКСПЕРТНОЕ РЕШЕНИЕ</span><h2>{item.closed ? 'Решение принято' : 'Выберите перевод'}</h2></div>{!item.closed && !isTaken && <button className="primary small" disabled={busy} onClick={claim}>Взять в работу</button>}{!item.closed && isTaken && <button className="ghost" disabled={busy} onClick={release}>Вернуть в очередь</button>}</div><div className="variantList">{item.variants.map(v => <div className="variant" key={v.id}><div className="variantTop"><strong>{v.text}</strong><span>{v.supports} · {percent(v.supportRatio)}</span></div>{!item.closed && isTaken && <div className="variantActions"><label><input type="radio" name="primary" checked={primary === v.id} onChange={() => { setPrimary(v.id); setRejected(rejected.filter(id => id !== v.id)); setAdditional(additional.filter(id => id !== v.id)); }}/> Основной</label><label><input type="checkbox" checked={additional.includes(v.id)} disabled={primary === v.id || rejected.includes(v.id)} onChange={() => toggle(v.id, additional, setAdditional)}/> Допустимый</label><label><input type="checkbox" checked={rejected.includes(v.id)} disabled={primary === v.id || additional.includes(v.id)} onChange={() => toggle(v.id, rejected, setRejected)}/> Отклонить</label></div>}</div>)}</div>{item.closed ? <p className="success">Решение сохранено. Повторная экспертиза закрыта.</p> : isTaken ? <><form onSubmit={addVariant} className="addVariant"><label>Предложить свой вариант<input value={proposal} onChange={e => setProposal(e.target.value)} maxLength={1000} placeholder="Точный перевод" required/></label><button className="ghost" disabled={busy || !proposal.trim()}>Добавить вариант</button></form><form onSubmit={resolve} className="resolveForm"><label>Комментарий к решению<textarea value={comment} onChange={e => setComment(e.target.value)} maxLength={10000} rows={3} placeholder="Почему выбран этот вариант?"/></label><button className="primary" disabled={busy || !primary || additional.length > 4}>Завершить экспертизу <span>↗</span></button>{additional.length > 4 && <p className="error">Можно выбрать не более четырёх допустимых вариантов.</p>}</form></> : <p className="muted">Закрепите перевод за собой, чтобы принять решение.</p>}{error && <p className="error" role="alert">{error}</p>}</section></div></div>;
}

function App() {
  const [signedIn, setSignedIn] = useState(Boolean(localStorage.getItem(tokenKey)));
  const [profile, setProfile] = useState(null);
  const [dashboard, setDashboard] = useState(null);
  const [queue, setQueue] = useState([]);
  const [work, setWork] = useState([]);
  const [policy, setPolicy] = useState(null);
  const [tab, setTab] = useState('overview');
  const [caseId, setCaseId] = useState(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const load = async () => {
    const results = await Promise.allSettled([api('/api/me'), api('/api/expert/dashboard'), api('/api/expert/cases?limit=100'), api('/api/expert/work'), api('/api/policies/distribution')]);
    if (results[0].status === 'rejected') throw results[0].reason;
    setProfile(results[0].value);
    if (results[1].status === 'fulfilled') setDashboard(results[1].value);
    if (results[2].status === 'fulfilled') setQueue(results[2].value);
    if (results[3].status === 'fulfilled') setWork(results[3].value);
    if (results[4].status === 'fulfilled') {
      setPolicy(results[4].value);
      if (results[1].status === 'rejected') setTab('settings');
    }
    if (results[1].status === 'rejected' && results[4].status === 'rejected') setTab('import');
  };
  useEffect(() => { if (signedIn) load().catch(e => setError(e.message)); }, [signedIn]);
  const logout = () => { localStorage.removeItem(tokenKey); localStorage.removeItem(refreshKey); setSignedIn(false); setProfile(null); setDashboard(null); setPolicy(null); setError(''); };
  const savePolicy = async event => { event.preventDefault(); setBusy(true); setError(''); setNotice(''); try {
    await api('/api/policies/distribution', { method: 'POST', body: JSON.stringify(policy.settings) }); await load(); setNotice('Настройки применены для всех новых решений.');
  } catch (e) { setError(e.message); } finally { setBusy(false); } };
  if (!signedIn) return <Login onLogin={() => setSignedIn(true)}/>;
  const taken = work.filter(x => !x.completed);
  const completed = work.filter(x => x.completed);
  const open = id => { setCaseId(id); window.scrollTo(0, 0); };
  const list = (rows, empty, status) => rows.length ? <div className="list">{rows.map(item => <button className="listItem" key={item.caseId} onClick={() => open(item.caseId)}><span className="listMain"><strong>{item.original || `Перевод ${item.caseId.slice(0, 8)}`}</strong><small>{status || (item.completed ? 'Завершено' : 'Спорный перевод')} · {item.observations} ответов · лидер {percent(item.leadingSupport)}</small></span><span className="listMeta">{date(item.takenAt || item.createdAt)} <b>↗</b></span></button>)}</div> : <p className="empty">{empty}</p>;
  return <div className="shell"><aside className="sidebar"><a className="logo" href="/"><span className="mark">Б<span>↗</span></span><span>башперевод<small>экспертиза</small></span></a><div className="navLabel">РАБОЧЕЕ ПРОСТРАНСТВО</div><nav>{[['overview', '▦', 'Дашборд'], ['available', '◫', 'Доступные'], ['taken', '◷', 'В работе'], ['completed', '✓', 'Сделано'], ['import', '↑', 'Загрузка CSV'], ...(policy ? [['settings', '⚙', 'Настройки']] : [])].map(([id, icon, label]) => <button key={id} className={tab === id ? 'active' : ''} onClick={() => { setTab(id); setCaseId(null); setError(''); }}><span>{icon}</span>{label}{id === 'available' && queue.length > 0 && <small>{queue.length}</small>}</button>)}</nav><div className="sidebarFoot"><span className="avatar">{profile?.username?.[0]?.toUpperCase() || 'Э'}</span><span>{profile?.username || 'Эксперт'}<small>Личный кабинет</small></span><button onClick={logout} title="Выйти">↗</button></div></aside><main className="content"><header className="topbar"><div className="breadcrumb">БАШПЕРЕВОД <span>/</span> ЭКСПЕРТИЗА</div><a href="https://bashperevod.ru">Основной сайт ↗</a></header>{caseId ? <CasePanel caseId={caseId} onBack={() => setCaseId(null)} onChange={load} isTaken={taken.some(x => x.caseId === caseId)}/> : <><div className="heading"><div><span className="eyebrow">{tab === 'overview' ? 'ОБЗОР' : 'ЭКСПЕРТИЗА ПЕРЕВОДОВ'}</span><h1>{{ overview: 'Рабочий стол', available: 'Можно взять', taken: 'В работе', completed: 'Сделано', settings: 'Настройки экспертизы', import: 'Загрузка переводов' }[tab]}</h1><p>{tab === 'import' ? 'Добавьте подготовленные переводы для проверки.' : tab === 'settings' ? 'Глобальные условия, по которым перевод попадает на экспертизу.' : 'Точные решения для спорных переводов.'}</p></div><span className="today">{new Date().toLocaleDateString('ru-RU', { day: 'numeric', month: 'long', year: 'numeric' })}</span></div>{tab === 'import' && <CsvImport api={api} profile={profile} administrator={Boolean(policy)}/>}{tab === 'overview' && <><div className="stats"><button onClick={() => setTab('available')}><span>ОЖИДАЮТ РЕШЕНИЯ</span><strong>{dashboard?.available ?? '—'}</strong><small>Можно взять в работу ↗</small></button><button onClick={() => setTab('taken')}><span>У МЕНЯ В РАБОТЕ</span><strong>{dashboard?.taken ?? '—'}</strong><small>Открыть список ↗</small></button><button onClick={() => setTab('completed')}><span>ЗАВЕРШЕНО МНОЙ</span><strong>{dashboard?.completed ?? '—'}</strong><small>Посмотреть историю ↗</small></button></div><section className="card"><div className="sectionHead"><div><span className="eyebrow">ОЧЕРЕДЬ</span><h2>Спорные переводы</h2></div><button className="linkButton" onClick={() => setTab('available')}>Все доступные ↗</button></div>{list(queue.slice(0, 5), 'Пока нет переводов, требующих экспертизы.')}</section><div className="rule"><span>✳</span><p>На экспертизу попадают переводы с {dashboard?.requiredParticipants ?? '—'} ответами и поддержкой лидера ниже {percent(dashboard?.supportThreshold)}, а также переводы по запросу преподавателя. После экспертного решения повторная проверка закрыта.</p></div></>}{tab === 'available' && <section className="card"><div className="sectionHead"><h2>Доступные для экспертизы</h2><span className="badge">{queue.length}</span></div>{list(queue, 'Сейчас нет спорных переводов.')}</section>}{tab === 'taken' && <section className="card"><div className="sectionHead"><h2>Взятые мной</h2><span className="badge">{taken.length}</span></div>{list(taken, 'Вы пока не взяли перевод в работу.', 'В работе')}</section>}{tab === 'completed' && <section className="card"><div className="sectionHead"><h2>Мои решения</h2><span className="badge">{completed.length}</span></div>{list(completed, 'Экспертных решений пока нет.', 'Завершено')}</section>}{tab === 'settings' && policy && <section className="card settingsCard"><span className="eyebrow">ОБЩАЯ ПОЛИТИКА · ВЕРСИЯ {policy.version}</span><h2>Когда нужен эксперт</h2><p>Если ответов достаточно, а наиболее популярный перевод получил меньше указанной доли, задача появится в очереди. Преподаватель также может запросить проверку своего перевода с пояснением.</p><form onSubmit={savePolicy}><label>Минимальное количество ответов<input type="number" min={Math.max(1, policy.settings.minimumClasses)} step="1" value={policy.settings.requiredParticipants} onChange={e => setPolicy({ ...policy, settings: { ...policy.settings, requiredParticipants: Number(e.target.value) } })} required/><small>Например, 300 ответов</small></label><label>Порог поддержки лидера, %<input type="number" min="1" max="100" step="1" value={Math.round(policy.settings.supportThreshold * 100)} onChange={e => setPolicy({ ...policy, settings: { ...policy.settings, supportThreshold: Number(e.target.value) / 100 } })} required/><small>При 80% перевод не требует экспертизы; 60–70% — спорный.</small></label><button className="primary" disabled={busy}>Сохранить для всех <span>↗</span></button></form></section>}</>}{error && <div className="alert error" role="alert">{error}</div>}{notice && <div className="alert success" role="status">{notice}</div>}</main></div>;
}

createRoot(document.getElementById('root')).render(<App/>);
