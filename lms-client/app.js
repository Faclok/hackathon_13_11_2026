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
  if (value) loadWorkspace();
  else resetWorkspace();
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
  if (auth && !session?.accessToken) throw new Error('Войдите в аккаунт.');
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
    if (response.status === 400) throw new Error(data?.title || 'Проверьте введённые данные.');
    if (response.status === 409) throw new Error(data?.title || 'Данные уже изменились. Обновите страницу.');
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
    message(mode === 'register' ? 'Аккаунт создан. Можно создавать классы.' : 'Вы вошли в учебный кабинет.');
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

let teacherClasses = [];
let selectedClassId = null;
let members = [];
let classTasks = [];
let ownTranslations = [];
let catalog = [];
let catalogOffset = 0;
let editingTranslationId = null;
let editingTaskId = null;

function workspaceNotice(value, error = false) {
  const target = $('#workspace-message');
  target.textContent = value;
  target.classList.toggle('error', error);
}

function resetWorkspace() {
  $('#workspace').hidden = true;
  selectedClassId = null;
  teacherClasses = []; members = []; classTasks = []; ownTranslations = []; catalog = [];
}

async function loadWorkspace() {
  if (!session?.accessToken) { resetWorkspace(); return; }
  $('#workspace').hidden = false;
  try {
    const [profile, classes, own, studentTasks] = await Promise.all([
      request('/api/lms/teacher/me', { auth: true }),
      request('/api/lms/teacher/classes', { auth: true }),
      request('/api/lms/teacher/translations', { auth: true }),
      request('/api/lms/me/tasks', { auth: true })
    ]);
    $('#teacher-name').textContent = profile.username;
    teacherClasses = classes;
    ownTranslations = own;
    renderTeacherClasses(); renderOwnTranslations(); renderStudentTasks(studentTasks);
    if (selectedClassId && teacherClasses.some(item => item.id === selectedClassId)) await selectClass(selectedClassId);
    else if (teacherClasses.length) await selectClass(teacherClasses[0].id);
    else { selectedClassId = null; $('#class-detail').hidden = true; }
    if (!catalog.length) await loadCatalog();
  } catch (error) { workspaceNotice(error.message, true); }
}

function renderTeacherClasses() {
  const target = $('#teacher-classes');
  target.replaceChildren();
  if (!teacherClasses.length) target.append(node('p', 'Пока нет классов. Создайте первый.'));
  for (const item of teacherClasses) {
    const button = node('button', undefined, 'workspace-item selectable');
    button.type = 'button';
    if (item.id === selectedClassId) button.classList.add('selected');
    button.append(node('strong', item.name), node('span', item.description || 'Открыть класс'));
    button.addEventListener('click', () => selectClass(item.id).catch(error => workspaceNotice(error.message, true)));
    target.append(button);
  }
}

async function selectClass(id) {
  const item = teacherClasses.find(value => value.id === id);
  if (!item) return;
  selectedClassId = id;
  $('#class-detail').hidden = false;
  $('#class-title').textContent = item.name;
  $('#edit-class-form').elements.name.value = item.name;
  $('#edit-class-form').elements.description.value = item.description || '';
  renderTeacherClasses();
  [members, classTasks] = await Promise.all([
    request(`/api/lms/teacher/classes/${id}/members`, { auth: true }),
    request(`/api/lms/teacher/classes/${id}/tasks`, { auth: true })
  ]);
  renderMembers(); renderTaskOptions(); renderClassTasks(); renderCatalog();
}

function renderMembers() {
  const target = $('#member-list'); target.replaceChildren();
  if (!members.length) target.append(node('p', 'В классе пока нет учеников.'));
  for (const member of members) {
    const row = node('div', undefined, 'workspace-item row');
    row.append(node('strong', member.displayName || member.username));
    const edit = node('button', 'Имя', 'secondary'); edit.type = 'button';
    edit.addEventListener('click', async () => {
      const displayName = prompt('Имя ученика в этом классе', member.displayName || member.username);
      if (displayName === null) return;
      try {
        await request(`/api/lms/teacher/classes/${selectedClassId}/members/${member.userId}`, {
          method: 'PUT', auth: true, body: { displayName }
        });
        await selectClass(selectedClassId); workspaceNotice('Имя участника обновлено.');
      } catch (error) { workspaceNotice(error.message, true); }
    });
    const remove = node('button', 'Убрать', 'text-danger'); remove.type = 'button';
    remove.addEventListener('click', async () => {
      if (!confirm(`Убрать ${member.username} из класса?`)) return;
      try {
        await request(`/api/lms/teacher/classes/${selectedClassId}/members/${member.userId}`, { method: 'DELETE', auth: true });
        await selectClass(selectedClassId); workspaceNotice('Участник удалён из класса.');
      } catch (error) { workspaceNotice(error.message, true); }
    });
    row.append(edit, remove); target.append(row);
  }
}

