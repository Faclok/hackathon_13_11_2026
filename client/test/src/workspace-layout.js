import usageUrl from '../USAGE.md?url';

export const roles = {
  participant: 'Участник', teacher: 'Учитель', administrator: 'Администратор',
  producer: 'Поставщик материалов', expert: 'Эксперт', exporter: 'Экспортёр'
};
const field = (name, label, type = 'text', attrs = '') => `<label>${label}<input name="${name}" type="${type}" ${attrs}></label>`;
const password = (name = 'password', label = 'Пароль') => field(name, label, 'password', 'required minlength="12" autocomplete="new-password"');
const button = (name, label, primary = false) => `<button type="button" data-action="${name}" ${primary ? 'class="primary"' : ''}>${label}</button>`;
const technical = (label, body) => `<details class="technical"><summary>${label}</summary>${body}</details>`;
const result = id => `<div class="result-block" data-result="${id}"><div class="readable-result" id="${id}-summary" aria-live="polite"></div>${technical('Технические данные ответа', `<pre id="${id}" tabindex="0">Запрос ещё не выполнен.</pre>`)}</div>`;
const picker = (name, label, kind, required = true) => `<label>${label}<select name="${name}" data-context="${name}" data-picker="${kind}" ${required ? 'required' : ''}><option value="">Сначала выберите из списка</option></select></label>`;
const profileSelect = () => '<label>Что будет делать сотрудник<select name="profile"><option value="teacher">Учитель — классы и ученики</option><option value="producer">Поставщик — загрузка материалов</option><option value="expert">Эксперт — окончательная проверка</option><option value="exporter">Экспортёр — выгрузка переводов</option></select></label>';
const page = (id, title, description, body) => `<section id="${id}" data-page="${id}" hidden><div class="page-heading"><h1 tabindex="-1">${title}</h1><p>${description}</p></div>${body}</section>`;
const form = (name, title, body, submit, attrs = '') => `<form data-action="${name}" ${attrs}>${title ? `<h2>${title}</h2>` : ''}${body}<button class="primary">${submit}</button></form>`;

