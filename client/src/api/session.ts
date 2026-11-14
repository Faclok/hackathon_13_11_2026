import { isMiniApp, telegramInitData } from "./telegram";
const STORAGE_KEY = "bashkir-school-session-v1";
let miniAppSession: Session | null = null;

interface Session {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

const readSession = (): Session | null => {
  if (isMiniApp()) return miniAppSession;
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "null");
    return value?.accessToken && value?.refreshToken ? value : null;
  } catch {
    return null;
  }
};

export const hasSession = () => !!readSession();
export const clearSession = () => {
  if (isMiniApp()) miniAppSession = null;
  else localStorage.removeItem(STORAGE_KEY);
};
const saveSession = (session: Session) => {
  if (isMiniApp()) miniAppSession = session;
  else localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
};

export async function loginTelegram() {
  const initData = await telegramInitData();
  if (!initData) return;
  miniAppSession = null;
  const response = await fetch("/auth/telegram-mini-app", {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ initData }),
  });
  if (response.status === 404) throw new Error("Сначала привяжите Telegram в профиле на сайте, затем откройте Mini App заново.");
  if (response.status === 401) throw new Error("Не удалось подтвердить вход через Telegram. Закройте и заново откройте приложение из бота.");
  miniAppSession = await decode<Session>(response);
}

async function decode<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new ApiError(response.status, body?.title ?? "Сервер не ответил на запрос.");
  }
  if (response.status === 204) return null as T;
  return response.json() as Promise<T>;
}

export async function loginUsername(username: string) {
  const session = await decode<Session>(
    await fetch("/auth/username", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: username.trim() }),
    }),
  );
  saveSession(session);
}

let refreshing: Promise<Session> | null = null;
async function refreshSession(session: Session): Promise<Session> {
  const current = readSession();
  if (current && current.refreshToken !== session.refreshToken) return current;
  refreshing ??= (async () => {
    try {
      const next = await decode<Session>(
        await fetch("/auth/refresh", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ refreshToken: session.refreshToken }),
        }),
      );
      saveSession(next);
      return next;
    } catch (error) {
      clearSession();
      throw error;
    }
  })();
  try {
    return await refreshing;
  } finally {
    refreshing = null;
  }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const initial = readSession();
  if (!initial) throw new ApiError(401, "Войдите в аккаунт.");
  const send = (token: string) =>
    fetch(path, {
      ...init,
      headers: {
        ...init.headers,
        Authorization: `Bearer ${token}`,
        ...(init.body ? { "Content-Type": "application/json" } : {}),
      },
    });
  let session = initial;
  if (new Date(session.expiresAt).getTime() - Date.now() < 30_000)
    session = await refreshSession(session);
  let response = await send(session.accessToken);
  if (response.status === 401) {
    session = await refreshSession(session);
    response = await send(session.accessToken);
  }
  return decode<T>(response);
}
