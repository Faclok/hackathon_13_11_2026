const form = document.querySelector('#login');
const status = document.querySelector('#status');
const submit = document.querySelector('#submit');
const unlink = document.querySelector('#unlink');
let accessToken;
let sessionId;

async function request(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
    cache: 'no-store',
  });
  if (!response.ok) {
    if (response.status === 401) throw new Error('Проверьте логин и пароль. Если доступ заблокирован, обратитесь к учителю.');
    if (response.status === 403) throw new Error('Привязка доступна учётным записям учеников с доступом к заданиям.');
    if (response.status === 429) throw new Error('Слишком много попыток. Попробуйте чуть позже.');
    throw new Error('Не удалось выполнить действие. Попробуйте позже.');
  }
  return response.status === 204 ? null : response.json();
}

async function endSession() {
  if (accessToken && sessionId) {
    try { await request(`/api/sessions/${encodeURIComponent(sessionId)}`, { method: 'DELETE' }); } catch { /* The session will expire normally if the network is unavailable. */ }
  }
  accessToken = undefined;
  sessionId = undefined;
}

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  submit.disabled = true;
  status.textContent = '';
  document.querySelector('#result').hidden = true;
  unlink.hidden = true;
  try {
    await endSession();
    const credentials = await request('/auth/sign-in', {
      method: 'POST', body: JSON.stringify({ username: form.username.value.trim(), password: form.password.value }),
    });
    accessToken = credentials.accessToken;
    sessionId = credentials.sessionId;
    form.password.value = '';
    const link = await request('/api/me/telegram-link', { method: 'POST', body: '{}' });
    document.querySelector('#instructions').textContent = 'Ссылка действует 10 минут и используется один раз. Откройте бота и нажмите «Старт».';
    const open = document.querySelector('#open');
    open.hidden = !link.url;
    if (link.url) open.href = link.url;
    document.querySelector('#manual').hidden = Boolean(link.url);
    document.querySelector('#code').textContent = `/start ${link.code}`;
    document.querySelector('#result').hidden = false;
    unlink.hidden = false;
  } catch (error) {
    status.textContent = error instanceof TypeError ? 'Нет соединения с сервером. Попробуйте снова.' : error.message;
    await endSession();
  } finally {
    form.password.value = '';
    submit.disabled = false;
  }
});

window.addEventListener('pagehide', () => {
  if (accessToken && sessionId) {
    fetch(`/api/sessions/${encodeURIComponent(sessionId)}`, {
      method: 'DELETE', headers: { Authorization: `Bearer ${accessToken}` }, keepalive: true,
    }).catch(() => {});
  }
  accessToken = undefined;
  sessionId = undefined;
});

unlink.addEventListener('click', async () => {
  unlink.disabled = true;
  try {
    await request('/api/me/telegram-link', { method: 'DELETE' });
    document.querySelector('#result').hidden = true;
    status.textContent = 'Telegram отвязан. Чтобы подключить другой аккаунт, получите новую ссылку.';
    unlink.hidden = true;
    await endSession();
  } catch (error) { status.textContent = error.message; }
  finally { unlink.disabled = false; }
});
