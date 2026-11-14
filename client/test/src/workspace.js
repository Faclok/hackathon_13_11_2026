import './workspace.css';
import { workspaceMarkup, roles } from './workspace-layout.js';
import { mountWorkspace } from './workspace-view.js';
import { exerciseKinds, renderExercise, readExerciseAnswer } from './exercise-view.js';
import exampleLesson from './examples/daily-lesson.json';
import exampleChoice from './examples/review-choice.json';
import exampleConfirm from './examples/review-confirm.json';

const $ = selector => document.querySelector(selector);
const freshState = () => ({ actors: [], active: '', workspaceRole: 'participant', schoolId: '', classId: '', caseId: '', accountId: '', assignments: {} });
const storageKey = 'bashperevod.workspace.v1';
let state;
try { state = { ...freshState(), ...JSON.parse(sessionStorage.getItem(storageKey) || '{}') }; } catch { state = freshState(); }
let busy = false;
let caseView = null;
let courseId = null;
let ui;
let currentTask = {};
let previewAssignment = null;
const refreshes = new Map();
const actor = () => state.actors.find(item => item.id === state.active);
const element = (tag, text, props = {}) => Object.assign(document.createElement(tag), { textContent: text, ...props });

$('#root').innerHTML = workspaceMarkup();
for (const [kind, title] of Object.entries(exerciseKinds)) $('#exercise-kind').append(element('option', title, { value: kind }));

