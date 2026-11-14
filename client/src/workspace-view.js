import { roles } from './workspace-layout.js';

const $ = selector => document.querySelector(selector);
const node = (tag, text, props = {}) => Object.assign(document.createElement(tag), { textContent: text, ...props });
const pages = {
  home: 'Главная', accounts: 'Вход и регистрация', reviews: 'Проверка переводов', learning: 'Практика перевода',
  lms: 'Вводный курс', prepare: 'Быстрый старт', schools: 'Школа и пользователи', expert: 'Проверка эксперта',
  materials: 'Добавить материал', profile: 'Мой аккаунт', access: 'Доступ пользователей', policy: 'Правила распределения',
  export: 'Выгрузка переводов', health: 'Проверка подключения', help: 'Как пользоваться'
};
const menus = {
  participant: ['home', 'reviews', 'learning', 'lms', 'profile'],
  teacher: ['home', 'schools', 'access', 'lms', 'profile'],
  administrator: ['home', 'prepare', 'schools', 'access', 'policy', 'profile'],
  producer: ['home', 'materials', 'profile'], expert: ['home', 'expert', 'profile'], exporter: ['home', 'export', 'profile']
};
const nextTasks = {
  participant: ['reviews', 'Помогите проверить перевод', 'Получите одну фразу и подтвердите перевод или предложите свой вариант.', 'Получить задание'],
  teacher: ['schools', 'Подготовьте свой класс', 'Выберите школу, создайте класс и добавьте учеников.', 'Перейти к классам'],
  administrator: ['prepare', 'Пройдите полный сценарий', 'Создайте начальные данные, затем проверьте перевод от лица участника и эксперта.', 'Начать подготовку'],
  producer: ['materials', 'Добавьте фразу для проверки', 'Укажите оригинал, предложенный перевод и источник.', 'Добавить материал'],
  expert: ['expert', 'Проверьте предложенные переводы', 'Откройте очередь, выберите фразу и сохраните окончательное решение.', 'Открыть экспертизу'],
  exporter: ['export', 'Получите готовые переводы', 'Скачайте доступные вашему аккаунту материалы в JSON.', 'Перейти к выгрузке']
};

