import { useUserStore } from "../store";
import { openLinkingSite } from "../api/telegram";

export default function TelegramAccess() {
  const error = useUserStore(s => s.authError);
  return <div className="school-app login-app"><section className="surface intro-card login-card">
    <h1>Вход через Telegram</h1>
    <p role="alert">{error || "Откройте приложение заново из бота, чтобы войти в связанный аккаунт."}</p>
    <button className="button button-primary full-width" onClick={() => window.location.reload()}>Повторить вход</button>
    <button className="button button-soft full-width" onClick={openLinkingSite}>Открыть сайт для привязки</button>
  </section></div>;
}