function save() {
  try { sessionStorage.setItem(storageKey, JSON.stringify(state)); }
  catch { $('#storage-note').textContent = 'Хранилище вкладки недоступно. После обновления потребуется войти заново.'; }
}
function redact(value) {
  if (Array.isArray(value)) return value.map(redact);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) =>
    [key, /^(password|currentPassword|newPassword|token|accessToken|refreshToken|signingKey)$/i.test(key) ? '[скрыто]' : redact(item)]));
  return value;
}
function show(id, value) {
  $(`#${id}`).textContent = typeof value === 'string' ? value : JSON.stringify(redact(value), null, 2);
  ui?.present(id, value, currentTask);
}
function log(method, path, status, value) { $('#log').textContent = (`${new Date().toLocaleTimeString()} ${method} ${path} → ${status}\n${JSON.stringify(redact(value), null, 2)}\n\n` + $('#log').textContent).slice(0, 60000); }
function requireActor() { if (!actor()) throw new Error('Войдите и выберите аккаунт сверху.'); return actor(); }
function context(values) {
  Object.assign(state, values); save();
  Object.entries(values).forEach(([key, value]) => document.querySelectorAll(`[data-context="${key}"]`).forEach(item => { item.value = value || ''; }));
  ui?.syncContext();
}
function renderActors() {
  $('#actor').replaceChildren(element('option', 'Не выбран', { value: '' }));
  state.actors.forEach(item => $('#actor').append(element('option', `${item.username}${roles[item.workspaceRole] ? ' — ' + roles[item.workspaceRole] : ''}`, { value: item.id })));
  $('#actor').value = state.active;
  $('#identity').textContent = actor() ? `Вы вошли как ${actor().username}` : 'Вы ещё не вошли';
  ui?.sync();
}
function selectActor(id) {
  state.active = id; caseView = null; previewAssignment = null; $('#lesson-preview-note').hidden = true; save(); renderActors(); renderAssignment();
  for (const target of ['variants', 'exercises', 'sessions', 'lms-lessons', 'access-list', 'queue', 'class-invitations', 'my-class-invitations']) $(`#${target}`).replaceChildren();
  $('#my-class-info').textContent = 'Обновите приглашения под выбранным аккаунтом.';
  $('#case-view').textContent = 'Откройте случай под текущим экспертом.';
  show('profile-result', 'Нажмите «Мой профиль», чтобы прочитать актуальные данные.');
  for (const target of ['review-result', 'expert-result', 'learning-result', 'lms-result', 'manage-result', 'directory-result', 'class-invitation-result', 'my-class-result']) show(target, 'Прочитайте данные под выбранным аккаунтом.');
  $('#setup-token').value = '';
  $('.expert-actions').hidden = true;
  ui?.sync({ actorChanged: true });
}
function remember(username, session, workspaceRole) {
  const known = state.actors.find(item => item.username === username);
  const current = { id: known?.id || session.sessionId, username, ...session,
    workspaceRole: workspaceRole || known?.workspaceRole || actor()?.workspaceRole || state.workspaceRole || 'participant' };
  state.actors = state.actors.filter(item => item.username !== username).concat(current); save(); renderActors(); return current;
}
async function request(path, { method = 'GET', body, current = actor(), auth = true, headers = {}, retry = true, timeoutMs = 30000 } = {}) {
  if (!/^\/(api|auth|health)\//.test(path)) throw new Error('Разрешено только API текущего сайта.');
  if (auth && !current) throw new Error('Для этого действия нужен вход.');
  const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), timeoutMs);
  let response, data;
  try {
    response = await fetch(path, { method, credentials: 'omit', signal: controller.signal,
      headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(auth ? { Authorization: `Bearer ${current.accessToken}` } : {}), ...headers },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const text = await response.text(); try { data = text ? JSON.parse(text) : null; } catch { data = text.slice(0, 2000); }
  } catch (error) {
    const message = error.name === 'AbortError' ? 'Истекло время ожидания.' : 'Ошибка сети.';
    log(method, path, 'сеть', message); throw new Error(message + ' Изменение могло сохраниться. Проверьте состояние перед повтором.');
  } finally { clearTimeout(timeout); }
  log(method, path, response.status, data);
  if (response.status === 401 && auth && current.refreshToken && retry) { await refresh(current); return request(path, { method, body, current, auth, headers, retry: false, timeoutMs }); }
  if (!response.ok) {
    const hint = { 400: 'Проверьте заполненные поля и требования к паролю.', 401: 'Не удалось подтвердить вход. Проверьте логин и пароль или войдите заново.',
      403: 'У выбранного аккаунта нет прав на это действие. Выберите подходящий аккаунт сверху; смена рабочего пространства не выдаёт права.',
      404: 'Запись не найдена. Обновите список и выберите её заново.', 409: 'Данные уже изменились. Обновите их перед повтором действия.',
      429: 'Слишком много запросов. Подождите минуту и попробуйте снова.' }[response.status] || 'Сервис не смог выполнить действие. Проверьте подключение.';
    throw new Error(hint + (response.status === 400 && data?.title ? ` Подробности: ${data.title}` : ''));
  }
  if (response.status !== 204 && typeof data === 'string') throw new Error('Получен текст или HTML вместо JSON. Проверьте маршруты Nginx и версию API.');
  return data;
}
async function refresh(current) {
  if (!refreshes.has(current.id)) refreshes.set(current.id, (async () => {
    const session = await request('/auth/refresh', { method: 'POST', auth: false, body: { refreshToken: current.refreshToken } });
    Object.assign(current, session); save();
  })().finally(() => refreshes.delete(current.id)));
  return refreshes.get(current.id);
}
async function login(username, password, workspaceRole) { return remember(username, await request('/auth/sign-in', { method: 'POST', auth: false, body: { username, password } }), workspaceRole); }
async function setPassword(accountId, password, owner) {
  const setup = await request(`/api/accounts/${accountId}/password-link`, { method: 'POST', current: owner });
  await request('/auth/password', { method: 'POST', auth: false, body: { accountId, token: setup.token, password } });
}
async function staff(values, owner) {
  const account = await request('/api/staff', { method: 'POST', current: owner, body: { username: values.username, profile: values.profile, schoolId: values.profile === 'teacher' ? values.schoolId : null } });
  context({ accountId: account.id }); await setPassword(account.id, values.password, owner); await login(values.username, values.password, values.profile); return account;
}
function renderAssignment() {
  const assignment = previewAssignment || state.assignments[state.active]; $('#assignment').replaceChildren();
  $('.review-actions').hidden = !assignment;
  $('#review-preview-note').hidden = !previewAssignment;
  $('form[data-action="confirm"] button').textContent = assignment?.kind === 'translation-choice' ? 'Отправить выбранный перевод' : 'Перевод верный';
  if (!assignment) { $('#assignment').append(element('h2', 'Начните с одной фразы'), element('p', 'Нажмите «Получить задание». Здесь появятся оригинал и предложенный перевод.')); return; }
  $('#assignment').append(element('p', 'Оригинал', { className: 'phrase-label' }), element('h2', assignment.original));
  if (assignment.kind === 'translation-choice') {
    $('#assignment').append(element('p', 'Выберите один перевод или предложите свой ниже.', { className: 'hint' }));
    const options = element('div', '', { className: 'answer-options' });
    for (const option of assignment.options || []) {
      const label = element('label', '', { className: 'answer-option' });
      label.append(element('input', '', { type: 'radio', name: 'review-option', value: option.id }), document.createTextNode(option.text)); options.append(label);
    }
    $('#assignment').append(options);
  } else if (!assignment.kind || assignment.kind === 'translation-confirm') {
    $('#assignment').append(element('p', 'Предложенный перевод', { className: 'phrase-label' }), element('p', assignment.translation, { className: 'translation-text' }));
  } else {
    $('#assignment').append(element('p', 'Этот вид проверки пока не поддерживается. Обновите клиент.', { className: 'notice' }));
    $('.review-actions').hidden = true;
  }
  if (!previewAssignment) $('#assignment').append(element('p', `Ответьте до ${new Date(assignment.expiresAt).toLocaleString('ru-RU')}`, { className: 'hint' }));
}
async function submitReview(reviewAction, correction) {
  const assignment = previewAssignment || state.assignments[state.active]; if (!assignment) throw new Error('Сначала получите свою выдачу.');
  const body = { action: reviewAction };
  if (reviewAction === 'Correct') body.correction = correction;
  if (reviewAction === 'Confirm' && assignment.kind === 'translation-choice') {
    const selected = $('#assignment input[name="review-option"]:checked');
    if (!selected) throw new Error('Выберите один из переводов или напишите свой вариант.');
    body.selectedVariantId = selected.value;
  }
  if (previewAssignment) { show('review-result', { message: 'Предпросмотр: ответ подготовлен, в API не отправлен.', request: body }); return; }
  const current = requireActor();
  const result = await request(`/api/reviews/${assignment.id}/response`, { method: 'POST', body });
  delete state.assignments[current.id]; save(); renderAssignment(); show('review-result', result);
}
async function readCase(id) {
  const view = await request(`/api/expert/cases/${id}`); context({ caseId: id }); caseView = { ...view, actor: state.active };
  $('#case-view').replaceChildren(element('p', view.closed ? 'Проверка завершена' : 'Оригинал для проверки', { className: 'phrase-label' }), element('h2', view.original));
  $('.expert-actions').hidden = view.closed;
  $('#variants').replaceChildren();
  view.variants.forEach(variant => {
    const label = element('label', `${variant.text} — поддержали ${variant.supports}, доля ${variant.supportRatio == null ? 'пока не определена' : Math.round(variant.supportRatio * 100) + '%'}`);
    const select = element('select', '', { name: variant.id, disabled: view.closed });
    [['none', 'Не рассмотрен'], ['primary', 'Основной допустимый'], ['additional', 'Дополнительный допустимый'], ['rejected', 'Явно неверный']].forEach(([value, text]) => select.append(element('option', text, { value })));
    if (view.resolution?.primaryVariantId === variant.id) select.value = 'primary';
    else if (view.resolution?.acceptedVariantIds.includes(variant.id)) select.value = 'additional';
    else if (view.resolution?.rejectedVariantIds.includes(variant.id)) select.value = 'rejected';
    label.append(select); $('#variants').append(label);
  }); show('expert-result', view);
}
function requireCase() { if (!caseView || caseView.actor !== state.active) throw new Error('Прочитайте случай под текущим экспертом.'); if (caseView.closed) throw new Error('Случай уже закрыт.'); return caseView; }
async function lesson() {
  const data = await request('/api/lessons/today'); renderLesson(data); show('learning-result', data);
}
function renderLesson(data, preview = false) {
  $('#lesson-preview-note').hidden = !preview;
  const completed = data.exercises.filter(exercise => exercise.completed).length;
  $('#exercises').replaceChildren(element('p', `${preview ? 'Пример урока' : 'Дата: ' + data.date}. Выполнено ${completed} из ${data.exercises.length}.`, { className: 'lesson-progress' }));
  for (const exercise of data.exercises) $('#exercises').append(renderExercise(exercise, { preview, actorId: state.active }));
  const boss = element('section', '', { className: 'exercise-card' });
  boss.append(element('h3', 'Финальный босс — проверка перевода'));
  if (data.bossCompleted) {
    boss.append(element('p', 'Босс пройден. Ежедневные задания завершены.'));
  } else {
    boss.append(element('p', completed < data.exercises.length
      ? 'Сначала выполните учебные упражнения. Последнее задание — подтвердить или исправить перевод реального фрагмента.'
      : 'Упражнения выполнены. Подтвердите или исправьте перевод, чтобы завершить дневной урок.'));
    const start = element('button', 'Открыть или продолжить босса', { type: 'button', disabled: preview || completed < data.exercises.length });
    start.dataset.action = 'lesson-boss';
    boss.append(start, element('div', '', { id: 'daily-boss-body' }));
  }
  $('#exercises').append(boss);
}
function renderDailyBoss(assignment) {
  const target = $('#daily-boss-body'); target.replaceChildren();
  target.append(element('h3', assignment.original), element('p', assignment.translation));
  const confirm = element('form'); confirm.dataset.action = 'daily-boss-answer'; confirm.dataset.actor = state.active;
  confirm.append(element('input', '', { type: 'hidden', name: 'assignmentId', value: assignment.id }),
    element('input', '', { type: 'hidden', name: 'action', value: 'Confirm' }));
  if (assignment.kind === 'translation-choice') {
    const options = element('select', '', { name: 'selectedVariantId', required: true });
    for (const option of assignment.options || []) options.append(element('option', option.text, { value: option.id }));
    confirm.append(options);
  }
  confirm.append(element('button', 'Подтвердить перевод', { type: 'submit' }));
  const correct = element('form'); correct.dataset.action = 'daily-boss-answer'; correct.dataset.actor = state.active;
  const label = element('label', 'Ваш перевод');
  label.append(element('textarea', '', { name: 'correction', required: true, rows: 3, maxLength: 1000 }));
  correct.append(element('input', '', { type: 'hidden', name: 'assignmentId', value: assignment.id }),
    element('input', '', { type: 'hidden', name: 'action', value: 'Correct' }), label,
    element('button', 'Отправить исправление', { type: 'submit' }));
  target.append(confirm, correct);
}
async function lmsLessons(id) {
  courseId = id;
  const lessons = await request(`/api/lms/courses/${id}/lessons`, { auth: false }); const progress = actor() ? await request('/api/lms/me/progress') : [];
  $('#lms-lessons').replaceChildren();
  lessons.forEach(item => {
    const form = element('form', ''); form.dataset.action = 'lms-complete'; form.dataset.id = item.id;
    const done = progress.some(entry => entry.lesson_id === item.id);
    form.append(element('h3', `${item.position}. ${item.title}`), element('p', item.body), element('button', done ? 'Уже пройдено' : 'Завершить урок', { disabled: done || !actor() })); $('#lms-lessons').append(form);
  }); show('lms-result', { lessons, progress });
}

