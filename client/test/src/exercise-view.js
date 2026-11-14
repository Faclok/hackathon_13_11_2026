const node = (tag, text = '', props = {}) => Object.assign(document.createElement(tag), { textContent: text, ...props });
const readers = new WeakMap();
export const exerciseKinds = {
  'word-match': 'Сопоставьте слова',
  'translation-text': 'Переведите предложение',
  'audio-dictation': 'Запишите услышанное',
  'audio-translation': 'Переведите на слух',
  'missing-word': 'Восстановите слово',
  'sentence-order': 'Соберите предложение'
};

function textAnswer(container, label) {
  const wrapper = node('label', label);
  const input = node('textarea', '', { name: 'text', rows: 3, required: true });
  wrapper.append(input); container.append(wrapper);
  return () => {
    if (!input.value.trim()) throw new Error('Введите ответ.');
    return { text: input.value };
  };
}
function audioPrompt(container, prompt, preview) {
  const url = prompt.audioUrl;
  let parsed;
  try { parsed = new URL(url); } catch { /* A missing or invalid URL is shown as unavailable. */ }
  if (!preview && parsed && ['https:', 'http:'].includes(parsed.protocol) && !parsed.username && !parsed.password) {
    const player = node('audio', '', { controls: true, preload: 'none', src: parsed.href });
    player.setAttribute('aria-label', 'Озвучка предложения');
    player.addEventListener('error', () => {
      status.textContent = 'Не удалось загрузить запись. Обновите урок: срок ссылки мог истечь.';
    });
    const status = node('p', 'Прослушайте запись и введите ответ.', { className: 'hint', role: 'status' });
    container.append(player, status);
  } else {
    container.append(node('p', preview ? 'В примере аудиофайл не подключён. С настоящими данными здесь будет проигрыватель.' : 'Запись недоступна. Обновите урок, чтобы получить новую ссылку.', { className: 'notice' }));
  }
}
const renderers = {
  'translation-text'(container, prompt, exercise) {
    container.append(node('p', prompt.text || exercise.original, { className: 'exercise-phrase' }));
    return textAnswer(container, 'Ваш перевод');
  },
  'audio-dictation'(container, prompt, _, preview) {
    audioPrompt(container, prompt, preview);
    return textAnswer(container, 'Что прозвучало на башкирском?');
  },
  'audio-translation'(container, prompt, _, preview) {
    audioPrompt(container, prompt, preview);
    return textAnswer(container, 'Перевод услышанного');
  },
  'word-match'(container, prompt) {
    container.append(node('p', 'Подберите перевод для каждого слова. Каждый перевод используйте один раз.', { className: 'hint' }));
    const grid = node('div', '', { className: 'word-match-grid' });
    const choices = [];
    for (const item of prompt.left) {
      const label = node('label', item.text, { className: 'word-match-row' });
      const select = node('select', '', { required: true });
      select.setAttribute('aria-label', `Перевод слова «${item.text}»`);
      select.append(node('option', 'Выберите перевод', { value: '' }));
      prompt.right.forEach(right => select.append(node('option', right.text, { value: right.id })));
      label.append(select); grid.append(label); choices.push({ leftId: item.id, select });
    }
    const progress = node('p', 'Выбрано 0 из 5 пар.', { className: 'hint', role: 'status' });
    grid.addEventListener('change', () => {
      const selected = choices.map(x => x.select.value).filter(Boolean);
      for (const choice of choices) for (const option of choice.select.options)
        option.disabled = Boolean(option.value && option.value !== choice.select.value && selected.includes(option.value));
      progress.textContent = `Выбрано ${selected.length} из ${choices.length} пар.`;
    });
    container.append(grid, progress);
    return () => {
      const pairs = choices.map(x => ({ leftId: x.leftId, rightId: x.select.value }));
      if (pairs.length !== 5 || pairs.some(x => !x.rightId) || new Set(pairs.map(x => x.rightId)).size !== 5)
        throw new Error('Соедините пять разных слов с пятью разными переводами.');
      return { pairs };
    };
  },
  'missing-word'(container, prompt) {
    const phrase = node('p', '', { className: 'exercise-phrase' });
    const blank = node('span', '…', { className: 'word-gap' });
    blank.setAttribute('aria-label', 'Пропущенное слово');
    phrase.append(document.createTextNode(prompt.before), blank, document.createTextNode(prompt.after));
    container.append(phrase);
    const options = node('div', '', { className: 'answer-options' });
    const radios = [];
    for (const option of prompt.options || []) {
      const label = node('label', '', { className: 'answer-option' });
      const input = node('input', '', { type: 'radio', name: 'gap-option', value: option.id });
      label.append(input, document.createTextNode(option.text)); options.append(label); radios.push(input);
    }
    container.append(options);
    let custom;
    if (prompt.allowCustom) {
      const label = node('label', 'Или напишите слово'); custom = node('input', '', { name: 'customWord', autocomplete: 'off' });
      label.append(custom); container.append(label);
      custom.addEventListener('input', () => { if (custom.value) radios.forEach(x => { x.checked = false; }); });
      options.addEventListener('change', () => { custom.value = ''; });
    }
    return () => {
      const checked = radios.find(x => x.checked);
      if (checked) return { optionId: checked.value };
      if (custom?.value.trim()) return { text: custom.value };
      throw new Error('Выберите вариант или напишите пропущенное слово.');
    };
  },
  'sentence-order'(container, prompt) {
    container.append(node('p', 'Нажимайте на слова в нужном порядке. Слово из собранного предложения можно вернуть вниз.', { className: 'hint' }));
    const chosen = node('div', '', { className: 'sentence-chosen', role: 'group' });
    chosen.setAttribute('aria-label', 'Собранное предложение');
    const bank = node('div', '', { className: 'sentence-bank', role: 'group' });
    bank.setAttribute('aria-label', 'Доступные слова');
    const selected = [];
    function update() {
      chosen.replaceChildren(); bank.replaceChildren();
      if (!selected.length) chosen.append(node('span', 'Здесь появится ваше предложение', { className: 'hint' }));
      for (const id of selected) {
        const token = prompt.tokens.find(x => x.id === id);
        const button = node('button', token.text, { type: 'button', className: 'sentence-token' });
        button.setAttribute('aria-label', `Убрать «${token.text}» из предложения`);
        button.addEventListener('click', () => { selected.splice(selected.indexOf(id), 1); update(); }); chosen.append(button);
      }
      for (const token of prompt.tokens.filter(x => !selected.includes(x.id))) {
        const button = node('button', token.text, { type: 'button', className: 'sentence-token' });
        button.setAttribute('aria-label', `Добавить «${token.text}» в предложение`);
        button.addEventListener('click', () => { selected.push(token.id); update(); }); bank.append(button);
      }
    }
    update(); container.append(chosen, bank);
    return () => {
      if (selected.length !== prompt.tokens.length) throw new Error('Используйте все слова предложения.');
      return { tokenIds: [...selected] };
    };
  }
};

