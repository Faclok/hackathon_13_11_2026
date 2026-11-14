export type Session = {
  accessToken: string;
  refreshToken?: string;
  username: string;
};
const key = "bashperevod.lms.session.v1";
let current: Session | null = null;
try {
  current = JSON.parse(sessionStorage.getItem(key) || "null") as Session | null;
} catch {
  current = null;
}
let refreshing: Promise<string> | null = null;

export function getSession() {
  return current;
}
export function saveSession(session: Session | null) {
  current = session;
  if (session) sessionStorage.setItem(key, JSON.stringify(session));
  else sessionStorage.removeItem(key);
}

async function refresh(): Promise<string> {
  if (!current?.refreshToken)
    throw new Error("Сессия закончилась. Войдите снова.");
  refreshing ||= (async () => {
    const response = await fetch("/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: current!.refreshToken }),
    });
    if (!response.ok) {
      saveSession(null);
      throw new Error("Сессия закончилась. Войдите снова.");
    }
    const tokens = (await response.json()) as Partial<Session>;
    saveSession({ ...current!, ...tokens });
    return current!.accessToken;
  })().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

async function send(
  path: string,
  init: RequestInit = {},
  auth = true,
  retry = true,
): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (auth) {
    if (!current?.accessToken) throw new Error("Войдите в аккаунт.");
    headers.set("Authorization", `Bearer ${current.accessToken}`);
  }
  const response = await fetch(path, { ...init, headers });
  if (response.status === 401 && auth && retry && current?.refreshToken) {
    await refresh();
    return send(path, init, auth, false);
  }
  if (!response.ok) {
    const error = (await response.json().catch(() => null)) as {
      title?: string;
    } | null;
    throw new Error(error?.title || `Ошибка сервиса: ${response.status}`);
  }
  return response;
}

export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
  auth = true,
): Promise<T> {
  const response = await send(
    path,
    { method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) },
    auth,
  );
  if (response.status === 204) return null as T;
  return response.json() as Promise<T>;
}

export async function download(path: string, filename: string) {
  const response = await send(path);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}
