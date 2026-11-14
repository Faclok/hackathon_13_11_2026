import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { Link } from "react-router-dom";
import { useUserStore } from "../store";

const paths: Record<string, ReactNode> = {
  home: (
    <>
      <path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z" />
    </>
  ),
  book: (
    <>
      <path d="M12 5C8 2 4 3 2 4v15c4-2 7-1 10 1 3-2 6-3 10-1V4c-4-2-7-1-10 1Zm0 0v15" />
    </>
  ),
  swords: (
    <>
      <path d="m4 3 5 2 10 13-3 3L5 8 4 3Zm16 0-5 2-3 4m-4 5-4 5m10-1 7-5M3 14l7 7" />
    </>
  ),
  trophy: (
    <>
      <path d="M7 3h10v7a5 5 0 0 1-10 0V3Zm5 12v6m-4 0h8M7 5H3v3a4 4 0 0 0 4 4m10-7h4v3a4 4 0 0 1-4 4" />
    </>
  ),
  user: (
    <>
      <circle cx="12" cy="7" r="4" />
      <path d="M4 21v-2a8 8 0 0 1 16 0v2" />
    </>
  ),
  bag: (
    <>
      <path d="M4 7h16l1 14H3L4 7Zm4 1V6a4 4 0 0 1 8 0v2" />
    </>
  ),
  key: (
    <>
      <circle cx="8" cy="8" r="5" />
      <path d="m11.5 11.5 9 9m-3-3 3-3m-6 0 3-3" />
    </>
  ),
  wheel: (
    <>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="2" />
      <path d="m12 3 0 7m9 2h-7m-2 9v-7m-9-2h7m-4-6 4 4m8-4-4 4m4 8-4-4m-8 4 4-4" />
    </>
  ),
  calendar: (
    <>
      <path d="M5 3v4m14-4v4M3 10h18M3 5h18v17H3Z" />
      <path d="m8 16 3 3 5-6" />
    </>
  ),
  arrow: (
    <>
      <path d="M4 12h15m-5-5 5 5-5 5" />
    </>
  ),
  back: (
    <>
      <path d="M20 12H5m5-5-5 5 5 5" />
    </>
  ),
  check: <path d="m5 12 4 4L19 6" />,
  lock: (
    <>
      <rect x="5" y="10" width="14" height="11" rx="3" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3m-4 5v2" />
    </>
  ),
};
export function Icon({ name, size = 22 }: { name: string; size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {paths[name] ?? paths.book}
    </svg>
  );
}
export function PageTitle({
  children,
  subtitle,
}: {
  children: ReactNode;
  subtitle?: string;
}) {
  return (
    <div className="page-heading">
      <Link to="/" className="back-link" aria-label="На главную">
        <Icon name="back" />
      </Link>
      <div>
        <h1>{children}</h1>
        {subtitle && <p>{subtitle}</p>}
      </div>
    </div>
  );
}
export function ProgressBar({ value, max }: { value: number; max: number }) {
  return (
    <div
      className="progress-track"
      role="progressbar"
      aria-valuenow={value}
      aria-valuemin={0}
      aria-valuemax={max}
    >
      <span style={{ width: `${(value / max) * 100}%` }} />
    </div>
  );
}
export function Modal({
  title,
  children,
  onClose,
  dismissible = true,
  className = "",
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  dismissible?: boolean;
  className?: string;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = dialog.current;
    const previous = document.activeElement as HTMLElement | null;
    element?.showModal();
    return () => {
      element?.close();
      previous?.focus();
    };
  }, []);
  return (
    <dialog
      ref={dialog}
      className={`modal ${className}`}
      onCancel={(event) => {
        event.preventDefault();
        if (dismissible) onClose();
      }}
      onClick={(e) => {
        if (dismissible && e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal-top">
        <h2>{title}</h2>
        {dismissible && (
          <button
            className="icon-button"
            onClick={onClose}
            aria-label="Закрыть"
          >
            ×
          </button>
        )}
      </div>
      {children}
    </dialog>
  );
}
const ToastContext = createContext<(message: string) => void>(() => {});
export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState("");
  useEffect(() => {
    if (!message) return;
    const timer = window.setTimeout(() => setMessage(""), 4000);
    return () => window.clearTimeout(timer);
  }, [message]);
  return (
    <ToastContext.Provider value={setMessage}>
      {children}
      {message && (
        <div role="status" className="school-toast">
          {message}
          <button onClick={() => setMessage("")} aria-label="Закрыть">
            ×
          </button>
        </div>
      )}
    </ToastContext.Provider>
  );
}
export const useToast = () => useContext(ToastContext);
export function IssueReportButton({ exerciseId }: { exerciseId: string }) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const toast = useToast();
  const report = useUserStore((s) => s.reportIssue);
  return (
    <>
      <button className="report-button" onClick={() => setOpen(true)}>
        ⚑ Ошибка в задании
      </button>
      {open && (
        <Modal title="Сообщить об ошибке" onClose={() => setOpen(false)}>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              report(exerciseId, text.trim());
              setOpen(false);
              setText("");
              toast("Спасибо! Мы проверим");
            }}
          >
            <label className="field-label" htmlFor="report">
              Что нужно исправить?
            </label>
            <textarea
              id="report"
              value={text}
              onChange={(e) => setText(e.target.value)}
              required
              maxLength={1000}
              rows={4}
              autoFocus
            />
            <button
              className="button button-primary full-width"
              disabled={!text.trim()}
            >
              Отправить
            </button>
          </form>
        </Modal>
      )}
    </>
  );
}
export function RewardResult({
  title,
  xp,
  coins,
  children,
}: {
  title: string;
  xp: number;
  coins: number;
  children?: ReactNode;
}) {
  return (
    <section className="surface result-card">
      <div className="result-symbol">✦</div>
      <h2>{title}</h2>
      <p>Ещё один шаг к башкирскому языку!</p>
      <div className="reward-row">
        <span>✦ +{xp} XP</span>
        <span>🪙 +{coins}</span>
      </div>
      {children}
      <Link className="button button-primary full-width" to="/">
        На главную <Icon name="arrow" size={18} />
      </Link>
      <Link className="button button-soft full-width" to="/leaderboard">
        К рейтингу
      </Link>
    </section>
  );
}