function renderTaskOptions(selected = '') {
  const select = $('#task-form').elements.source;
  select.replaceChildren();
  const empty = node('option', 'Выберите перевод'); empty.value = ''; select.append(empty);
  for (const item of ownTranslations) {
    const option = node('option', `Свой · ${item.sourceText.slice(0, 75)}`);
    option.value = `own:${item.id}`; select.append(option);
  }
  for (const item of catalog) {
    const option = node('option', `Каталог · ${item.sourceText.slice(0, 65)} · ${statusLabel(item.status)} · ★ ${item.rating}`);
    option.value = `catalog:${item.id}`; select.append(option);
  }
  if (selected && !Array.from(select.options).some(option => option.value === selected)) {
    const task = classTasks.find(item => item.id === editingTaskId);
    const option = node('option', `Выбранный перевод · ${task?.sourceText || ''}`); option.value = selected; select.append(option);
  }
  select.value = selected;
}

function statusLabel(status) {
  return ({ confirmed: 'Подтверждён', expert_review: 'На экспертизе', pending: 'Ожидает оценки', own: 'Свой' })[status] || status;
}

function renderClassTasks() {
  const target = $('#task-list'); target.replaceChildren();
  if (!classTasks.length) target.append(node('p', 'Заданий пока нет.'));
  for (const task of classTasks) {
    const card = node('article', undefined, 'workspace-item');
    card.append(node('strong', task.title), node('span', task.sourceText));
    const meta = node('small', `${statusLabel(task.translationStatus)} · рейтинг ${task.translationRating}${task.dueAt ? ` · до ${new Date(task.dueAt).toLocaleString('ru-RU')}` : ''}`);
    card.append(meta);
    const actions = node('div', undefined, 'item-actions');
    const edit = node('button', 'Изменить', 'secondary'); edit.type = 'button';
    edit.addEventListener('click', () => {
      editingTaskId = task.id;
      const form = $('#task-form'); form.elements.title.value = task.title;
      form.elements.dueAt.value = task.dueAt ? new Date(new Date(task.dueAt).getTime() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16) : '';
      renderTaskOptions(task.teacherTranslationId ? `own:${task.teacherTranslationId}` : `catalog:${task.catalogCaseId}`);
      $('#task-submit').textContent = 'Сохранить задание'; $('#task-cancel').hidden = false;
      form.scrollIntoView({ behavior: 'smooth', block: 'center' });
    });
    const show = node('button', 'Ответы', 'secondary'); show.type = 'button';
    show.addEventListener('click', async () => {
      try {
        const answers = await request(`/api/lms/teacher/classes/${selectedClassId}/tasks/${task.id}/submissions`, { auth: true });
        const list = node('div', undefined, 'answer-list');
        if (!answers.length) list.append(node('p', 'Пока нет ответов.'));
        for (const answer of answers) list.append(node('p', `${answer.username}: ${answer.answer}`));
        card.querySelector('.answer-list')?.remove(); card.append(list);
      } catch (error) { workspaceNotice(error.message, true); }
    });
    const remove = node('button', 'Удалить', 'text-danger'); remove.type = 'button';
    remove.addEventListener('click', async () => {
      if (!confirm(`Удалить задание «${task.title}» вместе с ответами?`)) return;
      try {
        await request(`/api/lms/teacher/classes/${selectedClassId}/tasks/${task.id}`, { method: 'DELETE', auth: true });
        await selectClass(selectedClassId); workspaceNotice('Задание удалено.');
      } catch (error) { workspaceNotice(error.message, true); }
    });
    actions.append(edit, show, remove); card.append(actions); target.append(card);
  }
}

