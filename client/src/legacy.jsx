import React, { useEffect, useState } from 'react';

import './style.css';
import './lms.css';

let refreshPromise = null;
const api = async (path, token, options = {}) => {
  const send = bearer => fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}), ...options.headers }
  });
  let response = await send(token ? localStorage.getItem('bashperevod_token') || token : null);
  if (response.status === 401 && token && localStorage.getItem('bashperevod_refresh')) {
    refreshPromise ||= fetch('/auth/refresh', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: localStorage.getItem('bashperevod_refresh') })
    }).then(async result => {
      if (!result.ok) throw new Error('Войдите в аккаунт ещё раз.');
      const session = await result.json();
      localStorage.setItem('bashperevod_token', session.accessToken);
      localStorage.setItem('bashperevod_refresh', session.refreshToken);
      return session.accessToken;
    }).finally(() => { refreshPromise = null; });
    response = await send(await refreshPromise);
  }
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.title || data?.error || (response.status === 401 ? 'Войдите в аккаунт ещё раз.' : 'Не удалось выполнить запрос.'));
  return data;
};

function Auth({ onSuccess }) {
  const [mode, setMode] = useState('register');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const send = async e => {
    e.preventDefault(); setBusy(true); setError('');
    try {
      if (mode === 'register') await api('/auth/register', null, { method: 'POST', body: JSON.stringify({ username, password }) });
      onSuccess(await api('/auth/sign-in', null, { method: 'POST', body: JSON.stringify({ username, password }) }));
    } catch (issue) { setError(issue.message); } finally { setBusy(false); }
  };
  return <div className="authLayout"><div className="authIntro"><span className="sparkle">✦</span><h3>Начните с одного слова</h3><p>Проверяйте переводы вместе с другими участниками.</p></div><form className="authForm" onSubmit={send}><div className="authTabs"><button type="button" className={mode === 'register' ? 'selected' : ''} onClick={() => setMode('register')}>Регистрация</button><button type="button" className={mode === 'login' ? 'selected' : ''} onClick={() => setMode('login')}>Войти</button></div><h3>{mode === 'register' ? 'Создать аккаунт' : 'С возвращением'}</h3>{mode === 'register' && <p>За регистрацию — 50 монет. Имя и фамилию можно заполнить в профиле.</p>}<label>Логин<input value={username} onChange={e => setUsername(e.target.value)} required minLength={3} maxLength={32} /></label><label>Пароль<input type="password" value={password} onChange={e => setPassword(e.target.value)} required minLength={8} /></label>{error && <p className="formError">{error}</p>}<button className="primary" disabled={busy}>{mode === 'register' ? 'Начать' : 'Войти'} <span>↗</span></button></form></div>;
}