const invitationStatuses = { Pending: 'Ожидает ответа ученика', Accepted: 'Принято — ученик в классе', Declined: 'Ученик отклонил', Cancelled: 'Отменено — выбран другой класс' };
const invitationPageSize = 50;
function renderInvitations(items, { student, classId = '', offset = 0 }) {
  const target = $(student ? '#my-class-invitations' : '#class-invitations'); target.replaceChildren();
  if (!items.length) target.append(element('p', offset ? 'На этой странице приглашений нет. Вернитесь к предыдущей.' : student
    ? 'Приглашений пока нет. Передайте учителю свой логин и обновите список после отправки приглашения.'
    : 'Приглашений в этот класс пока нет. Укажите логин ученика в форме выше.'));
  items.forEach(item => {
    const card = element('article', '', { className: 'invitation-card' }); card.dataset.invitationId = item.id;
    card.append(element('h2', student ? `Класс «${item.className}»` : item.participantUsername),
      element('p', `${item.schoolName} · ${item.className}`), element('p', `Учитель: ${item.teacherUsername}`),
      element('p', invitationStatuses[item.status] || item.status, { className: 'invitation-status' }),
      element('p', `Отправлено: ${new Date(item.createdAt).toLocaleString('ru-RU')}`, { className: 'hint' }));
    if (student && item.status === 'Pending') {
      const form = element('form', ''); form.dataset.action = 'invitation-accept'; form.dataset.id = item.id;
      const consent = element('label', '', { className: 'check-label' });
      consent.append(element('input', '', { type: 'checkbox', required: true, name: 'consent' }),
        element('span', `Хочу вступить в класс «${item.className}» (${item.schoolName}) к учителю ${item.teacherUsername}. После вступления этот учитель сможет восстанавливать доступ к моему аккаунту.`));
      const buttons = element('div', '', { className: 'invitation-actions' });
      const decline = element('button', 'Отклонить', { type: 'button' }); decline.dataset.action = 'invitation-decline'; decline.dataset.id = item.id;
      buttons.append(element('button', 'Принять приглашение', { type: 'submit', className: 'primary' }), decline);
      form.append(consent, buttons); card.append(form);
    }
    target.append(card);
  });
  const pager = element('div', '', { className: 'invitation-pagination' });
  for (const [title, nextOffset, disabled] of [['Предыдущие', offset - invitationPageSize, offset === 0], ['Следующие', offset + invitationPageSize, items.length < invitationPageSize]]) {
    const btn = element('button', title, { type: 'button', disabled });
    btn.dataset.action = student ? 'my-class-invitations' : 'class-invitations-page';
    btn.dataset.offset = String(nextOffset); btn.dataset.classId = classId; pager.append(btn);
  }
  target.append(pager);
}
async function loadClassInvitations(classId, offset = 0) {
  if (!classId) throw new Error('Загрузите классы и выберите свой класс.');
  context({ classId });
  const items = await request(`/api/classes/${encodeURIComponent(classId)}/invitations?offset=${offset}&limit=${invitationPageSize}`);
  renderInvitations(items, { student: false, classId, offset });
}
async function loadMyInvitations(offset = 0) {
  const profile = await request('/api/me');
  $('#my-class-info').textContent = profile.classId
    ? `Вы уже состоите в классе. Ниже — история приглашений. Ваш логин: ${profile.username}.`
    : `Вы пока занимаетесь самостоятельно. Передайте учителю свой логин: ${profile.username}.`;
  show('profile-result', profile);
  const items = await request(`/api/me/class-invitations?offset=${offset}&limit=${invitationPageSize}`);
  renderInvitations(items, { student: true, offset });
}
async function respondToInvitation(id, accept) {
  let decision;
  try { decision = await request(`/api/me/class-invitations/${encodeURIComponent(id)}/response`, { method: 'POST', body: { accept } }); }
  catch (error) {
    // Refresh decisions after a conflict or an uncertain network result before offering another action.
    $('#my-class-invitations').replaceChildren();
    try { await loadMyInvitations(); } catch { /* The original request error remains actionable. */ }
    throw error;
  }
  $('#my-class-invitations').replaceChildren();
  show('my-class-result', decision);
  try { await loadMyInvitations(); }
  catch { throw new Error('Решение сохранено. Не удалось обновить список: нажмите «Обновить приглашения».'); }
}