export function workspaceMarkup() {
  return `
<a class="skip-link" href="#page-content">Перейти к содержимому</a>
<div class="app-shell"><aside class="sidebar">
  <a class="brand" href="#home"><span class="brand-mark" aria-hidden="true">Б</span><span>Башперевод<small>Рабочий кабинет</small></span></a>
  <label class="mode-label">Рабочее пространство<select id="workspace-mode">${Object.entries(roles).map(([key, name]) => `<option value="${key}">${name}</option>`).join('')}</select></label>
  <p class="mode-note">Показывает задачи выбранного профиля. Права аккаунта проверяет сервер.</p>
  <nav id="workspace-nav" aria-label="Основные разделы"></nav>
  <div class="sidebar-bottom"><a href="#help">Как пользоваться</a><a href="#health">Проверить подключение</a><a href="https://expert.bashperevod.ru" data-workspaces="expert">Отдельный кабинет эксперта</a></div>
</aside><div class="workspace">
<header class="topbar"><span id="identity">Вы ещё не вошли</span><div class="account-switch"><label>Аккаунт<select id="actor" aria-label="Текущий аккаунт"></select></label><a href="#accounts" class="account-add">Войти в другой аккаунт</a>${button('logout', 'Выйти')}</div></header>
<p id="status" role="status" aria-live="polite">Добро пожаловать. Выберите задачу в меню слева.</p>
<main id="page-content" tabindex="-1">
${page('home', 'С чего начнём?', 'Здесь собраны ваши задачи и следующий шаг.', '<div id="home-content"></div>')}
${page('accounts', 'Войдите в свой аккаунт', 'Один аккаунт для переводов и обучения. Для обычного участия школа не нужна.', `
<div class="auth-tabs" role="group" aria-label="Вход или регистрация"><button type="button" data-auth-tab="login" aria-pressed="true">У меня есть аккаунт</button><button type="button" data-auth-tab="register" aria-pressed="false">Зарегистрироваться</button></div>
<div class="auth-layout"><div>
${form('login', 'Вход', field('username', 'Логин', 'text', 'required autocomplete="username"') + field('password', 'Пароль', 'password', 'required autocomplete="current-password"') + `<label>Мои задачи<select name="workspaceRole">${Object.entries(roles).map(([key, title]) => `<option value="${key}">${title}</option>`).join('')}</select></label><p class="hint">Помогает выбрать нужные разделы. Дополнительные права не выдаются.</p>`, 'Войти в кабинет', 'data-auth-panel="login"')}
${form('register', 'Создать аккаунт участника', field('username', 'Придумайте логин', 'text', 'required maxlength="100" autocomplete="username"') + password() + '<p class="hint">Минимум 12 символов: большие и маленькие буквы, цифра и спецсимвол. После регистрации передайте свой логин учителю — он сможет пригласить вас в класс.</p>', 'Создать аккаунт и войти', 'data-auth-panel="register" hidden')}
</div><div class="explanation"><h2>Первый раз здесь?</h2><p>Зарегистрируйтесь, чтобы проверять переводы и проходить уроки.</p><h3>Входите как сотрудник?</h3><p>Выберите рабочее пространство слева: учитель, эксперт, администратор или другой профиль. Затем войдите с выданными вам логином и паролем.</p><details><summary>Где взять доступ администратора?</summary><p>Используйте логин и пароль первого администратора, заданные при запуске приложения в Coolify. JWT-ключ не является паролем и сюда не вводится.</p></details></div></div>${result('profile-result')}`)}
${page('reviews', 'Проверка переводов', 'Прочитайте фразу и решите, подходит ли предложенный перевод.', `
<label class="compact-field">Режим проверки<select id="review-kind"><option value="translation-choice">Выбор из популярных переводов</option><option value="translation-confirm">Подтверждение одного перевода</option></select></label>
<div class="task-toolbar">${button('receive', 'Получить задание', true)}${button('review-preview', 'Посмотреть пример')}<span class="hint">Незавершённое задание откроется в своём исходном режиме.</span></div>
<p id="review-preview-note" class="notice" hidden>Предпросмотр. Пример не связан с базой; ответы не сохраняются.</p>
<div id="assignment" class="translation-sheet"></div>
<div class="review-actions" hidden><div class="review-buttons">${form('confirm', '', '', 'Перевод верный')}${form('skip', '', '', 'Не знаю — пропустить')}</div>
<details class="correction"><summary>Написать свой перевод</summary>${form('correct', 'Предложите свой перевод', field('correction', 'Ваш вариант', 'text', 'required'), 'Отправить исправление')}</details></div>
${result('review-result')}${technical('Посмотреть статистику перевода', form('statistics', '', field('caseId', 'Идентификатор случая', 'text', 'required data-context="caseId"'), 'Показать статистику'))}`)}
${page('learning', 'Учебные задания', 'Соединяйте слова, переводите, слушайте и собирайте предложения.', `
<div class="task-toolbar">${button('lesson', 'Открыть сегодняшний урок', true)}${button('lesson-preview', 'Посмотреть все виды')}</div>
<p class="hint">Примеры доступны без входа. Для настоящего урока войдите как участник. Задания на перевод требуют принятого перевода; диктант, сборка и пропуск слова используют исходный текст.</p>
<details class="exercise-request"><summary>Получить задание определённого вида</summary>
${form('exercise-issue', 'Добавить задание в сегодняшний урок', '<label>Вид задания<select name="kind" id="exercise-kind"></select></label>' + field('languagePair', 'Языковая пара', 'text', 'required value="ba-ru"') + field('caseId', 'ID предложения — необязательно', 'text', 'placeholder="Оставьте пустым для автоматического подбора"') + '<p class="hint">Для пар слов ID предложения не нужен. При первом запросе аудио сервер подготовит озвучку и загрузит её в хранилище. Повторный запрос вернёт прежнее задание.</p>', 'Получить задание')}
</details>
<p id="lesson-preview-note" class="notice" hidden>Предпросмотр всех видов. Данные вымышлены, ответы в API не отправляются. Чтобы проверить базу, нажмите «Открыть сегодняшний урок».</p>
<div id="exercises" class="exercise-list"></div>${result('learning-result')}`)}
${page('lms', 'Вводный курс', 'Короткие уроки с первыми словами и сохранением прогресса.', `<div class="task-toolbar">${button('courses', 'Открыть каталог курсов', true)}${button('progress', 'Посмотреть мой прогресс')}</div><div id="courses" class="course-list"></div><div id="lms-lessons" class="exercise-list"></div>${result('lms-result')}`)}
${page('prepare', 'Быстрый старт', 'Подготовьте данные и пройдите полный путь от перевода до упражнения.', `
<ol class="journey"><li><strong>Подготовьте данные</strong><span>Администратор создаёт аккаунты и материал.</span></li><li><strong>Проверьте перевод</strong><span>Участник подтверждает или исправляет фразу.</span></li><li><strong>Примите результат</strong><span>Эксперт выбирает допустимый перевод.</span></li><li><strong>Откройте урок</strong><span>Участник переводит принятую фразу.</span></li></ol>
<div class="notice"><strong>Будут созданы новые записи</strong><p>Школа, класс, шесть аккаунтов и фраза «Һаумыһығыҙ» → «Здравствуйте». Действие не удаляет данные и не меняет пороги согласования.</p></div>
${form('prepare', 'Создать начальный набор', field('prefix', 'Общий префикс логинов', 'text', 'id="prefix" required maxlength="45" pattern="[A-Za-z0-9-]+"') + '<p class="hint">Уникальное значение уже подставлено. Каждый аккаунт получит свой суффикс.</p>' + password('password', 'Пароль для новых аккаунтов') + '<p class="hint">Запомните пароль: он понадобится для входа с другого устройства.</p>', 'Создать аккаунты и первый материал')}
${result('prepare-result')}`)}
${page('schools', 'Школа и пользователи', 'Создавайте школу и сотрудников либо работайте со своими классами и учениками.', `
<div class="task-toolbar">${button('schools', 'Загрузить школы')}${button('classes', 'Загрузить классы')}</div>
<div class="columns">${form('school-create', 'Новая школа', field('name', 'Название школы', 'text', 'required'), 'Создать школу', 'data-workspaces="administrator"')}
${form('staff-create', 'Новый сотрудник', field('username', 'Логин сотрудника', 'text', 'required') + profileSelect() + picker('schoolId', 'Школа (нужна только учителю)', 'schools', false) + password('password', 'Первоначальный пароль'), 'Создать сотрудника', 'data-workspaces="administrator"')}
${form('class-create', 'Новый класс', picker('schoolId', 'Школа', 'schools') + field('name', 'Название класса', 'text', 'required placeholder="Например, 5А"'), 'Создать класс', 'data-workspaces="teacher"')}
${form('student-create', 'Новый ученик', picker('classId', 'Ваш класс', 'classes') + field('username', 'Логин ученика', 'text', 'required') + password('password', 'Первоначальный пароль'), 'Создать ученика', 'data-workspaces="teacher"')}</div>
<div data-workspaces="teacher"><h2>Ученик уже зарегистрировался?</h2><p>Пригласите его по логину. Он появится в списке учеников после подтверждения.</p>
${form('class-invite', 'Пригласить в свой класс', picker('classId', 'Ваш класс', 'classes') + field('username', 'Логин зарегистрированного ученика', 'text', 'required maxlength="100" autocomplete="off"') + '<p class="hint">Сначала загрузите классы кнопкой выше. Ученику не нужно создавать второй аккаунт или передавать пароль.</p>', 'Отправить приглашение')}
${form('class-invitations', 'Отправленные приглашения', picker('classId', 'Ваш класс', 'classes'), 'Обновить приглашения')}<div id="class-invitations" class="invitation-list"></div>${result('class-invitation-result')}</div>
${form('students', 'Ученики класса', picker('classId', 'Класс', 'classes'), 'Показать учеников')}${result('directory-result')}`)}
${page('my-class', 'Мой класс', 'Учитель отправляет приглашение по вашему логину. Вы решаете, вступать ли в класс.', `
<div class="task-toolbar">${button('my-class-invitations', 'Обновить приглашения', true)}</div>
<div id="my-class-info" class="notice" aria-live="polite">Обновите приглашения, чтобы узнать, пригласил ли вас учитель.</div>
<div id="my-class-invitations" class="invitation-list"></div>${result('my-class-result')}`)}
${page('materials', 'Добавить материал', 'Передайте готовую фразу и предложенный перевод в общий поток проверки.', `
${form('deliver', 'Фраза для проверки', field('original', 'Оригинал на башкирском', 'text', 'required value="Һаумыһығыҙ"') + field('translation', 'Предложенный перевод', 'text', 'required value="Здравствуйте"') + field('documentId', 'Документ-источник', 'text', 'required value="manual-material"') + technical('Идентификаторы и языковая пара', field('key', 'Ключ повторной отправки', 'text', 'id="delivery-key" required') + field('fragmentId', 'Идентификатор фрагмента у поставщика', 'text', 'id="fragment-id" required') + field('languagePair', 'Языковая пара', 'text', 'required value="ba-ru"') + '<p class="hint">Для нового фрагмента измените идентификатор и ключ. Повтор прежнего запроса с тем же содержимым безопасен.</p>'), 'Отправить на проверку')}${result('delivery-result')}`)}
${page('expert', 'Проверка эксперта', 'Откройте фразу из очереди и выберите допустимые переводы.', `
<div class="task-toolbar">${button('queue', 'Открыть очередь переводов', true)}${button('case-last', 'Открыть перевод текущего сценария')}</div><p class="hint">Очередь содержит спорные переводы с достаточным числом ответов. Новый материал можно открыть кнопкой текущего сценария или по идентификатору ниже.</p><div id="queue" class="queue-list"></div>
${technical('Открыть по идентификатору', form('case-read', '', field('caseId', 'Идентификатор случая', 'text', 'required data-context="caseId"'), 'Открыть перевод'))}
<div id="case-view" class="translation-sheet"></div><div class="expert-actions" hidden>
${form('resolve', 'Какие переводы допустимы?', '<p class="hint">Выберите один основной. Дополнительно можно принять до четырёх вариантов. Остальные можно оставить нерассмотренными.</p><div id="variants"></div>' + field('comment', 'Комментарий (необязательно)'), 'Сохранить окончательное решение')}
${technical('Добавить свой вариант', form('propose', '', field('text', 'Ваш перевод', 'text', 'required'), 'Добавить вариант'))}</div>
${technical('История решений', form('history', '', field('caseId', 'Идентификатор случая', 'text', 'required data-context="caseId"'), 'Показать историю'))}${result('expert-result')}`)}
${page('profile', 'Мой аккаунт', 'Профиль, пароль и управление входами в систему.', `
${button('profile', 'Показать мой профиль', true)}<div id="profile-readable"></div>
${form('change-password', 'Сменить пароль', field('currentPassword', 'Текущий пароль', 'password', 'required autocomplete="current-password"') + password('newPassword', 'Новый пароль') + '<p class="hint">После смены пароля потребуется войти заново на всех устройствах.</p>', 'Сменить пароль')}
${technical('Входы на других устройствах', button('sessions', 'Загрузить мои сессии') + '<div id="sessions"></div>' + button('refresh', 'Обновить текущую сессию'))}
${technical('Данные этой вкладки', '<p id="storage-note">Аккаунты сохраняются только в этой вкладке. Пароли не сохраняются.</p>' + button('forget', 'Забыть аккаунты в этой вкладке'))}<div data-manage-result></div>`)}
${page('access', 'Доступ пользователей', 'Восстановление паролей и назначения сотрудников.', `
<div class="columns">${form('issue-password', 'Пароль сотрудника или ученика', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"') + '<p class="hint">Администратор работает с сотрудниками; учитель — с учениками своих классов.</p>', 'Подготовить установку пароля')}
${form('recovery', 'Восстановить самостоятельного участника', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"') + field('verificationNote', 'Как вы проверили личность участника?', 'text', 'required'), 'Подготовить восстановление', 'data-workspaces="administrator"')}</div>
${form('complete-password', 'Установить новый пароль', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"') + technical('Токен установки (подставляется автоматически)', field('token', 'Токен', 'password', 'id="setup-token" required autocomplete="off"')) + password('password', 'Новый пароль'), 'Установить пароль')}
<div data-workspaces="administrator">${technical('Управление назначениями', form('access-list', '', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"'), 'Загрузить назначения') + '<div id="access-list"></div>' + form('access-assign', 'Назначить дополнительные права', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"') + profileSelect() + picker('schoolId', 'Школа (только для учителя)', 'schools', false) + field('caseId', 'Ограничить случаем (необязательно, эксперт / экспортёр)'), 'Назначить права'))}
${technical('Блокировка аккаунта', form('account-status', '', field('accountId', 'Идентификатор аккаунта', 'text', 'required data-context="accountId"') + '<label>Состояние<select name="active"><option value="true">Активен</option><option value="false">Заблокирован</option></select></label><p class="hint">Изменение состояния отзывает прежние сессии.</p>', 'Сохранить состояние'))}</div><div data-manage-result></div>`)}
${page('policy', 'Правила распределения', 'Новая версия применяется к новым выдачам. Текущие задания сохраняют прежние настройки.', `
${button('policy-read', 'Загрузить текущие настройки', true)}
${form('policy-save', 'Параметры новой версии', `<div class="columns">${[
['requiredParticipants', 'Число независимых участников', 100, 1, 1], ['supportThreshold', 'Порог согласия (0–1)', .8, .01, .01],
['minimumSchools', 'Минимум школ', 2, 1, 1], ['minimumClasses', 'Минимум классов', 3, 1, 1],
['maximumSchoolShare', 'Максимальная доля школы (0–1)', .6, .01, .01], ['maximumClassShare', 'Максимальная доля класса (0–1)', .4, .01, .01],
['assignmentMinutes', 'Срок задания, минут', 30, 1, 1], ['repeatHours', 'Повтор через, часов', 24, 1, 1], ['workingSetSize', 'Размер рабочего набора', 20, 1, 1]
].map(([name, label, value, min, step]) => field(name, label, 'number', `required value="${value}" min="${min}" step="${step}"${name.includes('Share') || name === 'supportThreshold' ? ' max="1"' : ''}`)).join('')}</div><textarea id="policy" hidden disabled></textarea>`, 'Сохранить новую версию')}<div data-manage-result></div>`)}
${page('export', 'Выгрузка переводов', 'Скачайте материалы, доступные вашему аккаунту экспортёра.', `${form('export', 'Что включить в выгрузку?', '<label class="check-label"><input type="checkbox" name="includeDisputed"> Также спорные и ещё не принятые переводы</label><p class="hint">Скачивается JSON, до 1000 записей за один запрос.</p>', 'Скачать переводы')}<div data-manage-result></div>`)}
${page('health', 'Проверка подключения', 'Показывает, отвечают ли приложение, база данных и учебный сервис.', `${button('health', 'Проверить подключение', true)}${result('health-result')}${technical('Журнал запросов', '<p>Статусы и ответы API. Пароли и токены скрыты.</p>' + button('clear-log', 'Очистить журнал') + '<pre id="log" tabindex="0"></pre>')}`)}
${page('help', 'Как пользоваться', 'Не нужно знать API или копировать JSON, чтобы пройти основной сценарий.', `
<div class="help-content"><h2>Если вы участник</h2><ol><li>Зарегистрируйтесь или войдите.</li><li>Откройте «Проверка переводов» и получите задание.</li><li>Подтвердите перевод, предложите исправление или пропустите.</li><li>В «Вводном курсе» читайте уроки и сохраняйте прогресс.</li></ol>
<h2>Если вы администратор</h2><ol><li>Выберите пространство «Администратор» и войдите.</li><li>Откройте «Быстрый старт», задайте пароль и создайте набор данных.</li><li>Переключитесь на участника и проверьте фразу.</li><li>Переключитесь на эксперта и примите перевод.</li><li>Вернитесь к участнику и впервые откройте дневное упражнение.</li></ol>
<h2>Что выбрать в меню?</h2><p>Учитель создаёт классы и учеников. Поставщик добавляет материалы. Эксперт принимает переводы. Экспортёр выгружает результат. Выбор рабочего пространства помогает найти нужные задачи, но сам по себе не выдаёт права.</p>
<h2>Нет заданий или упражнений?</h2><p>Для проверки нужны загруженные материалы. Для упражнений — принятые переводы. Урок формируется один раз за день: не открывайте его до принятия материала, если хотите проверить полный сценарий.</p><a href="${usageUrl}">Открыть подробную инструкцию</a></div>`)}
<div id="shared-manage-result" hidden>${result('manage-result')}</div>
</main></div></div>`;
}