function App() {
  const [token, setToken] = useState(localStorage.getItem('bashperevod_token') || '');
  const [profile, setProfile] = useState(null);
  const [assignment, setAssignment] = useState(null);
  const [correction, setCorrection] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const load = async activeToken => {
    const me = await api('/api/me', activeToken);
    setProfile(me);
    const next = await api('/api/reviews/next', activeToken, { method: 'POST' });
    setAssignment(next);
  };
  useEffect(() => {
    if (token) load(token).catch(error => {
      setMessage(error.message);
      if (error.message.includes('Войдите')) {
        localStorage.removeItem('bashperevod_token');
        localStorage.removeItem('bashperevod_refresh');
        setToken('');
      }
    });
    else { setProfile(null); setAssignment(null); }
  }, [token]);
  const authenticated = ({ accessToken, refreshToken }) => {
    localStorage.setItem('bashperevod_token', accessToken);
    localStorage.setItem('bashperevod_refresh', refreshToken);
    setToken(accessToken); setMessage('');
  };
  const logout = () => {
    localStorage.removeItem('bashperevod_token');
    localStorage.removeItem('bashperevod_refresh');
    setToken(''); setMessage('');
  };
  const submit = async action => {
    setBusy(true); setMessage('');
    try {
      await api(`/api/reviews/${assignment.id}/response`, token, {
        method: 'POST', body: JSON.stringify({ action, correction: action === 'Correct' ? correction.trim() : null })
      });
      setCorrection(''); setMessage('Ответ сохранён.');
      setAssignment(await api('/api/reviews/next', token, { method: 'POST' }));
    } catch (error) { setMessage(error.message); } finally { setBusy(false); }
  };
  return <div className="shell"><header className="header wrap"><a className="brand" href="/"><span className="brandMark">Б<span>↗</span></span><span>баш<span className="brandLight">перевод</span><small>язык живёт в диалоге</small></span></a><nav><a href="https://lms.bashperevod.ru">Учебный кабинет ↗</a></nav><div className="headerRight">{profile ? <><span className="pill">{profile.displayName || profile.username}</span><button className="textButton" onClick={logout}>Выйти</button></> : <span className="pill">Башҡортса · Русский</span>}</div></header>
    <main className="wrap"><section className="hero"><div className="heroText"><div className="eyebrow">БЕРГӘ ТӘРЖЕМӘ ИТӘБЕҘ</div><h1>Каждое слово<br /><em>имеет значение.</em></h1><p>Изучайте башкирский язык и помогайте проверять переводы.</p></div><div className="heroArt" aria-hidden="true"><span className="orb orbOne"/><div className="artCard artCardFront"><span>БАШҠОРТСА → РУССКИЙ</span><strong>Рәхмәт</strong><i>спасибо</i></div></div></section>
    <div className="sectionHeading"><div><span className="sectionIndex">ПРАКТИКА</span><h2>Проверка перевода</h2></div></div>
    {!token ? <Auth onSuccess={authenticated} /> : assignment ? <div className="taskLayout"><article className="taskCard"><div className="cardTop"><span className="languagePair">БАШКИРСКИЙ → РУССКИЙ</span></div><p className="taskLabel">Прочитайте фразу и проверьте перевод</p><div className="phrase">{assignment.original}</div><div className="translation"><span>ПРЕДЛОЖЕННЫЙ ПЕРЕВОД</span><strong>{assignment.translation}</strong></div><div className="answerArea"><label htmlFor="correction">Ваш вариант перевода</label><textarea id="correction" value={correction} onChange={e => setCorrection(e.target.value)} maxLength={1000} rows={3}/><div className="actions"><button className="primary" disabled={busy || !correction.trim()} onClick={() => submit('Correct')}>Отправить исправление <span>↗</span></button><button className="secondary" disabled={busy} onClick={() => submit('Confirm')}>Верный перевод ✓</button><button className="secondary" disabled={busy} onClick={() => submit('Skip')}>Не знаю</button></div></div></article><aside className="sideCard"><span className="sideIcon">✳</span><h3>Вместе точнее</h3><p>Последнее принятие каждого участника участвует в согласовании. Исправление поддерживает предложенный вами вариант.</p></aside></div> : <p className="hint">Сейчас нет доступных переводов для проверки.</p>}
    {message && <div className="toast" role="status"><button onClick={() => setMessage('')} aria-label="Закрыть">×</button>{message}</div>}</main><footer className="footer wrap">© Башперевод</footer></div>;
}