export function mountWorkspace({ getActor, getState, save, onContext }) {
  let activePage = '';
  const catalogs = () => (getState().catalogs ||= { schools: {}, classes: {} });
  const mode = () => { const preferred = getActor()?.workspaceRole || getState().workspaceRole; return roles[preferred] ? preferred : 'participant'; };
  function navigate(page, focus = false) {
    const allowed = getActor() ? [...menus[mode()], 'accounts', 'health', 'help'] : ['accounts', 'home', 'lms', 'health', 'help'];
    if (!allowed.includes(page)) page = getActor() ? 'home' : 'accounts';
    activePage = page;
    document.querySelectorAll('[data-page]').forEach(item => { item.hidden = item.dataset.page !== page; });
    document.querySelectorAll('#workspace-nav a').forEach(link => {
      if (link.hash === '#' + page) link.setAttribute('aria-current', 'page'); else link.removeAttribute('aria-current');
    });
    const result = document.querySelector('[data-result="manage-result"]');
    const outlet = $(`#${page} [data-manage-result]`);
    (outlet || $('#shared-manage-result')).append(result);
    if (location.hash !== '#' + page) history.replaceState(null, '', '#' + page);
    if (focus) { $(`#${page} h1`).focus({ preventScroll: true }); window.scrollTo({ top: 0 }); }
  }
  function renderHome() {
    const current = getActor(); const target = $('#home-content'); target.replaceChildren();
    if (!current) {
      const lead = node('div', '', { className: 'next-task' });
      lead.append(node('h2', 'Проверяйте переводы и изучайте язык'), node('p', 'Для начала войдите или создайте аккаунт. Вводный курс можно читать без регистрации.'),
        node('a', 'Войти или зарегистрироваться', { href: '#accounts', className: 'primary-link' })); target.append(lead); return;
    }
    const [page, title, description, cta] = nextTasks[mode()];
    const lead = node('div', '', { className: 'next-task' });
    lead.append(node('p', `Вы вошли как ${current.username}`, { className: 'account-caption' }), node('h2', title), node('p', description), node('a', cta, { href: '#' + page, className: 'primary-link' }));
    target.append(lead);
    if (mode() === 'administrator') {
      const guide = node('div', '', { className: 'home-guide' }); guide.append(node('h2', 'Полный путь: от материала до урока'));
      const list = node('ol', '', { className: 'journey' });
      [['Администратор', 'В «Быстром старте» создайте аккаунты и материал.'], ['Участник', 'Выберите аккаунт с суффиксом -participant и проверьте перевод.'],
        ['Эксперт', 'Выберите аккаунт -expert и примите один из вариантов.'], ['Участник', 'Вернитесь в аккаунт участника и впервые откройте практику перевода.']]
        .forEach(([title, description]) => { const item = node('li', ''); item.append(node('strong', title), node('span', description)); list.append(item); });
      guide.append(list); target.append(guide);
    } else if (mode() === 'participant') {
      const more = node('div', '', { className: 'home-guide' }); more.append(node('h2', 'Или начните с обучения'), node('p', 'Короткий вводный курс знакомит с первыми словами. Практика перевода появится из принятых материалов.'), node('a', 'Открыть вводный курс', { href: '#lms' })); target.append(more);
    }
  }
  function sync({ actorChanged = false } = {}) {
    const currentMode = mode(); $('#workspace-mode').value = currentMode;
    document.querySelector('form[data-action="login"] [name="workspaceRole"]').value = currentMode;
    const nav = $('#workspace-nav'); nav.replaceChildren();
    (getActor() ? menus[currentMode] : ['accounts', 'lms']).forEach(page => nav.append(node('a', pages[page], { href: '#' + page })));
    document.querySelectorAll('[data-workspaces]').forEach(item => { item.hidden = !item.dataset.workspaces.split(' ').includes(currentMode); });
    $('button[data-action="logout"]').hidden = !getActor();
    renderHome(); syncContext(); navigate(actorChanged ? (getActor() ? 'home' : 'accounts') : activePage || location.hash.slice(1) || (getActor() ? 'home' : 'accounts'), actorChanged);
  }
  function syncContext() {
    document.querySelectorAll('select[data-picker]').forEach(select => {
      const selected = getState()[select.name] || select.value;
      const entries = catalogs()[select.dataset.picker] || {};
      select.replaceChildren(node('option', 'Выберите из загруженного списка', { value: '' }));
      for (const [id, name] of Object.entries(entries)) select.append(node('option', name, { value: id }));
      if (selected && !entries[selected]) select.append(node('option', select.name === 'schoolId' ? 'Ранее выбранная школа' : 'Ранее выбранный класс', { value: selected }));
      select.value = selected;
    });
  }
  function rememberEntity(kind, id, name) { catalogs()[kind][id] = name; save(); syncContext(); }
  function table(target, headers, rows) {
    const table = node('table', ''); const head = node('tr', '');
    headers.forEach(title => head.append(node('th', title, { scope: 'col' }))); const thead = node('thead', ''); thead.append(head); table.append(thead);
    const body = node('tbody', ''); rows.forEach(row => { const tr = node('tr', ''); row.forEach(value => { const td = node('td', ''); if (value instanceof Node) td.append(value); else td.textContent = value; tr.append(td); }); body.append(tr); }); table.append(body); target.append(table);
  }
  function present(id, data, task = {}) {
    const target = $(`#${id}-summary`); if (!target) return;
    target.replaceChildren();
    if (typeof data === 'string') {
      if (data.startsWith('Прочитайте данные') || data.startsWith('Нажмите «Мой профиль»')) { if (id === 'profile-result') $('#profile-readable').replaceChildren(); return; }
      if (id === 'prepare-result') {
        const list = node('ul', '', { className: 'completed-steps' });
        data.split('\n').forEach(line => {
          const cleaned = line.replace(/\s*\/\s*[\da-f-]{36}/gi, '').replace(/[\da-f]{8}-[\da-f-]{27}/gi, 'создано');
          list.append(node('li', cleaned));
        }); target.append(list);
      } else target.append(node('p', data.replace(/^HTTP 204:\s*/, '')));
    } else if (id === 'health-result' && Array.isArray(data)) {
      data.forEach(item => { const ok = ['alive', 'ready', 'ok'].includes(item.result?.status); const row = node('p', '', { className: 'service-row' }); row.append(node('strong', item.service), node('span', ok ? 'Работает' : 'Нет подключения', { className: ok ? 'healthy' : 'unhealthy' })); target.append(row); });
    } else if (id === 'profile-result' && data?.username) {
      target.append(node('h2', data.username), node('p', data.schoolId ? 'Участник школьного класса.' : 'Школа к аккаунту не привязана.'));
    } else if (id === 'directory-result') {
      if (Array.isArray(data)) {
        if (!data.length) target.append(node('p', 'Список пока пуст. Создайте запись с помощью формы выше.'));
        else table(target, ['Название / логин', 'Действие'], data.map(item => {
          const kind = task.name === 'schools' ? 'schools' : task.name === 'classes' ? 'classes' : null;
          if (kind) catalogs()[kind][item.id] = item.name;
          const btn = node('button', 'Выбрать', { type: 'button' });
          btn.addEventListener('click', () => { onContext({ [kind === 'schools' ? 'schoolId' : kind === 'classes' ? 'classId' : 'accountId']: item.id }); btn.textContent = 'Выбрано'; });
          return [item.name || item.username, btn];
        })); save(); syncContext();
      } else if (data?.id) {
        if (task.name === 'school-create') rememberEntity('schools', data.id, task.values.name);
        if (task.name === 'class-create') rememberEntity('classes', data.id, task.values.name);
        target.append(node('p', data.username ? `Аккаунт ${data.username} создан. Пароль установлен. Выберите его в списке аккаунтов сверху.` : 'Готово. Запись создана и подставлена в связанные формы.'));
      }
    } else if (id === 'delivery-result' && data?.caseId) {
      target.append(node('p', 'Материал добавлен в поток проверки. Теперь участники могут получить его как задание.'));
    } else if (id === 'review-result' && data?.responseId) {
      target.append(node('p', data.archived ? 'Ответ сохранён в истории. Эксперт уже закрыл этот случай, результат не изменился.' : 'Ответ сохранён. Можно получить следующее доступное задание.', { className: 'success-message' }));
    } else if (id === 'review-result' && data?.original) {
      target.append(node('p', 'Задание получено. Прочитайте фразу и выберите ответ.'));
    } else if (id === 'review-result' && data?.variants) {
      target.append(node('p', `Участников: ${data.uniqueParticipants}. Нужно для выборки: ${data.requiredParticipants}.`));
      table(target, ['Вариант', 'Поддержали', 'Доля'], data.variants.map((item, i) => [String(i + 1), String(item.supports), percent(item.supportRatio)]));
    } else if (id === 'learning-result' && data?.exercises) {
      target.append(node('p', data.exercises.length ? `Упражнений на сегодня: ${data.exercises.length}.` : 'Сегодняшний урок пуст. Сначала нужны принятые переводы. Уже созданный урок не пополняется: вернитесь завтра или используйте участника, который ещё не открывал урок.'));
    } else if (data?.explanation) {
      target.append(node('p', data.explanation, { className: data.recognized ? 'success-message' : 'notice' }));
    } else if (id === 'lms-result' && data?.lessons) {
      const completed = data.lessons.filter(item => data.progress.some(progress => progress.lesson_id === item.id)).length;
      target.append(node('p', `Пройдено ${completed} из ${data.lessons.length} уроков.`));
      if (!getActor()) target.append(node('a', 'Войдите, чтобы сохранять прогресс', { href: '#accounts' }));
    } else if (id === 'lms-result' && Array.isArray(data)) {
      target.append(node('p', task.name === 'progress' ? `Сохранено пройденных уроков: ${data.length}.` : data.length ? 'Выберите курс, чтобы открыть уроки.' : 'Пока нет доступных курсов.'));
    } else if (id === 'expert-result' && data?.caseId) {
      target.append(node('p', data.closed ? 'Решение сохранено. Случай закрыт, принятые переводы доступны для упражнений.' : 'Выберите один основной допустимый перевод и сохраните решение.'));
    } else if (id === 'expert-result' && Array.isArray(data)) {
      target.append(node('p', data.length ? (task.name === 'history' ? `Решений в истории: ${data.length}. Подробности — в технических данных.` : `Переводов в очереди: ${data.length}. Откройте нужный выше.`) : 'Очередь или история пока пуста.'));
    } else if (data?.message) target.append(node('p', data.message));
    else if (data?.version) target.append(node('p', `Загружена версия настроек ${data.version}. Измените нужные поля и сохраните новую версию.`));
    else if (id === 'manage-result' && Array.isArray(data)) {
      target.append(node('p', task.name === 'export' ? `Выгружено переводов: ${data.length}. Файл скачан.` : `Получено записей: ${data.length}.`));
    } else if (data != null) target.append(node('p', 'Готово. Изменения сохранены.'));
    if (id === 'profile-result') $('#profile-readable').replaceChildren(...Array.from(target.childNodes, item => item.cloneNode(true)));
  }
  $('#workspace-mode').addEventListener('change', event => {
    if (getActor()) getActor().workspaceRole = event.target.value; else getState().workspaceRole = event.target.value;
    save(); activePage = getActor() ? 'home' : 'accounts'; sync();
  });
  window.addEventListener('hashchange', () => navigate(location.hash.slice(1), true));
  document.addEventListener('click', event => {
    const tab = event.target.closest('[data-auth-tab]'); if (!tab) return;
    document.querySelectorAll('[data-auth-panel]').forEach(item => { item.hidden = item.dataset.authPanel !== tab.dataset.authTab; });
    document.querySelectorAll('[data-auth-tab]').forEach(item => item.setAttribute('aria-pressed', String(item === tab)));
  });
  document.addEventListener('change', event => {
    const field = event.target;
    if (field.matches('[data-picker]')) onContext({ [field.name]: field.value });
    if (field.matches('select[name="profile"]')) {
      const school = field.form.elements.schoolId; school.disabled = field.value !== 'teacher'; school.required = field.value === 'teacher'; school.closest('label').hidden = field.value !== 'teacher';
      const caseId = field.form.elements.caseId; if (caseId) { caseId.disabled = !['expert', 'exporter'].includes(field.value); caseId.closest('label').hidden = caseId.disabled; }
    }
  });
  document.querySelectorAll('select[name="profile"]').forEach(item => item.dispatchEvent(new Event('change', { bubbles: true })));
  sync();
  return { sync, syncContext, present, navigate };
}

function percent(value) { return value == null ? 'Не определена' : `${Math.round(value * 100)}%`; }