const actions = {
  async health() { const results = await Promise.allSettled(['/health/live', '/health/ready', '/api/lms/health'].map(path => request(path, { auth: false }))); show('health-result', results.map((item, i) => ({ service: ['Процесс ядра', 'База ядра', 'LMS'][i], result: item.status === 'fulfilled' ? item.value : item.reason.message }))); },
  async login(values) { const current = await login(values.username, values.password, values.workspaceRole || $('#workspace-mode').value); selectActor(current.id); show('profile-result', await request('/api/me')); },
  async register(values) { const account = await request('/auth/register', { method: 'POST', auth: false, body: values }); context({ accountId: account.id }); const current = await login(values.username, values.password, 'participant'); selectActor(current.id); show('profile-result', await request('/api/me')); },
  async profile() { show('profile-result', await request('/api/me')); },
  async refresh() { await refresh(requireActor()); show('profile-result', 'Сессия обновлена, refresh-секрет заменён.'); },
  async logout() { const current = requireActor(); await request(`/api/sessions/${current.sessionId}`, { method: 'DELETE' }); removeActor(current); },
  forget() { state = freshState(); save(); context({ schoolId: '', classId: '', caseId: '', accountId: '' }); selectActor(''); document.querySelectorAll('input[type="password"]').forEach(item => { item.value = ''; }); $('#log').textContent = ''; show('manage-result', 'Локальные сессии забыты. Для отзыва на сервере используйте управление сессиями.'); },
  'clear-log'() { $('#log').textContent = ''; },
  async prepare(values) {
    const owner = requireActor(); const lines = []; const done = (label, value) => { lines.push(`${label}: ${value}`); show('prepare-result', lines.join('\n')); };
    const school = await request('/api/schools', { method: 'POST', current: owner, body: { name: values.prefix + ' — школа' } }); context({ schoolId: school.id }); done('Школа', school.id);
    for (const profile of ['teacher', 'producer', 'expert', 'exporter']) { const name = `${values.prefix}-${profile}`; const user = await staff({ username: name, profile, schoolId: school.id, password: values.password }, owner); done(profile, name + ' / ' + user.id); }
    const teacher = state.actors.find(item => item.username === `${values.prefix}-teacher`);
    const group = await request('/api/classes', { method: 'POST', current: teacher, body: { schoolId: school.id, name: 'Класс 1' } }); context({ classId: group.id }); done('Класс', group.id);
    const studentName = `${values.prefix}-student`; const student = await request(`/api/classes/${group.id}/students`, { method: 'POST', current: teacher, body: { username: studentName } }); await setPassword(student.id, values.password, teacher); await login(studentName, values.password, 'participant'); done('Ученик', studentName + ' / ' + student.id);
    const name = `${values.prefix}-participant`; const independent = await request('/auth/register', { method: 'POST', auth: false, body: { username: name, password: values.password } }); const participant = await login(name, values.password, 'participant'); done('Участник', name + ' / ' + independent.id);
    const producer = state.actors.find(item => item.username === `${values.prefix}-producer`);
    const data = await request('/api/deliveries', { method: 'POST', current: producer, headers: { 'Idempotency-Key': values.prefix + '-fragment' }, body: { fragment_id: values.prefix + '-fragment', text_ba: 'Һаумыһығыҙ', lang_pair: 'ba-ru', translation: 'Здравствуйте', source: { document_id: values.prefix } } });
    context({ caseId: data.caseId, accountId: independent.id }); done('Случай', data.caseId); selectActor(participant.id); done('Готово', 'Выбран самостоятельный участник. Откройте «Переводы» и получите выдачу.');
  },
  async 'school-create'(values) { const data = await request('/api/schools', { method: 'POST', body: values }); context({ schoolId: data.id }); show('directory-result', data); },
  async 'staff-create'(values) { show('directory-result', await staff(values, requireActor())); },
  async 'class-create'(values) { const data = await request('/api/classes', { method: 'POST', body: values }); context({ classId: data.id }); show('directory-result', data); },
  async 'student-create'(values) { const owner = requireActor(); const data = await request(`/api/classes/${values.classId}/students`, { method: 'POST', body: { username: values.username } }); context({ accountId: data.id }); await setPassword(data.id, values.password, owner); await login(values.username, values.password, 'participant'); show('directory-result', data); },
  async schools() { show('directory-result', await request('/api/schools?offset=0&limit=100')); },
  async classes() { show('directory-result', await request('/api/classes?offset=0&limit=100')); },
  async students(values) { show('directory-result', await request(`/api/classes/${values.classId}/students?offset=0&limit=100`)); },
  async 'class-invite'(values) {
    const invitation = await request(`/api/classes/${encodeURIComponent(values.classId)}/invitations`, { method: 'POST', body: { username: values.username } });
    show('class-invitation-result', invitation);
    $('#class-invitations').replaceChildren();
    try { await loadClassInvitations(values.classId); }
    catch { throw new Error('Приглашение отправлено. Не удалось обновить список: нажмите «Обновить приглашения».'); }
  },
  'class-invitations'(values) { return loadClassInvitations(values.classId); },
  'class-invitations-page'(_, target) { return loadClassInvitations(target.dataset.classId, Number(target.dataset.offset)); },
  'my-class-invitations'(_, target) { return loadMyInvitations(Number(target?.dataset.offset || 0)); },
  'invitation-accept'(_, form) { return respondToInvitation(form.dataset.id, true); },
  'invitation-decline'(_, target) { return respondToInvitation(target.dataset.id, false); },
  async deliver(values) { const data = await request('/api/deliveries', { method: 'POST', headers: { 'Idempotency-Key': values.key }, body: { fragment_id: values.fragmentId, text_ba: values.original, lang_pair: values.languagePair, translation: values.translation, source: { document_id: values.documentId } } }); context({ caseId: data.caseId }); show('delivery-result', data); },
  async receive() { const current = requireActor(); const kind = $('#review-kind').value; const data = await request(`/api/reviews/next?kind=${encodeURIComponent(kind)}`, { method: 'POST' }); previewAssignment = null; state.assignments[current.id] = data; save(); renderAssignment(); if (data) context({ caseId: data.caseId }); show('review-result', data || 'Подходящих проверок пока нет. Загрузите материалы или проверьте ограничения выдачи.'); },
  'review-preview'() { previewAssignment = structuredClone($('#review-kind').value === 'translation-choice' ? exampleChoice : exampleConfirm); renderAssignment(); show('review-result', { message: 'Открыт пример проверки. Выберите ответ: в базу он не отправится.' }); },
  confirm() { return submitReview('Confirm'); }, skip() { return submitReview('Skip'); }, correct(values) { return submitReview('Correct', values.correction); },
  async statistics(values) { context({ caseId: values.caseId }); show('review-result', await request(`/api/cases/${values.caseId}/statistics`)); },
  async queue() { const data = await request('/api/expert/cases?offset=0&limit=100'); $('#queue').replaceChildren(); data.forEach((item, index) => { const btn = element('button', `${item.original || 'Перевод ' + (index + 1)} — ответов: ${item.observations}${item.disputed ? ', есть разногласия' : ''}`, { type: 'button' }); btn.dataset.action = 'case-open'; btn.dataset.id = item.caseId; $('#queue').append(btn); }); show('expert-result', data); },
  'case-last'() { if (!state.caseId) throw new Error('Сначала добавьте материал или получите задание участником. Можно также открыть перевод по идентификатору.'); return readCase(state.caseId); },
  'case-read'(values) { return readCase(values.caseId); }, 'case-open'(_, target) { return readCase(target.dataset.id); },
  async propose(values) { const view = requireCase(); await request(`/api/expert/cases/${view.caseId}/variants`, { method: 'POST', body: { text: values.text, expectedRevision: view.revision } }); await readCase(view.caseId); },
  async resolve(values) { const view = requireCase(); const primary = [], additional = [], rejected = []; $('#variants').querySelectorAll('select').forEach(item => { if (item.value === 'primary') primary.push(item.name); else if (item.value === 'additional') additional.push(item.name); else if (item.value === 'rejected') rejected.push(item.name); }); if (primary.length !== 1 || additional.length > 4) throw new Error('Выберите ровно один основной и не более четырёх дополнительных вариантов.'); await request(`/api/expert/cases/${view.caseId}/resolution`, { method: 'POST', body: { expectedRevision: view.revision, primaryVariantId: primary[0], additionalVariantIds: additional, rejectedVariantIds: rejected, comment: values.comment || null } }); await readCase(view.caseId); },
  async history(values) { show('expert-result', await request(`/api/expert/cases/${values.caseId}/history`)); },
  lesson,
  async 'lesson-boss'() {
    requireActor();
    const assignment = await request('/api/lessons/today/boss', { method: 'POST' });
    if (!assignment) {
      await lesson();
      show('learning-result', 'Если босс ещё не пройден, подходящих переводов сейчас нет. Вернитесь позже.');
      return;
    }
    renderDailyBoss(assignment);
  },
  async 'daily-boss-answer'(values, form) {
    requireActor();
    if (form.dataset.actor !== state.active) throw new Error('Обновите урок под текущим участником.');
    const body = { action: values.action };
    if (values.action === 'Correct') {
      if (!values.correction?.trim()) throw new Error('Введите свой перевод.');
      body.correction = values.correction.trim();
    }
    if (values.selectedVariantId) body.selectedVariantId = values.selectedVariantId;
    await request(`/api/reviews/${values.assignmentId}/response`, { method: 'POST', body });
    await lesson();
  },
  'lesson-preview'() { const data = structuredClone(exampleLesson); renderLesson(data, true); show('learning-result', { message: 'Открыты примеры всех шести учебных видов. Ответ можно собрать и посмотреть без входа и без записи в базу.' }); },
  async 'exercise-issue'(values) {
    const body = { kind: values.kind, languagePair: values.languagePair, ...(values.caseId.trim() ? { caseId: values.caseId.trim() } : {}) };
    if (values.kind.startsWith('audio-')) $('#status').textContent = 'Подготавливается аудиозадание. Первая озвучка может занять несколько минут…';
    const exercise = await request('/api/lessons/today/exercises', { method: 'POST', body, timeoutMs: values.kind.startsWith('audio-') ? 360000 : 30000 });
    if (!exercise) { show('learning-result', { message: 'Нет подходящего материала для этого вида. Для пар нужны пять слов, для перевода — принятый перевод; для сборки и пропуска — предложение из нескольких слов.' }); return; }
    await lesson(); document.querySelector(`[data-action="exercise-answer"][data-id="${exercise.id}"]`)?.scrollIntoView({ block: 'center' });
  },
  async 'exercise-answer'(_, form) {
    const answer = readExerciseAnswer(form);
    if (form.dataset.preview === 'true') { show('learning-result', { message: 'Предпросмотр: ответ подготовлен, в API не отправлен.', request: { answer } }); return; }
    if (form.dataset.actor !== state.active) throw new Error('Аккаунт изменился. Откройте урок заново.');
    const data = await request(`/api/exercises/${encodeURIComponent(form.dataset.id)}/answer`, { method: 'POST', body: { answer } });
    await lesson(); show('learning-result', { ...data, explanation: data.recognized ? 'Ответ сохранён и совпал с ключом задания.' : 'Ответ сохранён, совпадение с ключом не найдено. Это не экспертная оценка нового перевода. Повторить это упражнение с другим ответом уже нельзя.' });
  },
  async attempt(values, form) { const data = await request(`/api/exercises/${form.dataset.id}/attempt`, { method: 'POST', body: values }); await lesson(); show('learning-result', { ...data, explanation: data.recognized ? 'Ответ совпал с допустимым текстом.' : 'Ответ сохранён, но не найден в ключе. Это не лингвистический вердикт. Другой ответ на это упражнение уже не принимается.' }); },
  async courses() { const data = await request('/api/lms/courses', { auth: false }); $('#courses').replaceChildren(); data.forEach(item => { const btn = element('button', `${item.title} — уроков ${item.lesson_count}`, { type: 'button' }); btn.dataset.action = 'lms-open'; btn.dataset.id = item.id; $('#courses').append(btn); }); show('lms-result', data); },
  'lms-open'(_, target) { return lmsLessons(target.dataset.id); }, async 'lms-complete'(_, form) { await request(`/api/lms/lessons/${form.dataset.id}/complete`, { method: 'POST' }); await lmsLessons(courseId); },
  async progress() { show('lms-result', await request('/api/lms/me/progress')); },
  async sessions() { const data = await request('/api/sessions'); $('#sessions').replaceChildren(); data.forEach(item => { const row = element('p', `${item.id} | до ${item.expiresAt} | ${item.revokedAt ? 'Отозвана' : 'Активна'} `); if (!item.revokedAt) { const btn = element('button', 'Отозвать', { type: 'button' }); btn.dataset.action = 'session-revoke'; btn.dataset.id = item.id; row.append(btn); } $('#sessions').append(row); }); show('manage-result', data); },
  async 'session-revoke'(_, target) { const current = requireActor(); await request(`/api/sessions/${target.dataset.id}`, { method: 'DELETE' }); if (target.dataset.id === current.sessionId) removeActor(current); else await actions.sessions(); },
  async 'change-password'(values) { const current = requireActor(); await request('/api/credentials/change', { method: 'POST', body: values }); removeActor(current); show('manage-result', 'Пароль изменён. Войдите заново.'); },
  async 'issue-password'(values) { const data = await request(`/api/accounts/${values.accountId}/password-link`, { method: 'POST' }); passwordToken(data); },
  async recovery(values) { const data = await request(`/api/accounts/${values.accountId}/recovery-link`, { method: 'POST', body: { verificationNote: values.verificationNote } }); passwordToken(data); },
  async 'complete-password'(values) { await request('/auth/password', { method: 'POST', auth: false, body: values }); $('#setup-token').value = ''; show('manage-result', 'Пароль установлен. Прежние сессии отозваны; войдите заново.'); },
  async 'access-list'(values) { const data = await request(`/api/accounts/${values.accountId}/access`); $('#access-list').replaceChildren(); data.forEach(item => { const row = element('p', `${item.profile} | ${item.scope} | ${item.resourceId || 'все ресурсы'} `); if (!['account', 'administrator'].includes(item.profile)) { const btn = element('button', 'Отозвать', { type: 'button' }); btn.dataset.action = 'access-revoke'; btn.dataset.id = item.id; row.append(btn); } $('#access-list').append(row); }); show('manage-result', data); },
  async 'access-revoke'(_, target) { await request(`/api/access/${target.dataset.id}`, { method: 'DELETE' }); target.closest('p').remove(); show('manage-result', 'Назначение отозвано.'); },
  async 'access-assign'(values) { await request(`/api/accounts/${values.accountId}/access`, { method: 'PUT', body: { profile: values.profile, schoolId: values.schoolId || null, caseId: values.caseId || null } }); show('manage-result', 'Права назначены.'); },
  async 'account-status'(values) { await request(`/api/accounts/${values.accountId}/status`, { method: 'PUT', body: { active: values.active === 'true' } }); show('manage-result', 'Состояние сохранено. Прежние сессии аккаунта отозваны.'); },
  async 'policy-read'() { const data = await request('/api/policies/distribution'); $('#policy').value = JSON.stringify(data.settings, null, 2); Object.entries(data.settings).forEach(([key, value]) => { const field = document.querySelector(`form[data-action="policy-save"] [name="${key}"]`); if (field) field.value = value; }); show('manage-result', data); },
  async 'policy-save'(values) { const policy = Object.fromEntries(Object.entries(values).map(([key, value]) => [key, Number(value)])); if (Object.values(policy).some(value => !Number.isFinite(value))) throw new Error('Введите корректные числа в настройках.'); show('manage-result', await request('/api/policies/distribution', { method: 'POST', body: policy })); },
  async export(values) { const data = await request(`/api/translations/export?offset=0&limit=1000&includeDisputed=${values.includeDisputed === 'on'}`); show('manage-result', data); const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })); const link = element('a', 'Экспорт', { href: url, download: 'translations.json' }); link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
};
function removeActor(current) { state.actors = state.actors.filter(item => item.id !== current.id); delete state.assignments[current.id]; selectActor(''); }
function passwordToken(data) { context({ accountId: data.accountId }); $('#setup-token').value = data.token; show('manage-result', { accountId: data.accountId, expiresAt: data.expiresAt, message: 'Токен подставлен в форму установки пароля.' }); }
async function run(name, values, target) {
  if (busy || !actions[name]) return;
  const isPreview = name.endsWith('-preview') || target?.dataset.preview === 'true' || (previewAssignment && ['confirm', 'correct', 'skip'].includes(name));
  currentTask = { name, values };
  busy = true; $('#status').className = ''; $('#status').textContent = 'Выполняется…'; $('#actor').disabled = true;
  document.querySelectorAll('button').forEach(btn => { if (!btn.disabled) { btn.disabled = true; btn.dataset.busy = 'true'; } });
  $('#workspace-mode').disabled = true;
  try {
    await actions[name](values, target);
    $('#status').textContent = isPreview ? 'Предпросмотр. Данные и ответы в базу не отправляются.' : ({ login: 'Вы вошли. На главной странице показан следующий шаг.', register: 'Аккаунт создан. Теперь можно проверять переводы и учиться.',
      prepare: 'Данные созданы. Выбран участник — откройте «Проверка переводов».', confirm: 'Перевод подтверждён. Можно получить следующее задание.',
      correct: 'Ваш вариант сохранён.', skip: 'Задание пропущено. Можно получить следующее.', resolve: 'Решение эксперта сохранено. Участник может открыть практику перевода.',
      'class-invite': 'Приглашение отправлено. Ученик увидит его в разделе «Мой класс».',
      'invitation-accept': 'Вы вступили в класс. Профиль и приглашения обновлены.', 'invitation-decline': 'Приглашение отклонено.',
      'lms-complete': 'Урок пройден. Прогресс сохранён.', deliver: 'Материал отправлен на проверку.', logout: 'Вы вышли из аккаунта.' })[name] || 'Готово. Результат показан ниже.';
    if (target?.tagName === 'FORM') target.querySelectorAll('input[type="password"]').forEach(item => { item.value = ''; });
  }
  catch (error) { $('#status').textContent = error.message; $('#status').className = 'error'; }
  finally { busy = false; $('#actor').disabled = false; $('#workspace-mode').disabled = false; document.querySelectorAll('[data-busy]').forEach(btn => { btn.disabled = false; delete btn.dataset.busy; }); }
}
document.addEventListener('submit', event => { const form = event.target.closest('form[data-action]'); if (!form) return; event.preventDefault(); run(form.dataset.action, Object.fromEntries(new FormData(form)), form); });
document.addEventListener('click', event => { const btn = event.target.closest('button[type="button"][data-action]'); if (btn) run(btn.dataset.action, {}, btn); });
$('#actor').addEventListener('change', event => selectActor(event.target.value));
const suffix = new Date().toISOString().replace(/\D/g, '').slice(0, 14) + '-' + crypto.getRandomValues(new Uint32Array(1))[0].toString(36);
$('#prefix').value = 'demo-' + suffix; $('#delivery-key').value = 'fragment-' + suffix; $('#fragment-id').value = 'fragment-' + suffix;
ui = mountWorkspace({ getActor: actor, getState: () => state, save, onContext: context });
renderActors(); context({ caseId: state.caseId, schoolId: state.schoolId, classId: state.classId, accountId: state.accountId }); renderAssignment();