function renderOwnTranslations() {
  const target = $('#own-translations'); target.replaceChildren();
  if (!ownTranslations.length) target.append(node('p', 'Своих переводов пока нет.'));
  for (const item of ownTranslations) {
    const card = node('article', undefined, 'workspace-item');
    card.append(node('strong', item.sourceText), node('span', item.translationText), node('small', item.languagePair));
    const actions = node('div', undefined, 'item-actions');
    const edit = node('button', 'Изменить', 'secondary'); edit.type = 'button';
    edit.addEventListener('click', () => {
      editingTranslationId = item.id;
      const form = $('#translation-form'); form.elements.sourceText.value = item.sourceText;
      form.elements.translationText.value = item.translationText;
      form.elements.languagePair.value = item.languagePair;
      $('#translation-submit').textContent = 'Сохранить перевод'; $('#translation-cancel').hidden = false;
      form.scrollIntoView({ behavior: 'smooth', block: 'center' });
    });
    const remove = node('button', 'Удалить', 'text-danger'); remove.type = 'button';
    remove.addEventListener('click', async () => {
      if (!confirm('Удалить свой перевод? Уже выданные задания сохранят текст.')) return;
      try {
        await request(`/api/lms/teacher/translations/${item.id}`, { method: 'DELETE', auth: true });
        ownTranslations = await request('/api/lms/teacher/translations', { auth: true });
        renderOwnTranslations(); renderTaskOptions(); workspaceNotice('Перевод удалён.');
      } catch (error) { workspaceNotice(error.message, true); }
    });
    actions.append(edit, remove); card.append(actions); target.append(card);
  }
}

function renderCatalog() {
  const target = $('#catalog-list'); target.replaceChildren();
  const query = $('#catalog-search').value.trim().toLocaleLowerCase('ru');
  const matching = catalog.filter(item => `${item.sourceText} ${item.translationText}`.toLocaleLowerCase('ru').includes(query));
  if (!matching.length) target.append(node('p', catalog.length ? 'Совпадений среди загруженных переводов нет.' : 'Каталог пуст.'));
  for (const item of matching) {
    const card = node('article', undefined, 'workspace-item');
    card.append(node('strong', item.sourceText), node('span', item.translationText),
      node('small', `${statusLabel(item.status)} · рейтинг ${item.rating} · ${item.languagePair}`));
    const use = node('button', 'Выбрать для задания', 'secondary'); use.type = 'button';
    use.disabled = !selectedClassId;
    use.addEventListener('click', () => {
      renderTaskOptions(`catalog:${item.id}`);
      $('#task-form').scrollIntoView({ behavior: 'smooth', block: 'center' });
    });
    card.append(use); target.append(card);
  }
}

async function loadCatalog() {
  const selected = $('#task-form').elements.source.value;
  const page = await request(`/api/lms/teacher/catalog?offset=${catalogOffset}&limit=50`, { auth: true });
  catalog.push(...page); catalogOffset += page.length;
  $('#catalog-more').hidden = page.length < 50;
  renderCatalog(); renderTaskOptions(selected);
}

function renderStudentTasks(tasks) {
  const target = $('#student-tasks'); target.replaceChildren();
  if (!tasks.length) target.append(node('p', 'Вам пока не выдавали заданий.'));
  for (const task of tasks) {
    const card = node('article', undefined, 'workspace-item');
    card.append(node('small', task.className), node('strong', task.title), node('span', task.sourceText));
    const form = node('form', undefined, 'inline-form');
    const answer = node('textarea'); answer.name = 'answer'; answer.maxLength = 5000; answer.required = true;
    answer.placeholder = 'Ваш перевод'; answer.value = task.answer || '';
    const submit = node('button', task.submittedAt ? 'Обновить ответ' : 'Отправить ответ', 'primary');
    const expired = task.dueAt && new Date(task.dueAt) < new Date();
    submit.disabled = Boolean(expired);
    form.append(answer, submit);
    form.addEventListener('submit', async event => {
      event.preventDefault(); submit.disabled = true;
      try {
        await request(`/api/lms/me/tasks/${task.id}/submission`, { method: 'PUT', auth: true, body: { answer: answer.value } });
        workspaceNotice('Ответ сохранён.'); submit.textContent = 'Обновить ответ';
      } catch (error) { workspaceNotice(error.message, true); }
      finally { submit.disabled = Boolean(expired); }
    });
    if (task.dueAt) card.append(node('small', `Срок: ${new Date(task.dueAt).toLocaleString('ru-RU')}${expired ? ' · истёк' : ''}`));
    card.append(form); target.append(card);
  }
}

