const sessionKey = 'bashperevod.lms.session.v1';
const $ = selector => document.querySelector(selector);
const scormMode = new URLSearchParams(window.location.search).get('scorm') === '1';

let session = null;
try { session = JSON.parse(sessionStorage.getItem(sessionKey) || 'null'); } catch { session = null; }
let refreshPromise = null;
let courses = [];
let selectedCourse = null;
let lessons = [];
let completed = new Set();

function node(tag, text, className) {
  const element = document.createElement(tag);
  if (text !== undefined) element.textContent = text;
  if (className) element.className = className;
  return element;
}

function message(text, error = false) {
  $('#account-message').textContent = text;
  $('#account-message').classList.toggle('error', error);
}

function saveSession(value) {
  session = value;
  if (value) sessionStorage.setItem(sessionKey, JSON.stringify(value));
  else sessionStorage.removeItem(sessionKey);
  renderAccount();
}

function renderAccount() {
  $('#signed-out').hidden = Boolean(session?.accessToken);
  $('#signed-in').hidden = !session?.accessToken;
  $('#username').textContent = session?.username || '';
}

async function refreshSession() {
  if (!session?.refreshToken) throw new Error('Войдите снова, чтобы сохранить прогресс.');
  refreshPromise ||= (async () => {
    const response = await fetch('/auth/refresh', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: session.refreshToken })
    });
    if (!response.ok) {
      saveSession(null);
      throw new Error('Сессия закончилась. Войдите снова.');
    }
    saveSession({ ...session, ...await response.json() });
    return session.accessToken;
  })().finally(() => { refreshPromise = null; });
  return refreshPromise;
}

async function request(path, { method = 'GET', body, auth = false, retry = true } = {}) {
  if (!path.startsWith('/api/lms/') && !path.startsWith('/auth/')) throw new Error('Неизвестный маршрут.');
  if (auth && !session?.accessToken) throw new Error('Войдите, чтобы сохранить прогресс.');
  let response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(auth ? { Authorization: `Bearer ${session.accessToken}` } : {})
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) })
    });
  } catch {
    throw new Error('Не удалось связаться с сервисом. Попробуйте ещё раз.');
  }
  if (response.status === 401 && auth && retry && session?.refreshToken) {
    await refreshSession();
    return request(path, { method, body, auth, retry: false });
  }
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    if (response.status === 401) throw new Error('Войдите снова.');
    if (response.status === 400) throw new Error(data?.title || 'Проверьте логин и пароль.');
    throw new Error(data?.title || `Ошибка сервиса: ${response.status}.`);
  }
  return data;
}

function renderCourses() {
  const list = $('#course-list');
  list.replaceChildren();
  $('#course-count').textContent = `${courses.length} ${courses.length === 1 ? 'курс' : 'курсов'}`;
  if (!courses.length) {
    list.append(node('p', 'Пока нет доступных курсов.'));
    return;
  }
  for (const course of courses) {
    const card = node('button', undefined, 'course-card');
    card.type = 'button';
    card.append(node('span', 'ВВОДНЫЙ КУРС', 'eyebrow'), node('h3', course.title), node('p', course.description || 'Откройте уроки курса.'));
    const meta = node('div', undefined, 'course-meta');
    meta.append(node('span', `${course.lesson_count} уроков`), node('span', 'Открыть ↗'));
    card.append(meta);
    card.addEventListener('click', () => selectCourse(course).catch(error => message(error.message, true)));
    list.append(card);
  }
}