export function renderExercise(exercise, { preview = false, actorId = '' } = {}) {
  const form = node('form', '', { className: 'exercise-card' });
  form.dataset.id = exercise.id; form.dataset.action = 'exercise-answer'; form.dataset.actor = actorId;
  form.dataset.preview = String(preview);
  const header = node('div', '', { className: 'exercise-card-heading' });
  header.append(node('h2', exerciseKinds[exercise.kind] || 'Неизвестный вид задания'),
    node('span', exercise.completed ? 'Выполнено' : preview ? 'Пример' : 'Ожидает ответа', { className: 'exercise-status' }));
  form.append(header);
  const renderer = renderers[exercise.kind];
  if (!renderer || exercise.schemaVersion !== 1) {
    form.append(node('p', 'Этот вид или версия задания пока не поддерживаются. Обновите клиент.', { className: 'notice' }));
    return form;
  }
  const fields = node('fieldset', '', { disabled: Boolean(exercise.completed) });
  try { readers.set(form, renderer(fields, exercise.prompt || {}, exercise, preview)); }
  catch { form.append(node('p', 'Не удалось прочитать условие задания. Обновите урок.', { className: 'notice' })); return form; }
  fields.append(node('button', exercise.completed ? 'Ответ уже сохранён' : preview ? 'Посмотреть отправляемый ответ' : 'Отправить ответ', { type: 'submit', className: 'primary' }));
  form.append(fields);
  if (preview) form.append(node('p', 'Предпросмотр: ответы в базу не отправляются.', { className: 'hint' }));
  return form;
}

export function readExerciseAnswer(form) {
  const read = readers.get(form);
  if (!read) throw new Error('Нет обработчика для этого задания.');
  return read();
}