$('#class-form').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget;
  try {
    const created = await request('/api/lms/teacher/classes', { method: 'POST', auth: true,
      body: { name: form.elements.name.value, description: form.elements.description.value } });
    form.reset(); teacherClasses = await request('/api/lms/teacher/classes', { auth: true });
    await selectClass(created.id); workspaceNotice('Класс создан.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#edit-class-form').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget;
  try {
    await request(`/api/lms/teacher/classes/${selectedClassId}`, { method: 'PUT', auth: true,
      body: { name: form.elements.name.value, description: form.elements.description.value } });
    teacherClasses = await request('/api/lms/teacher/classes', { auth: true });
    await selectClass(selectedClassId); workspaceNotice('Класс обновлён.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#delete-class').addEventListener('click', async () => {
  if (!confirm('Удалить класс, его задания и ответы учеников?')) return;
  try {
    await request(`/api/lms/teacher/classes/${selectedClassId}`, { method: 'DELETE', auth: true });
    selectedClassId = null; await loadWorkspace(); workspaceNotice('Класс удалён.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#member-form').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget;
  try {
    await request(`/api/lms/teacher/classes/${selectedClassId}/members`, { method: 'POST', auth: true,
      body: { username: form.elements.username.value } });
    form.reset(); await selectClass(selectedClassId); workspaceNotice('Участник добавлен.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#translation-form').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget;
  const body = { sourceText: form.elements.sourceText.value, translationText: form.elements.translationText.value,
    languagePair: form.elements.languagePair.value };
  try {
    await request(editingTranslationId ? `/api/lms/teacher/translations/${editingTranslationId}` : '/api/lms/teacher/translations',
      { method: editingTranslationId ? 'PUT' : 'POST', auth: true, body });
    editingTranslationId = null; form.reset(); $('#translation-submit').textContent = 'Добавить перевод';
    $('#translation-cancel').hidden = true;
    ownTranslations = await request('/api/lms/teacher/translations', { auth: true });
    renderOwnTranslations(); renderTaskOptions(); workspaceNotice('Перевод сохранён.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#translation-cancel').addEventListener('click', () => {
  editingTranslationId = null; $('#translation-form').reset();
  $('#translation-submit').textContent = 'Добавить перевод'; $('#translation-cancel').hidden = true;
});
$('#task-form').addEventListener('submit', async event => {
  event.preventDefault(); const form = event.currentTarget;
  const [sourceType, translationId] = form.elements.source.value.split(':');
  const body = { title: form.elements.title.value, sourceType, translationId,
    dueAt: form.elements.dueAt.value ? new Date(form.elements.dueAt.value).toISOString() : null };
  try {
    await request(editingTaskId ? `/api/lms/teacher/classes/${selectedClassId}/tasks/${editingTaskId}` :
      `/api/lms/teacher/classes/${selectedClassId}/tasks`, { method: editingTaskId ? 'PUT' : 'POST', auth: true, body });
    editingTaskId = null; form.reset(); $('#task-submit').textContent = 'Выдать задание'; $('#task-cancel').hidden = true;
    await selectClass(selectedClassId); workspaceNotice('Задание сохранено.');
  } catch (error) { workspaceNotice(error.message, true); }
});
$('#task-cancel').addEventListener('click', () => {
  editingTaskId = null; $('#task-form').reset(); renderTaskOptions();
  $('#task-submit').textContent = 'Выдать задание'; $('#task-cancel').hidden = true;
});
$('#catalog-search').addEventListener('input', renderCatalog);
$('#catalog-more').addEventListener('click', () => loadCatalog().catch(error => workspaceNotice(error.message, true)));
if (session?.accessToken) loadWorkspace();
