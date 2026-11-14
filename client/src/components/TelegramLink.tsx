import { useEffect, useState } from "react";
import { apiRequest, ApiError } from "../api/session";
import { Icon, Modal } from "./ui";

interface LinkDetails {
  code: string;
  expiresAt: string;
  url: string | null;
}

function errorMessage(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Войдите в аккаунт заново и повторите привязку.";
    if (error.status === 403) return "Привязка недоступна для этого аккаунта. Обратитесь к преподавателю.";
    if (error.status === 429) return "Слишком много попыток. Попробуйте чуть позже.";
  }
  return "Не удалось связаться с сервером. Попробуйте ещё раз.";
}

export default function TelegramLink() {
  const [link, setLink] = useState<LinkDetails | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [confirmUnlink, setConfirmUnlink] = useState(false);
  const [expired, setExpired] = useState(false);

  useEffect(() => {
    if (!link) return;
    const timer = window.setTimeout(() => setExpired(true), Math.max(0, Date.parse(link.expiresAt) - Date.now()));
    return () => window.clearTimeout(timer);
  }, [link]);

  async function createLink() {
    setBusy(true);
    setError("");
    setNotice("");
    setLink(null);
    try {
      const result = await apiRequest<LinkDetails>("/api/me/telegram-link", { method: "POST" });
      setExpired(Date.parse(result.expiresAt) <= Date.now());
      setLink(result);
      if (result.url) window.location.assign(result.url);
    } catch (cause) {
      setError(errorMessage(cause));
    } finally {
      setBusy(false);
    }
  }

  async function unlink() {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await apiRequest<void>("/api/me/telegram-link", { method: "DELETE" });
      setLink(null);
      setConfirmUnlink(false);
      setNotice("Telegram отвязан. Теперь можно привязать другой аккаунт.");
    } catch (cause) {
      setError(errorMessage(cause));
      setConfirmUnlink(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section id="telegram" className="surface profile-section telegram-card" aria-labelledby="telegram-title">
      <div className="telegram-heading">
        <span className="telegram-symbol"><Icon name="telegram" size={26} /></span>
        <div>
          <h2 id="telegram-title">Telegram</h2>
          <p>Продолжай учиться в боте</p>
        </div>
      </div>
      <p className="telegram-description">Привяжи Telegram к своему профилю — на сайте и в боте будет один аккаунт и общий прогресс.</p>
      {link && !expired ? (
        <div className="telegram-instructions">
          <p>В боте нажми «Старт», чтобы завершить привязку. Ссылка действует 10 минут и используется один раз.</p>
          {link.url ? (
            <a className="button button-primary full-width" href={link.url}>Открыть бота <Icon name="arrow" size={18} /></a>
          ) : (
            <>
              <p>Открой нашего бота и отправь эту команду:</p>
              <code className="telegram-code">/start {link.code}</code>
            </>
          )}
          <button className="button button-soft full-width" onClick={createLink} disabled={busy}>
            {busy ? "Подождите…" : "Получить новую ссылку"}
          </button>
        </div>
      ) : (
        <button className="button button-primary full-width" onClick={createLink} disabled={busy}>
          {busy ? "Подождите…" : expired && link ? "Получить новую ссылку" : "Авторизоваться в Telegram"}
          {!busy && <Icon name="arrow" size={18} />}
        </button>
      )}
      {link && expired && <p className="telegram-feedback" role="status">Срок действия ссылки истёк. Получи новую, чтобы продолжить.</p>}
      {notice && <p className="telegram-feedback" role="status">{notice}</p>}
      {error && <p className="telegram-feedback telegram-error" role="alert">{error}</p>}
      <button className="telegram-unlink" onClick={() => setConfirmUnlink(true)} disabled={busy}>Отвязать Telegram</button>
      {confirmUnlink && (
        <Modal title="Отвязать Telegram?" onClose={() => setConfirmUnlink(false)} dismissible={!busy}>
          <p>Доступ из бота будет отключён, а ожидающая ссылка отменена. Профиль и прогресс на сайте сохранятся.</p>
          <button className="button button-primary full-width" onClick={unlink} disabled={busy}>{busy ? "Отвязываем…" : "Отвязать"}</button>
          <button className="button button-soft full-width" onClick={() => setConfirmUnlink(false)} disabled={busy}>Отмена</button>
        </Modal>
      )}
    </section>
  );
}
