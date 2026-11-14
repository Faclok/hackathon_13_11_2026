interface TelegramApp {
  initData: string;
  ready(): void;
  expand(): void;
  openLink(url: string): void;
}
declare global {
  interface Window { Telegram?: { WebApp?: TelegramApp } }
}

let miniAppContext = new URLSearchParams(window.location.search).get("miniapp") === "1" ||
  new URLSearchParams(window.location.hash.slice(1)).has("tgWebAppData") || !!window.Telegram?.WebApp?.initData;
try {
  miniAppContext ||= sessionStorage.getItem("school-telegram-context") === "1";
  if (miniAppContext) sessionStorage.setItem("school-telegram-context", "1");
} catch { /* Keep the launch context in memory when tab storage is unavailable. */ }
export const isMiniApp = () => miniAppContext || !!window.Telegram?.WebApp?.initData;

let sdk: Promise<void> | null = null;
export async function telegramInitData(): Promise<string | null> {
  if (!isMiniApp()) return null;
  if (!window.Telegram?.WebApp) {
    sdk ??= new Promise<void>((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "https://telegram.org/js/telegram-web-app.js";
      const timer = window.setTimeout(() => reject(new Error("Telegram не ответил. Закройте и заново откройте приложение из бота.")), 15000);
      script.onload = () => { window.clearTimeout(timer); resolve(); };
      script.onerror = () => { window.clearTimeout(timer); reject(new Error("Не удалось подключиться к Telegram. Откройте приложение заново.")); };
      document.head.append(script);
    });
    await sdk;
  }
  const app = window.Telegram?.WebApp;
  if (!app?.initData) throw new Error("Откройте приложение кнопкой «Рейтинг» или «Открыть сайт» в нашем боте.");
  app.ready();
  app.expand();
  return app.initData;
}

export function openLinkingSite() {
  const url = new URL("/profile#telegram", window.location.origin).href;
  if (window.Telegram?.WebApp) window.Telegram.WebApp.openLink(url);
  else window.open(url, "_blank", "noopener,noreferrer");
}
