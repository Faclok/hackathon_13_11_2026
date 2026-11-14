import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

const api = async (path, token, options = {}) => {
  const response = await fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...options.headers }
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.error || (response.status === 401 ? 'Войдите в аккаунт ещё раз.' : 'Не удалось выполнить запрос.'));
  return data;
};

function App() {
  const [token, setToken] = useState(localStorage.getItem('bashperevod_token') || '');
  const [tab, setTab] = useState('task');
  const [profile, setProfile] = useState(null);
  const [task, setTask] = useState(null);
  const [leaders, setLeaders] = useState([]);
  const [courses, setCourses] = useState([]);
  const [lessons, setLessons] = useState([]);
  const [progress, setProgress] = useState([]);
  const [translations, setTranslations] = useState([]);
  const [answer, setAnswer] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const load = async (activeToken = token) => {
    const [me, today, board, courseList, words] = await Promise.all([
      api('/api/me', activeToken), api('/api/tasks/today', activeToken), api('/api/leaderboard'),
      api('/api/lms/courses'), api('/api/translations')
    ]);
    setProfile(me); setTask(today); setLeaders(board); setCourses(courseList); setTranslations(words);
    if (courseList[0]) {
      const [items, done] = await Promise.all([
        api(`/api/lms/courses/${courseList[0].id}/lessons`), api('/api/lms/me/progress', activeToken)
      ]);
      setLessons(items); setProgress(done);
    }
  };
  useEffect(() => {
    Promise.all([api('/api/leaderboard'), api('/api/lms/courses'), api('/api/translations')])
      .then(async ([board, list, words]) => {
        setLeaders(board); setCourses(list); setTranslations(words);
        if (list[0]) setLessons(await api(`/api/lms/courses/${list[0].id}/lessons`));
      })
      .catch(() => {});
  }, []);
  useEffect(() => { if (token) load().catch(error => { setMessage(error.message); if (error.message.includes('Войдите')) logout(); }); }, [token]);
  const logout = () => { localStorage.removeItem('bashperevod_token'); setToken(''); setProfile(null); };
  const authenticated = ({ token: newToken }) => { localStorage.setItem('bashperevod_token', newToken); setToken(newToken); setMessage(''); };

  const submit = async action => {
    setBusy(true); setMessage('');
    try {
      const result = await api(`/api/tasks/${task.id}/submission`, token, { method: 'POST', body: JSON.stringify({ action, answerText: answer }) });
      setMessage(`Ответ сохранён · +10 баллов · ${result.status === 'confirmed' ? 'перевод подтверждён' : result.status === 'disputed' ? 'ответы различаются' : 'ожидаем другие ответы'}`);
      await load();
    } catch (error) { setMessage(error.message); }
    finally { setBusy(false); }
  };
  const complete = async id => {
    setBusy(true);
    try { await api(`/api/lms/lessons/${id}/complete`, token, { method: 'POST' }); setProgress(await api('/api/lms/me/progress', token)); }
    catch (error) { setMessage(error.message); }
    finally { setBusy(false); }
  };

  return <div className="shell">
    <header className="header wrap">
      <a className="brand" href="/" onClick={e => { e.preventDefault(); setTab('task'); }}><span className="brandMark">Б<span>↗</span></span><span>баш<span className="brandLight">перевод</span><small>язык живёт в диалоге</small></span></a>
      <nav aria-label="Основное меню">
        <button className={tab === 'task' ? 'active' : ''} onClick={() => setTab('task')}>Задание</button>
        <button className={tab === 'course' ? 'active' : ''} onClick={() => setTab('course')}>Курс</button>
        <button className={tab === 'community' ? 'active' : ''} onClick={() => setTab('community')}>Сообщество</button>
      </nav>
      <div className="headerRight">{profile ? <><span className="pill">✦ {profile.points} баллов</span><button className="textButton" onClick={logout}>Выйти</button></> : <span className="pill">Башҡортса · Русский</span>}</div>
    </header>

    <main className="wrap">
      {tab === 'task' && <>
        <section className="hero">
          <div className="heroText"><div className="eyebrow"><span className="eyebrowLine"/> БЕРГӘ ТӘРЖЕМӘ ИТӘБЕҘ <span className="eyebrowLine"/></div><h1>Каждое слово<br /><em>имеет значение.</em></h1><p>Изучайте башкирский язык и помогайте сохранять его живым. Одно небольшое задание каждый день — большой вклад вместе.</p><div className="heroMeta"><span><b>01</b> задание в день</span><span><b>10</b> баллов за ответ</span><span><b>∞</b> поводов учиться</span></div></div>
          <div className="heroArt" aria-hidden="true"><span className="orb orbOne"/><span className="orb orbTwo"/><div className="artCard artCardBack">Һаумыһығыҙ!</div><div className="artCard artCardFront"><span>БАШҠОРТСА → РУССКИЙ</span><strong>Рәхмәт</strong><i>спасибо</i><div className="artLine"/></div><span className="artGlyph">ә</span></div>
        </section>
        <div className="sectionHeading"><div><span className="sectionIndex">01 / ПРАКТИКА</span><h2>Задание дня</h2></div><span className="dateBadge">{new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long' }).format(new Date())}</span></div>
        {!token ? <Auth onSuccess={authenticated} /> : <div className="taskLayout"><article className="taskCard">
          <div className="cardTop"><span className="languagePair">БАШКИРСКИЙ <span>→</span> РУССКИЙ</span><span className="taskNumber">Ежедневная практика</span></div>
          {task ? <><p className="taskLabel">Прочитайте фразу и проверьте перевод</p><div className="phrase">{task.text_ba}</div><div className="translation"><span>ПРЕДЛОЖЕННЫЙ ПЕРЕВОД</span><strong>{task.candidate}</strong></div>
          <div className="answerArea"><label htmlFor="answer">Предложить свой вариант</label><textarea id="answer" value={answer} onChange={e => setAnswer(e.target.value)} placeholder="Напишите перевод здесь…" maxLength={1000} rows={3} /><div className="actions"><button className="primary" disabled={busy || !answer.trim()} onClick={() => submit('correct')}>Отправить исправление <span>↗</span></button><button className="secondary" disabled={busy} onClick={() => submit('confirm')}>Верный перевод ✓</button></div></div>
          <p className="source">Источник: {task.source_title} · {task.attribution}{task.source_url && <> · <a href={task.source_url} target="_blank" rel="noreferrer">Открыть</a></>}</p>
          {task.submitted_text && <p className="saved">Ваш ответ сегодня: {task.submitted_text}</p>}</> : <p>Задание пока недоступно.</p>}
        </article><aside className="sideCard"><span className="sideIcon">✳</span><h3>Вместе точнее</h3><p>Мы сравниваем независимые ответы участников. Перевод становится подтверждённым, когда два человека предлагают один вариант.</p><div className="sideRule"/><span className="sideCaption">Ваш вклад сохраняет язык</span></aside></div>}
      </>}
      {tab === 'course' && <section className="contentPage"><span className="sectionIndex">02 / УЧИМСЯ</span><h1>Первые шаги</h1><p className="pageLead">Небольшие уроки помогут освоиться перед практикой перевода.</p>{courses[0] && <div className="courseIntro"><strong>{courses[0].title}</strong><span>{courses[0].description}</span></div>}<div className="lessons">{lessons.map(lesson => <article className="lesson" key={lesson.id}><span className="lessonNo">{String(lesson.position).padStart(2, '0')}</span><div><h3>{lesson.title}</h3><p>{lesson.body}</p></div><button disabled={!token || busy || progress.some(p => p.lesson_id === lesson.id)} onClick={() => complete(lesson.id)}>{progress.some(p => p.lesson_id === lesson.id) ? 'Пройдено ✓' : 'Отметить пройденным'}</button></article>)}</div>{!token && <p className="hint">Войдите на вкладке «Задание», чтобы сохранять прогресс.</p>}</section>}
      {tab === 'community' && <section className="contentPage"><span className="sectionIndex">03 / СООБЩЕСТВО</span><h1>Растём вместе</h1><p className="pageLead">Каждый ответ делает общую базу переводов точнее.</p><div className="communityGrid"><div className="panel"><h2>Рейтинг участников</h2>{leaders.length ? leaders.map((person, index) => <div className="leader" key={index}><span>{String(index + 1).padStart(2, '0')}</span><strong>{person.display_name}</strong><b>{person.points} ✦</b></div>) : <p>Пока нет ответов. Станьте первым участником!</p>}</div><div className="panel"><h2>Подтверждённые переводы</h2>{translations.length ? translations.map(item => <div className="word" key={item.fragment_id}><strong>{item.text_ba}</strong><span>{item.translation}</span></div>) : <p>Здесь появятся переводы после совпадения двух независимых ответов.</p>}<a className="exportLink" href="/api/translations/export">Скачать CSV ↗</a></div></div></section>}
      {message && <div className="toast" role="status"><button onClick={() => setMessage('')} aria-label="Закрыть">×</button>{message}</div>}
    </main><footer className="footer wrap"><span>© Башперевод · Сделано для живого языка</span><span>Башҡортса · Русский</span></footer>
  </div>;
}

function Auth({ onSuccess }) {
  const [mode, setMode] = useState('register');
  const [username, setUsername] = useState('');
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const send = async e => { e.preventDefault(); setBusy(true); setError(''); try { onSuccess(await api(`/api/auth/${mode}`, null, { method: 'POST', body: JSON.stringify({ username, password, displayName: name }) })); } catch (e) { setError(e.message); } finally { setBusy(false); } };
  return <div className="authLayout"><div className="authIntro"><span className="sparkle">✦</span><h3>Начните с одного слова</h3><p>Присоединяйтесь к сообществу, которое учится и помогает создавать проверенные переводы.</p><div className="authQuote">«Туған тел — халыҡтың күңеле»<small>Родной язык — душа народа</small></div></div><form className="authForm" onSubmit={send}><div className="authTabs"><button type="button" className={mode === 'register' ? 'selected' : ''} onClick={() => setMode('register')}>Регистрация</button><button type="button" className={mode === 'login' ? 'selected' : ''} onClick={() => setMode('login')}>Войти</button></div><h3>{mode === 'register' ? 'Создать аккаунт' : 'С возвращением'}</h3>{mode === 'register' && <label>Ваше имя<input value={name} onChange={e => setName(e.target.value)} placeholder="Как к вам обращаться" required maxLength={80} /></label>}<label>Логин<input value={username} onChange={e => setUsername(e.target.value)} placeholder="От 3 символов" required minLength={3} maxLength={32} /></label><label>Пароль<input type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="Не менее 8 символов" required minLength={8} /></label>{error && <p className="formError">{error}</p>}<button className="primary" disabled={busy}>{mode === 'register' ? 'Начать обучение' : 'Войти'} <span>↗</span></button></form></div>;
}

createRoot(document.getElementById('root')).render(<App />);