export function LmsApp() {
  const requestedCourse = new URLSearchParams(window.location.search).get('scorm') === '1' ? 1 : null;
  const [token, setToken] = useState(localStorage.getItem('bashperevod_token') || '');
  const [profile, setProfile] = useState(null);
  const [courses, setCourses] = useState([]);
  const [courseId, setCourseId] = useState(null);
  const [lessons, setLessons] = useState([]);
  const [progress, setProgress] = useState([]);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api('/api/lms/courses').then(items => {
      setCourses(items);
      if (items.length) setCourseId(requestedCourse ?? items[0].id);
    }).catch(error => setMessage(error.message));
  }, []);
  useEffect(() => {
    if (courseId === null) return;
    setLessons([]);
    api(`/api/lms/courses/${courseId}/lessons`).then(setLessons).catch(error => setMessage(error.message));
  }, [courseId]);
  useEffect(() => {
    if (!token) { setProfile(null); setProgress([]); return; }
    Promise.all([api('/api/me', token), api('/api/lms/me/progress', token)])
      .then(([me, done]) => { setProfile(me); setProgress(done); })
      .catch(error => {
        setMessage(error.message);
        if (error.message.includes('Войдите')) {
          localStorage.removeItem('bashperevod_token'); localStorage.removeItem('bashperevod_refresh'); setToken('');
        }
      });
  }, [token]);
  useEffect(() => {
    if (!token || !lessons.length || window.parent === window) return;
    const done = lessons.filter(lesson => progress.some(item => item.lesson_id === lesson.id)).length;
    window.parent.postMessage({
      type: 'BASHPEREVOD_SCORM_PROGRESS',
      score: Math.round(done * 100 / lessons.length),
      completed: done === lessons.length
    }, '*');
  }, [token, lessons, progress]);

  const complete = async id => {
    setBusy(true); setMessage('');
    try {
      await api(`/api/lms/lessons/${id}/complete`, token, { method: 'POST' });
      setProgress(await api('/api/lms/me/progress', token));
      setMessage('Урок сохранён. Прогресс обновлён.');
    } catch (error) { setMessage(error.message); }
    finally { setBusy(false); }
  };
  const authenticated = ({ accessToken: newToken, refreshToken }) => {
    localStorage.setItem('bashperevod_refresh', refreshToken);
    localStorage.setItem('bashperevod_token', newToken);
    setToken(newToken); setMessage('');
  };
  const logout = () => {
    localStorage.removeItem('bashperevod_token');
    setToken(''); setMessage('');
  };
  const completed = lessons.filter(lesson => progress.some(item => item.lesson_id === lesson.id)).length;
  const currentCourse = courses.find(course => course.id === courseId);

  return <div className="shell lmsShell">
    <header className="header wrap">
      <a className="brand" href="/"><span className="brandMark">Б<span>↗</span></span><span>баш<span className="brandLight">перевод</span><small>учебный кабинет</small></span></a>
      <span className="pill">LMS · Башҡортса</span>
      {profile && <button className="textButton" onClick={logout}>Выйти</button>}
    </header>
    <main className="wrap lmsMain">
      <section className="lmsHero">
        <span className="sectionIndex">ОБУЧЕНИЕ · БАШКИРСКИЙ ЯЗЫК</span>
        <h1>Начните с простого.<br /><em>Учитесь в своём темпе.</em></h1>
        <p>Короткие уроки помогут узнать первые слова и перейти к практике перевода.</p>
      </section>
      {!token && <section className="lmsAuth"><h2>Войдите, чтобы сохранять прогресс</h2><Auth onSuccess={authenticated} /></section>}
      <section className="lmsCourse">
        <div className="lmsCourseHeader"><div><span className="sectionIndex">КАТАЛОГ КУРСОВ</span><h2>{currentCourse?.title || 'Курсы'}</h2><p>{currentCourse?.description}</p></div>{!requestedCourse && courses.length > 1 && <label>Курс <select value={courseId ?? ''} onChange={e => setCourseId(Number(e.target.value))}>{courses.map(course => <option key={course.id} value={course.id}>{course.title}</option>)}</select></label>}</div>
        {token && lessons.length > 0 && <div className="lmsProgress" role="status"><span>Пройдено {completed} из {lessons.length} уроков</span><div className="lmsProgressTrack"><span style={{ width: `${completed * 100 / lessons.length}%` }}/></div></div>}
        <div className="lessons">{lessons.map(lesson => {
          const done = progress.some(item => item.lesson_id === lesson.id);
          return <article className="lesson" key={lesson.id}><span className="lessonNo">{String(lesson.position).padStart(2, '0')}</span><div><h3>{lesson.title}</h3><p>{lesson.body}</p></div><button disabled={!token || busy || done} onClick={() => complete(lesson.id)}>{done ? 'Пройдено ✓' : 'Завершить урок'}</button></article>;
        })}</div>
        {token && lessons.length > 0 && completed === lessons.length && <p className="saved">Курс завершён. При запуске через SCORM прогресс отправляется в учебную систему.</p>}
      </section>
      {message && <div className="toast" role="status"><button onClick={() => setMessage('')} aria-label="Закрыть">×</button>{message}</div>}
    </main>
    <footer className="footer wrap"><span>© Башперевод · Учебный кабинет</span><a href="https://bashperevod.ru">Основной сайт ↗</a></footer>
  </div>;
}