function renderLessons() {
  const list = $('#lesson-list');
  list.replaceChildren();
  const count = lessons.filter(lesson => completed.has(lesson.id)).length;
  $('#progress-label').textContent = `Пройдено ${count} из ${lessons.length}`;
  $('#progress-fill').style.width = lessons.length ? `${Math.round(count / lessons.length * 100)}%` : '0%';
  if (scormMode && session?.accessToken && lessons.length && window.parent !== window) {
    window.parent.postMessage({ type: 'BASHPEREVOD_SCORM_PROGRESS',
      score: Math.round(count / lessons.length * 100), completed: count === lessons.length }, '*');
  }
  if (!lessons.length) list.append(node('p', 'В этом курсе пока нет уроков.'));
  for (const lesson of lessons) {
    const card = node('article', undefined, 'lesson');
    const heading = node('div', undefined, 'lesson-top');
    heading.append(node('span', String(lesson.position).padStart(2, '0'), 'lesson-number'), node('h3', lesson.title));
    card.append(heading, node('p', lesson.body));
    if (completed.has(lesson.id)) card.append(node('span', '✓ Урок пройден', 'completed'));
    else {
      const button = node('button', session?.accessToken ? 'Завершить урок' : 'Войти для сохранения', session?.accessToken ? 'primary' : 'secondary');
      button.type = 'button';
      button.addEventListener('click', async () => {
        if (!session?.accessToken) { $('#auth-form').elements.username.focus(); return; }
        button.disabled = true;
        try {
          await request(`/api/lms/lessons/${lesson.id}/complete`, { method: 'POST', auth: true });
          completed.add(lesson.id);
          renderLessons();
          message('Прогресс сохранён.');
        } catch (error) {
          button.disabled = false;
          message(error.message, true);
        }
      });
      card.append(button);
    }
    list.append(card);
  }
}

async function loadProgress() {
  if (!session?.accessToken) { completed = new Set(); return; }
  const progress = await request('/api/lms/me/progress', { auth: true });
  completed = new Set(progress.map(item => item.lesson_id));
}

async function selectCourse(course) {
  selectedCourse = course;
  $('#course-list').hidden = true;
  $('#course-detail').hidden = false;
  $('#detail-title').textContent = course.title;
  $('#detail-description').textContent = course.description || '';
  $('#lesson-list').replaceChildren(node('p', 'Загружаем уроки…'));
  try {
    lessons = await request(`/api/lms/courses/${course.id}/lessons`);
    try { await loadProgress(); } catch (error) { completed = new Set(); message(error.message, true); }
    renderLessons();
  } catch (error) {
    $('#lesson-list').replaceChildren(node('p', error.message));
    throw error;
  }
}

$('#back-to-courses').addEventListener('click', () => {
  selectedCourse = null;
  $('#course-detail').hidden = true;
  $('#course-list').hidden = false;
});

$('#auth-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const username = form.elements.username.value.trim();
  const password = form.elements.password.value;
  const mode = event.submitter?.value || 'login';
  if (mode === 'register' && password.length < 12) { message('Для нового аккаунта нужен пароль от 12 символов.', true); return; }
  for (const button of form.querySelectorAll('button')) button.disabled = true;
  message('Проверяем аккаунт…');
  try {
    if (mode === 'register') await request('/auth/register', { method: 'POST', body: { username, password } });
    const value = await request('/auth/sign-in', { method: 'POST', body: { username, password } });
    saveSession({ ...value, username });
    form.elements.password.value = '';
    message(mode === 'register' ? 'Аккаунт создан. Прогресс теперь сохраняется.' : 'Вы вошли. Прогресс теперь сохраняется.');
    if (selectedCourse) await selectCourse(selectedCourse);
  } catch (error) { message(error.message, true); }
  finally { for (const button of form.querySelectorAll('button')) button.disabled = false; }
});

$('#logout').addEventListener('click', () => {
  saveSession(null);
  completed = new Set();
  if (selectedCourse) renderLessons();
  message('Вы вышли из аккаунта. Курсы по-прежнему доступны для чтения.');
});

renderAccount();
request('/api/lms/courses')
  .then(data => {
    courses = data;
    renderCourses();
    if (scormMode && courses.length) selectCourse(courses[0]).catch(error => message(error.message, true));
  })
  .catch(error => { $('#course-list').replaceChildren(node('p', error.message)); });
