import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { api, download, getSession, saveSession } from "./api";
import type { Session } from "./api";
import type {
  Answer,
  Assignment,
  ClassReport,
  Member,
  StudentTask,
  TaskReport,
  TeacherClass,
  Translation,
} from "./types";

type View = "overview" | "classes" | "materials" | "my-tasks";
type ClassTab = "tasks" | "students" | "analytics";
type Filter = "all" | "participated" | "missing";
type Modal =
  | { kind: "class"; item?: TeacherClass }
  | { kind: "member"; item?: Member }
  | { kind: "translation"; item?: Translation }
  | { kind: "task"; item?: Assignment; source?: string }
  | null;

type IconName =
  | "grid"
  | "classes"
  | "book"
  | "student"
  | "plus"
  | "download"
  | "arrow"
  | "users"
  | "check"
  | "clock"
  | "alert"
  | "chart"
  | "search"
  | "edit"
  | "trash"
  | "close"
  | "logout"
  | "chevron"
  | "menu"
  | "spark"
  | "file";
const paths: Record<IconName, ReactNode> = {
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </>
  ),
  classes: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M8 4v16M12 9h5M12 13h5" />
    </>
  ),
  book: (
    <>
      <path d="M12 7C9.5 5.5 6.5 5 3 6v13c3.5-1 6.5-.5 9 1.1C14.5 18.5 17.5 18 21 19V6c-3.5-1-6.5-.5-9 1Z" />
      <path d="M12 7v13" />
    </>
  ),
  student: (
    <>
      <circle cx="9" cy="8" r="3" />
      <path d="M3.5 19v-2a5.5 5.5 0 0 1 11 0v2M17 11a3 3 0 1 0-1-5.8M18 14a5 5 0 0 1 2.5 4.3V19" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  download: (
    <>
      <path d="M12 3v12m0 0 4-4m-4 4-4-4M4 17v3h16v-3" />
    </>
  ),
  arrow: (
    <>
      <path d="M5 12h14m-5-5 5 5-5 5" />
    </>
  ),
  users: (
    <>
      <circle cx="8" cy="8" r="3" />
      <path d="M2.5 20v-2a5.5 5.5 0 0 1 11 0v2M16 5a3 3 0 0 1 0 6m1 4a5 5 0 0 1 4 5" />
    </>
  ),
  check: <path d="m5 12 4 4L19 6" />,
  clock: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3.5 2" />
    </>
  ),
  alert: (
    <>
      <path d="M12 3 2.5 20h19L12 3Z" />
      <path d="M12 9v5m0 3h.01" />
    </>
  ),
  chart: (
    <>
      <path d="M4 19V5m0 14h16M8 16v-5m5 5V7m5 9v-3" />
    </>
  ),
  search: (
    <>
      <circle cx="10.7" cy="10.7" r="6.7" />
      <path d="m16 16 5 5" />
    </>
  ),
  edit: (
    <>
      <path d="m4 17-.5 3.5L7 20l12-12-3-3L4 17Z" />
      <path d="m14 7 3 3" />
    </>
  ),
  trash: (
    <>
      <path d="M4 7h16M9 7V4h6v3m3 0-1 13H7L6 7M10 11v6m4-6v6" />
    </>
  ),
  close: <path d="M5 5 19 19M19 5 5 19" />,
  logout: (
    <>
      <path d="M9 4H5v16h4M13 8l4 4-4 4m4-4H9" />
    </>
  ),
  chevron: <path d="m9 5 7 7-7 7" />,
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  spark: (
    <>
      <path d="M12 2 14.2 9.8 22 12l-7.8 2.2L12 22l-2.2-7.8L2 12l7.8-2.2L12 2Z" />
    </>
  ),
  file: (
    <>
      <path d="M5 3h9l5 5v13H5V3Z" />
      <path d="M14 3v5h5M8 13h8M8 17h8" />
    </>
  ),
};
function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
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
      {paths[name]}
    </svg>
  );
}
function formatDate(value?: string | null, full = false) {
  if (!value) return "Без срока";
  return new Date(value).toLocaleString(
    "ru-RU",
    full
      ? {
          day: "numeric",
          month: "long",
          year: "numeric",
          hour: "2-digit",
          minute: "2-digit",
        }
      : { day: "numeric", month: "short" },
  );
}
function statusLabel(status: string) {
  return (
    (
      {
        confirmed: "Подтверждён",
        community: "Решение сообщества",
        expert: "Решение эксперта",
        expert_review: "На экспертизе",
        pending: "Ожидает оценки",
        own: "Свой перевод",
      } as Record<string, string>
    )[status] || status
  );
}
function verdictLabel(verdict: Answer["verdict"]) {
  return {
    correct: "Верно",
    incorrect: "Ошибка",
    review: "На проверке",
    missing: "Не сдано",
  }[verdict];
}
function percent(part: number, total: number) {
  return total ? Math.round((part / total) * 100) : 0;
}
function initials(value: string) {
  return (
    value
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join("") || "У"
  );
}
function Button({
  children,
  icon,
  variant = "solid",
  onClick,
  type = "button",
  disabled,
  className = "",
}: {
  children: ReactNode;
  icon?: IconName;
  variant?: "solid" | "light" | "outline" | "ghost" | "danger";
  onClick?: () => void;
  type?: "button" | "submit";
  disabled?: boolean;
  className?: string;
}) {
  return (
    <button
      className={`button button-${variant} ${className}`}
      onClick={onClick}
      type={type}
      disabled={disabled}
    >
      {icon && <Icon name={icon} size={17} />}
      <span>{children}</span>
    </button>
  );
}
function Empty({
  icon,
  title,
  text,
  action,
}: {
  icon: IconName;
  title: string;
  text: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty">
      <div className="empty-icon">
        <Icon name={icon} size={26} />
      </div>
      <h3>{title}</h3>
      <p>{text}</p>
      {action}
    </div>
  );
}
function Metric({
  icon,
  title,
  value,
  detail,
  tone = "neutral",
}: {
  icon: IconName;
  title: string;
  value: string | number;
  detail: string;
  tone?: string;
}) {
  return (
    <div className={`metric metric-${tone}`}>
      <div className="metric-icon">
        <Icon name={icon} size={21} />
      </div>
      <span className="metric-title">{title}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  );
}
function Segmented<T extends string>({
  value,
  items,
  onChange,
}: {
  value: T;
  items: { value: T; label: string; count?: number }[];
  onChange: (value: T) => void;
}) {
  return (
    <div className="segmented">
      {items.map((item) => (
        <button
          key={item.value}
          type="button"
          className={value === item.value ? "active" : ""}
          onClick={() => onChange(item.value)}
        >
          {item.label}
          {item.count !== undefined && <b>{item.count}</b>}
        </button>
      ))}
    </div>
  );
}
function AuthScreen({ onSuccess }: { onSuccess: (session: Session) => void }) {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    setBusy(true);
    try {
      if (mode === "register")
        await api("/auth/register", "POST", { username, password }, false);
      const tokens = await api<Omit<Session, "username">>(
        "/auth/sign-in",
        "POST",
        { username, password },
        false,
      );
      onSuccess({ ...tokens, username });
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="auth-shell">
      <div className="auth-brand">
        <div className="brand-mark">
          б<span>.</span>
        </div>
        <span>
          башперевод<small>КАБИНЕТ ПРЕПОДАВАТЕЛЯ</small>
        </span>
      </div>
      <div className="auth-layout">
        <div className="auth-story">
          <span className="eyebrow light">ПРОСТРАНСТВО ДЛЯ ПРЕПОДАВАТЕЛЯ</span>
          <h1>
            Учите видеть
            <br />
            <em>смысл</em> в каждом
            <br />
            переводе.
          </h1>
          <p>Классы, задания, ответы и понятная аналитика в одном месте.</p>
          <div className="story-art">
            <div className="story-sheet sheet-back" />
            <div className="story-sheet sheet-front">
              <div className="sheet-top">
                <span>ПЕРЕВОД · 8А КЛАСС</span>
                <span>01 / 03</span>
              </div>
              <strong>«Һаумыһығыҙ!»</strong>
              <div className="sheet-answer">Здравствуйте!</div>
              <div className="sheet-line" />
              <div className="sheet-foot">
                <span className="tiny-avatar">А</span>
                <span className="tiny-avatar">Д</span>
                <span className="tiny-avatar">М</span>
                <small>Ответы учеников — на ладони</small>
              </div>
            </div>
            <div className="floating-note">
              <Icon name="check" size={19} />
              <span>
                <b>14 из 18</b>
                <small>ответили на задание</small>
              </span>
            </div>
          </div>
        </div>
        <div className="auth-card">
          <div className="auth-card-head">
            <span className="eyebrow">ДОБРО ПОЖАЛОВАТЬ</span>
            <h2>{mode === "login" ? "Вход в кабинет" : "Создать аккаунт"}</h2>
            <p>Начните работу с классом за пару минут.</p>
          </div>
          <div className="auth-switch">
            <button
              className={mode === "login" ? "active" : ""}
              onClick={() => setMode("login")}
            >
              Войти
            </button>
            <button
              className={mode === "register" ? "active" : ""}
              onClick={() => setMode("register")}
            >
              Регистрация
            </button>
          </div>
          <form onSubmit={submit} className="auth-form">
            <label>
              Логин
              <input
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoComplete="username"
                placeholder="Ваш логин"
                required
              />
            </label>
            <label>
              Пароль
              <input
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                type="password"
                autoComplete={
                  mode === "login" ? "current-password" : "new-password"
                }
                minLength={mode === "register" ? 12 : undefined}
                placeholder="Введите пароль"
                required
              />
            </label>
            {mode === "register" && (
              <p className="form-hint">
                От 12 символов: заглавная и строчная буквы, цифра и спецсимвол.
              </p>
            )}
            {error && (
              <p className="form-error" role="alert">
                {error}
              </p>
            )}
            <Button type="submit" icon="arrow" disabled={busy}>
              {busy
                ? "Подождите…"
                : mode === "login"
                  ? "Войти в кабинет"
                  : "Создать аккаунт"}
            </Button>
          </form>
          <p className="auth-bottom">Материалы для уроков всегда под рукой.</p>
        </div>
      </div>
    </div>
  );
}

function diffWords(answer: string, reference: string) {
  const left = answer.trim().split(/\s+/).slice(0, 120);
  const right = reference.trim().split(/\s+/).slice(0, 120);
  const clean = (word: string) =>
    word.toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
  const dp = Array.from({ length: left.length + 1 }, () =>
    Array<number>(right.length + 1).fill(0),
  );
  for (let i = left.length - 1; i >= 0; i--)
    for (let j = right.length - 1; j >= 0; j--)
      dp[i][j] =
        clean(left[i]) === clean(right[j])
          ? dp[i + 1][j + 1] + 1
          : Math.max(dp[i + 1][j], dp[i][j + 1]);
  const matchingLeft = new Set<number>();
  const matchingRight = new Set<number>();
  let i = 0;
  let j = 0;
  while (i < left.length && j < right.length) {
    if (clean(left[i]) === clean(right[j])) {
      matchingLeft.add(i++);
      matchingRight.add(j++);
    } else if (dp[i + 1][j] >= dp[i][j + 1]) i++;
    else j++;
  }
  return { left, right, matchingLeft, matchingRight };
}
function AnswerReview({
  answer,
  task,
}: {
  answer: Answer;
  task: TaskReport;
}) {
  const diff = useMemo(
    () =>
      answer.answer
        ? diffWords(answer.answer, answer.referenceTranslation || task.referenceTranslation)
        : null,
    [answer.answer, task.referenceTranslation],
  );
  return (
    <article className="answer-card">
      <div className="answer-card-top">
        <div className="person">
          <div className="avatar">{initials(answer.displayName)}</div>
          <div>
            <strong>{answer.displayName}</strong>
            <small>
              @{answer.username} ·{" "}
              {answer.submittedAt
                ? formatDate(answer.submittedAt, true)
                : "Ответа пока нет"}
            </small>
          </div>
        </div>
        <span className={`status status-${answer.verdict}`}>
          {verdictLabel(answer.verdict)}
        </span>
      </div>
      {answer.answer ? (
        <>
          <div className="compare">
            <div className="compare-side pupil">
              <span>ОТВЕТ УЧЕНИКА</span>
              <p>
                {diff?.left.map((word, index) => (
                  <span
                    key={index}
                    className={diff.matchingLeft.has(index) ? "" : "difference"}
                  >
                    {word}{" "}
                  </span>
                ))}
              </p>
            </div>
            <div className="compare-side reference">
              <span>
                {task.translationStatus === "expert"
                  ? "ПЕРЕВОД, УТВЕРЖДЁННЫЙ ЭКСПЕРТОМ"
                  : task.translationStatus === "community"
                    ? "ПЕРЕВОД, ПРИНЯТЫЙ СООБЩЕСТВОМ"
                    : "ПРЕДЛОЖЕННЫЙ ВАРИАНТ"}
              </span>
              <p>
                {diff?.right.map((word, index) => (
                  <span
                    key={index}
                    className={
                      diff.matchingRight.has(index) ? "" : "difference"
                    }
                  >
                    {word}{" "}
                  </span>
                ))}
              </p>
            </div>
          </div>
          <p className="diff-note">
            <span className="diff-dot" />
            Подсвечены различия в тексте. Оценка появляется после решения сообщества или эксперта.
          </p>
        </>
      ) : (
        <div className="not-submitted">
          <Icon name="clock" size={20} /> Ученик ещё не отправил перевод.
        </div>
      )}
    </article>
  );
}

export default function App() {
  const [session, setSession] = useState<Session | null>(getSession);
  const [view, setView] = useState<View>("overview");
  const [classTab, setClassTab] = useState<ClassTab>("tasks");
  const [filter, setFilter] = useState<Filter>("all");
  const [selectedClassId, setSelectedClassId] = useState<string | null>(null);
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(null);
  const [classes, setClasses] = useState<TeacherClass[]>([]);
  const [members, setMembers] = useState<Member[]>([]);
  const [tasks, setTasks] = useState<Assignment[]>([]);
  const [report, setReport] = useState<ClassReport | null>(null);
  const [ownTranslations, setOwnTranslations] = useState<Translation[]>([]);
  const [catalog, setCatalog] = useState<Translation[]>([]);
  const [catalogOffset, setCatalogOffset] = useState(0);
  const [catalogHasMore, setCatalogHasMore] = useState(true);
  const [catalogQuery, setCatalogQuery] = useState("");
  const [studentTasks, setStudentTasks] = useState<StudentTask[]>([]);
  const [modal, setModal] = useState<Modal>(null);
  const [toast, setToast] = useState("");
  const [loading, setLoading] = useState(true);
  const [mobileMenu, setMobileMenu] = useState(false);
  const baseRequest = useRef(0);
  const classRequest = useRef(0);

  const show = (text: string) => {
    setToast(text);
    window.setTimeout(() => setToast(""), 4400);
  };
  const fail = (cause: unknown) =>
    show((cause as Error).message || "Не удалось выполнить действие.");
  const loadBase = useCallback(async () => {
    const requestId = ++baseRequest.current;
    const [allClasses, own, mine] = await Promise.all([
      api<TeacherClass[]>("/api/lms/teacher/classes"),
      api<Translation[]>("/api/lms/teacher/translations"),
      api<StudentTask[]>("/api/lms/me/tasks"),
    ]);
    if (requestId !== baseRequest.current) return;
    setClasses(allClasses);
    setOwnTranslations(own);
    setStudentTasks(mine);
    setSelectedClassId((previous) =>
      previous && allClasses.some((item) => item.id === previous)
        ? previous
        : allClasses[0]?.id || null,
    );
  }, []);
  const loadClass = useCallback(async (id: string) => {
    const requestId = ++classRequest.current;
    const [classMembers, initialTasks] = await Promise.all([
      api<Member[]>(`/api/lms/teacher/classes/${id}/members`),
      api<Assignment[]>(`/api/lms/teacher/classes/${id}/tasks`),
    ]);
    if (initialTasks.some((task) => task.teacherTranslationId && !task.catalogCaseId))
      await api(`/api/lms/teacher/classes/${id}/tasks/sync`, "POST");
    const [classTasks, classReport] = await Promise.all([
      initialTasks.some((task) => task.teacherTranslationId && !task.catalogCaseId)
        ? api<Assignment[]>(`/api/lms/teacher/classes/${id}/tasks`)
        : Promise.resolve(initialTasks),
      api<ClassReport>(`/api/lms/teacher/classes/${id}/report`),
    ]);
    if (requestId !== classRequest.current) return;
    setMembers(classMembers);
    setTasks(classTasks.map((task) => {
      const current = classReport.tasks.find((item) => item.id === task.id);
      return current ? { ...task, referenceTranslation: current.referenceTranslation,
        translationStatus: current.translationStatus, translationRating: current.translationRating } : task;
    }));
    setReport(classReport);
  }, []);
  const loadMoreCatalog = useCallback(async (offset: number) => {
    const page = await api<Translation[]>(
      `/api/lms/teacher/catalog?offset=${offset}&limit=50`,
    );
    setCatalog((previous) => (offset === 0 ? page : [...previous, ...page]));
    setCatalogOffset(offset + page.length);
    setCatalogHasMore(page.length === 50);
  }, []);
  useEffect(() => {
    if (!session?.accessToken) {
      setLoading(false);
      return;
    }
    setLoading(true);
    Promise.all([loadBase(), loadMoreCatalog(0)])
      .catch(fail)
      .finally(() => setLoading(false));
    // The access token identifies the active account; data refreshes when it changes.
  }, [session?.accessToken, loadBase, loadMoreCatalog]);
  useEffect(() => {
    if (!session?.accessToken || !selectedClassId) {
      classRequest.current++;
      setReport(null);
      setMembers([]);
      setTasks([]);
      return;
    }
    setReport(null);
    loadClass(selectedClassId).catch(fail);
  }, [selectedClassId, session?.accessToken, loadClass]);

  const activeClass = classes.find((item) => item.id === selectedClassId);
  const reportTask =
    report?.tasks.find((item) => item.id === selectedTaskId) || null;
  const totalAnswers =
    report?.tasks.reduce((sum, task) => sum + task.participated, 0) || 0;
  const possibleAnswers =
    (report?.memberCount || 0) * (report?.tasks.length || 0);
  const reviewCount =
    report?.tasks.reduce((sum, task) => sum + task.needsReview, 0) || 0;
  const visibleCatalog = catalog.filter((item) =>
    `${item.sourceText} ${item.translationText}`
      .toLocaleLowerCase("ru")
      .includes(catalogQuery.toLocaleLowerCase("ru")),
  );
  const selectedAnswers =
    reportTask?.answers.filter(
      (answer) =>
        filter === "all" ||
        (filter === "participated" && !!answer.answer) ||
        (filter === "missing" && !answer.answer),
    ) || [];

  async function doMutation(
    action: () => Promise<unknown>,
    message: string,
    refreshClass = true,
  ) {
    try {
      const result = await action();
      setModal(null);
      await loadBase();
      if (refreshClass && selectedClassId) await loadClass(selectedClassId);
      show(message);
      return result;
    } catch (cause) {
      fail(cause);
      return null;
    }
  }
  async function deleteClass() {
    if (
      !selectedClassId ||
      !confirm(`Удалить класс «${activeClass?.name}», его задания и ответы?`)
    )
      return;
    try {
      await api(`/api/lms/teacher/classes/${selectedClassId}`, "DELETE");
      setSelectedTaskId(null);
      setSelectedClassId(null);
      setReport(null);
      await loadBase();
      show("Класс удалён.");
    } catch (cause) {
      fail(cause);
    }
  }
  async function deleteMember(member: Member) {
    if (!selectedClassId || !confirm(`Убрать ${member.displayName} из класса?`))
      return;
    await doMutation(
      () =>
        api(
          `/api/lms/teacher/classes/${selectedClassId}/members/${member.userId}`,
          "DELETE",
        ),
      "Ученик удалён из класса.",
    );
  }
  async function deleteTranslation(item: Translation) {
    if (!confirm("Удалить перевод?"))
      return;
    await doMutation(
      () => api(`/api/lms/teacher/translations/${item.id}`, "DELETE"),
      "Перевод удалён.",
      false,
    );
  }
  async function requestExpert(item: Translation) {
    const note = window.prompt("Пометка для эксперта: что нужно проверить?");
    if (!note?.trim()) return;
    try {
      await api(`/api/lms/teacher/translations/${item.id}/expert-request`, "POST", { note: note.trim() });
      show("Запрос на экспертизу отправлен.");
    } catch (cause) {
      fail(cause);
    }
  }
  async function deleteTask(item: Assignment) {
    if (
      !selectedClassId ||
      !confirm(`Удалить задание «${item.title}» и ответы учеников?`)
    )
      return;
    await doMutation(
      () =>
        api(
          `/api/lms/teacher/classes/${selectedClassId}/tasks/${item.id}`,
          "DELETE",
        ),
      "Задание удалено.",
    );
    setSelectedTaskId(null);
  }
  async function exportFile(kind: "tasks" | "report") {
    if (!selectedClassId) return;
    try {
      await download(
        `/api/lms/teacher/classes/${selectedClassId}/${kind === "tasks" ? "tasks/export" : "report/export"}`,
        `class-${selectedClassId}-${kind}.csv`,
      );
      show(kind === "tasks" ? "Задания выгружены." : "Отчёт выгружен.");
    } catch (cause) {
      fail(cause);
    }
  }
  function navigate(next: View) {
    setView(next);
    setSelectedTaskId(null);
    setMobileMenu(false);
  }
  function pickClass(id: string) {
    setSelectedClassId(id);
    setView("classes");
    setClassTab("tasks");
    setSelectedTaskId(null);
    setMobileMenu(false);
  }
  function signOut() {
    baseRequest.current++;
    classRequest.current++;
    saveSession(null);
    setSession(null);
    setClasses([]);
    setReport(null);
    setSelectedClassId(null);
    setView("overview");
  }

  if (!session)
    return (
      <AuthScreen
        onSuccess={(value) => {
          saveSession(value);
          setSession(value);
        }}
      />
    );
  const nav: { id: View; icon: IconName; label: string }[] = [
    { id: "overview", icon: "grid", label: "Обзор" },
    { id: "classes", icon: "classes", label: "Мои классы" },
    { id: "materials", icon: "book", label: "Переводы" },
    ...(studentTasks.length
      ? [
          {
            id: "my-tasks" as View,
            icon: "student" as IconName,
            label: "Мне выдали",
          },
        ]
      : []),
  ];
  return (
    <div className="app-shell">
      <aside className={`sidebar ${mobileMenu ? "sidebar-open" : ""}`}>
        <div className="sidebar-top">
          <button className="sidebar-logo" onClick={() => navigate("overview")}>
            <div className="brand-mark">
              б<span>.</span>
            </div>
            <div>
              башперевод<small>КАБИНЕТ ПРЕПОДАВАТЕЛЯ</small>
            </div>
          </button>
          <button
            className="mobile-close"
            onClick={() => setMobileMenu(false)}
            aria-label="Закрыть меню"
          >
            <Icon name="close" />
          </button>
        </div>
        <div className="sidebar-label">РАБОЧЕЕ ПРОСТРАНСТВО</div>
        <nav className="side-nav">
          {nav.map((item) => (
            <button
              key={item.id}
              className={view === item.id ? "active" : ""}
              onClick={() => navigate(item.id)}
            >
              <Icon name={item.icon} size={19} />
              {item.label}
              {item.id === "classes" && <small>{classes.length}</small>}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="tip-card">
            <div className="tip-star">
              <Icon name="spark" size={21} />
            </div>
            <strong>Каждый ответ важен</strong>
            <p>Смотрите, где именно ученику нужна помощь.</p>
          </div>
          <div className="sidebar-account">
            <div className="avatar">{initials(session.username)}</div>
            <span>
              <b>{session.username}</b>
              <small>Преподаватель</small>
            </span>
            <button onClick={signOut} title="Выйти" aria-label="Выйти">
              <Icon name="logout" size={19} />
            </button>
          </div>
        </div>
      </aside>
      {mobileMenu && (
        <button
          className="mobile-scrim"
          aria-label="Закрыть меню"
          onClick={() => setMobileMenu(false)}
        />
      )}
      <div className="main-shell">
        <header className="topbar">
          <div className="topbar-left">
            <button
              className="mobile-menu"
              onClick={() => setMobileMenu(true)}
              aria-label="Открыть меню"
            >
              <Icon name="menu" />
            </button>
            <span>КАБИНЕТ ПРЕПОДАВАТЕЛЯ</span>
            <Icon name="chevron" size={14} />
            <strong>
              {view === "overview"
                ? "Обзор"
                : view === "classes"
                  ? "Мои классы"
                  : view === "materials"
                    ? "Переводы"
                    : "Мои задания"}
            </strong>
          </div>
          <div className="topbar-right">
            <span className="today">
              {new Date().toLocaleDateString("ru-RU", {
                day: "numeric",
                month: "long",
                year: "numeric",
              })}
            </span>
            <div className="top-avatar">{initials(session.username)}</div>
          </div>
        </header>
        <main className="main-content">
          {loading ? (
            <div className="loading">
              <div className="loader" />
              <p>Открываем кабинет…</p>
            </div>
          ) : (
            <>
              {view === "overview" && (
                <>
                  <div className="page-heading">
                    <div>
                      <span className="eyebrow">ВАШЕ РАБОЧЕЕ ПРОСТРАНСТВО</span>
                      <h1>
                        Добрый день, {session.username}{" "}
                        <span className="wave">✳</span>
                      </h1>
                      <p>
                        Здесь видно, как идут занятия и кому нужна ваша помощь.
                      </p>
                    </div>
                    <Button
                      icon="plus"
                      onClick={() => setModal({ kind: "class" })}
                    >
                      Новый класс
                    </Button>
                  </div>
                  <div className="overview-banner">
                    <div>
                      <span className="banner-kicker">
                        <span /> ВАШ КЛАСС СЕГОДНЯ
                      </span>
                      <h2>
                        {activeClass
                          ? activeClass.name
                          : "Первый класс начинается здесь"}
                      </h2>
                      <p>
                        {activeClass
                          ? activeClass.description ||
                            "Выдавайте задания и следите за прогрессом учеников."
                          : "Создайте класс, добавьте учеников и выберите первое предложение для перевода."}
                      </p>
                      <Button
                        variant="light"
                        icon="arrow"
                        onClick={() =>
                          activeClass
                            ? navigate("classes")
                            : setModal({ kind: "class" })
                        }
                      >
                        {activeClass ? "Открыть класс" : "Создать класс"}
                      </Button>
                    </div>
                    <div className="banner-visual">
                      <div className="banner-orbit one" />
                      <div className="banner-orbit two" />
                      <div className="banner-mini">
                        <small>АКТИВНОСТЬ КЛАССА</small>
                        <strong>
                          {percent(totalAnswers, possibleAnswers)}
                          <em>%</em>
                        </strong>
                        <div className="mini-bar">
                          <span
                            style={{
                              width: `${percent(totalAnswers, possibleAnswers)}%`,
                            }}
                          />
                        </div>
                        <p>
                          {totalAnswers} ответов из {possibleAnswers}
                        </p>
                      </div>
                    </div>
                  </div>
                  <div className="metrics">
                    <Metric
                      icon="classes"
                      title="Мои классы"
                      value={classes.length}
                      detail="в рабочем пространстве"
                      tone="blue"
                    />
                    <Metric
                      icon="users"
                      title="Ученики"
                      value={report?.memberCount || 0}
                      detail={
                        activeClass
                          ? `в классе ${activeClass.name}`
                          : "добавьте первый класс"
                      }
                      tone="mint"
                    />
                    <Metric
                      icon="file"
                      title="Выдано заданий"
                      value={tasks.length}
                      detail="в выбранном классе"
                      tone="peach"
                    />
                    <Metric
                      icon="alert"
                      title="Ждут проверки"
                      value={reviewCount}
                      detail="ответов, отличных от образца"
                      tone="lavender"
                    />
                  </div>
                  <div className="overview-columns">
                    <section className="surface">
                      <div className="section-head">
                        <div>
                          <span className="eyebrow">ВАША РАБОТА</span>
                          <h2>Классы</h2>
                        </div>
                        <button
                          className="link-button"
                          onClick={() => navigate("classes")}
                        >
                          Открыть все <Icon name="arrow" size={16} />
                        </button>
                      </div>
                      {classes.length ? (
                        <div className="overview-class-list">
                          {classes.slice(0, 4).map((item) => (
                            <button
                              key={item.id}
                              onClick={() => pickClass(item.id)}
                            >
                              <div className="class-avatar">
                                {initials(item.name)}
                              </div>
                              <span>
                                <strong>{item.name}</strong>
                                <small>
                                  {item.description ||
                                    "Открыть задания и учеников"}
                                </small>
                              </span>
                              <Icon name="chevron" size={18} />
                            </button>
                          ))}
                        </div>
                      ) : (
                        <Empty
                          icon="classes"
                          title="Классов пока нет"
                          text="Создайте пространство для своих учеников."
                          action={
                            <Button
                              variant="outline"
                              icon="plus"
                              onClick={() => setModal({ kind: "class" })}
                            >
                              Создать класс
                            </Button>
                          }
                        />
                      )}
                    </section>
                    <section className="surface">
                      <div className="section-head">
                        <div>
                          <span className="eyebrow">НЕДАВНИЕ ЗАДАНИЯ</span>
                          <h2>В работе</h2>
                        </div>
                        <button
                          className="link-button"
                          onClick={() => navigate("classes")}
                        >
                          Все задания <Icon name="arrow" size={16} />
                        </button>
                      </div>
                      {report?.tasks.length ? (
                        <div className="recent-list">
                          {report.tasks.slice(0, 4).map((item) => (
                            <button
                              key={item.id}
                              onClick={() => {
                                navigate("classes");
                                setSelectedTaskId(item.id);
                              }}
                            >
                              <div className="recent-icon">
                                <Icon name="file" size={20} />
                              </div>
                              <span>
                                <strong>{item.title}</strong>
                                <small>
                                  {item.participated} из {report.memberCount}{" "}
                                  ответили
                                </small>
                              </span>
                              <span className="recent-percent">
                                {percent(item.participated, report.memberCount)}
                                %
                              </span>
                            </button>
                          ))}
                        </div>
                      ) : (
                        <Empty
                          icon="file"
                          title="Заданий пока нет"
                          text="Выберите перевод и выдайте его классу."
                        />
                      )}
                    </section>
                  </div>
                </>
              )}
              {view === "classes" && (
                <>
                  <div className="page-heading">
                    <div>
                      <span className="eyebrow">ОРГАНИЗАЦИЯ ОБУЧЕНИЯ</span>
                      <h1>Мои классы</h1>
                      <p>Управляйте учениками, заданиями и результатами.</p>
                    </div>
                    <Button
                      icon="plus"
                      onClick={() => setModal({ kind: "class" })}
                    >
                      Новый класс
                    </Button>
                  </div>
                  <div className="class-layout">
                    <aside className="class-rail">
                      <div className="rail-heading">
                        <span>КЛАССЫ</span>
                        <b>{classes.length}</b>
                      </div>
                      {classes.length ? (
                        classes.map((item) => (
                          <button
                            className={`class-rail-item ${selectedClassId === item.id ? "active" : ""}`}
                            key={item.id}
                            onClick={() => pickClass(item.id)}
                          >
                            <div className="class-avatar">
                              {initials(item.name)}
                            </div>
                            <span>
                              <strong>{item.name}</strong>
                              <small>
                                {item.description || "Учебная группа"}
                              </small>
                            </span>
                            <Icon name="chevron" size={16} />
                          </button>
                        ))
                      ) : (
                        <p className="rail-empty">Создайте первый класс.</p>
                      )}
                      <button
                        className="rail-add"
                        onClick={() => setModal({ kind: "class" })}
                      >
                        <Icon name="plus" size={17} /> Добавить класс
                      </button>
                    </aside>
                    <div className="class-main">
                      {activeClass ? (
                        <>
                          <div className="class-cover">
                            <div className="class-cover-text">
                              <span className="eyebrow">
                                КЛАСС · УЧЕБНОЕ ПРОСТРАНСТВО
                              </span>
                              <h2>{activeClass.name}</h2>
                              <p>
                                {activeClass.description ||
                                  "Задания и прогресс ваших учеников в одном месте."}
                              </p>
                              <div className="cover-meta">
                                <span>
                                  <Icon name="users" size={16} />
                                  {report?.memberCount || 0} учеников
                                </span>
                                <span>
                                  <Icon name="file" size={16} />
                                  {tasks.length} заданий
                                </span>
                              </div>
                            </div>
                            <div className="class-cover-actions">
                              <Button
                                variant="light"
                                icon="edit"
                                onClick={() =>
                                  setModal({ kind: "class", item: activeClass })
                                }
                              >
                                Изменить
                              </Button>
                              <button
                                className="subtle-danger"
                                onClick={deleteClass}
                                title="Удалить класс"
                              >
                                <Icon name="trash" size={18} />
                              </button>
                            </div>
                          </div>
                          <div className="class-toolbar">
                            <Segmented
                              value={classTab}
                              items={[
                                {
                                  value: "tasks",
                                  label: "Задания",
                                  count: tasks.length,
                                },
                                {
                                  value: "students",
                                  label: "Ученики",
                                  count: members.length,
                                },
                                { value: "analytics", label: "Аналитика" },
                              ]}
                              onChange={(value) => {
                                setClassTab(value);
                                setSelectedTaskId(null);
                              }}
                            />
                            <div className="toolbar-actions">
                              <Button
                                variant="outline"
                                icon="download"
                                onClick={() => exportFile("tasks")}
                              >
                                Задания
                              </Button>
                              <Button
                                variant="outline"
                                icon="download"
                                onClick={() => exportFile("report")}
                              >
                                Отчёт
                              </Button>
                            </div>
                          </div>
                          {classTab === "tasks" &&
                            (reportTask ? (
                              <>
                                <button
                                  className="back-link"
                                  onClick={() => setSelectedTaskId(null)}
                                >
                                  ← Все задания
                                </button>
                                <div className="task-detail-head">
                                  <div>
                                    <span className="eyebrow">
                                      РАЗБОР ЗАДАНИЯ
                                    </span>
                                    <h2>{reportTask.title}</h2>
                                    <p>{reportTask.sourceText}</p>
                                    <span className="translation-chip">
                                      {statusLabel(
                                        reportTask.translationStatus,
                                      )}{" "}
                                      · рейтинг {reportTask.translationRating}
                                    </span>
                                    {reportTask.acceptedTranslations.length > 1 && (
                                      <p>Допустимые переводы: {reportTask.acceptedTranslations.join("; ")}</p>
                                    )}
                                  </div>
                                  <Button
                                    variant="outline"
                                    icon="edit"
                                    onClick={() =>
                                      setModal({
                                        kind: "task",
                                        item: tasks.find(
                                          (item) => item.id === reportTask.id,
                                        ),
                                      })
                                    }
                                  >
                                    Изменить
                                  </Button>
                                </div>
                                <div className="task-stats">
                                  <Metric
                                    icon="users"
                                    title="Участвовали"
                                    value={`${reportTask.participated}/${report?.memberCount ?? 0}`}
                                    detail={`${percent(reportTask.participated, report?.memberCount ?? 0)}% класса`}
                                    tone="blue"
                                  />
                                  <Metric
                                    icon="check"
                                    title="Верно"
                                    value={reportTask.correct}
                                    detail={statusLabel(reportTask.translationStatus)}
                                    tone="mint"
                                  />
                                  <Metric
                                    icon="alert"
                                    title="Ошибки"
                                    value={reportTask.incorrect}
                                    detail={statusLabel(reportTask.translationStatus)}
                                    tone="peach"
                                  />
                                  <Metric
                                    icon="clock"
                                    title="На проверке"
                                    value={reportTask.needsReview}
                                    detail="ожидают решения"
                                    tone="lavender"
                                  />
                                </div>
                                <div className="answers-header">
                                  <div>
                                    <h3>Ответы учеников</h3>
                                    <p>
                                      Ответы сверяются с решением сообщества или эксперта.
                                    </p>
                                  </div>
                                  <Segmented
                                    value={filter}
                                    items={[
                                      {
                                        value: "all",
                                        label: "Все",
                                        count: reportTask.answers.length,
                                      },
                                      {
                                        value: "participated",
                                        label: "Участвовали",
                                        count: reportTask.participated,
                                      },
                                      {
                                        value: "missing",
                                        label: "Не сдали",
                                        count:
                                          reportTask.answers.length -
                                          reportTask.participated,
                                      },
                                    ]}
                                    onChange={setFilter}
                                  />
                                </div>
                                <div className="answer-list">
                                  {selectedAnswers.length ? (
                                    selectedAnswers.map((answer) => (
                                      <AnswerReview
                                        key={answer.userId}
                                        answer={answer}
                                        task={reportTask}
                                      />
                                    ))
                                  ) : (
                                    <Empty
                                      icon="users"
                                      title="В этой группе пока никого"
                                      text="Попробуйте другой фильтр."
                                    />
                                  )}
                                </div>
                              </>
                            ) : (
                              <>
                                <div className="section-head task-list-head">
                                  <div>
                                    <span className="eyebrow">
                                      ПРАКТИКА ПЕРЕВОДА
                                    </span>
                                    <h2>Выданные задания</h2>
                                  </div>
                                  <Button
                                    icon="plus"
                                    onClick={() => setModal({ kind: "task" })}
                                  >
                                    Выдать задание
                                  </Button>
                                </div>
                                {report?.tasks.length ? (
                                  <div className="assignment-list">
                                    {report.tasks.map((item) => (
                                      <article
                                        className="assignment-row"
                                        key={item.id}
                                      >
                                        <button
                                          className="assignment-primary"
                                          onClick={() => {
                                            setSelectedTaskId(item.id);
                                            setFilter("all");
                                          }}
                                        >
                                          <div className="assignment-icon">
                                            <Icon name="file" size={21} />
                                          </div>
                                          <div>
                                            <strong>{item.title}</strong>
                                            <span>{item.sourceText}</span>
                                            <small>
                                              <Icon name="clock" size={14} />
                                              {item.dueAt
                                                ? `До ${formatDate(item.dueAt)}`
                                                : "Без срока"}{" "}
                                              <i />{" "}
                                              {statusLabel(
                                                item.translationStatus,
                                              )}
                                            </small>
                                          </div>
                                        </button>
                                        <div className="assignment-progress">
                                          <div>
                                            <strong>
                                              {item.participated}/
                                              {report.memberCount}
                                            </strong>
                                            <span>ответили</span>
                                          </div>
                                          <div className="progress-track">
                                            <span
                                              style={{
                                                width: `${percent(item.participated, report.memberCount)}%`,
                                              }}
                                            />
                                          </div>
                                        </div>
                                        <div className="assignment-actions">
                                          <button
                                            title="Изменить"
                                            onClick={() =>
                                              setModal({
                                                kind: "task",
                                                item: tasks.find(
                                                  (task) => task.id === item.id,
                                                ),
                                              })
                                            }
                                          >
                                            <Icon name="edit" size={18} />
                                          </button>
                                          <button
                                            title="Удалить"
                                            onClick={() => {
                                              const task = tasks.find(
                                                (task) => task.id === item.id,
                                              );
                                              if (task) deleteTask(task);
                                            }}
                                          >
                                            <Icon name="trash" size={18} />
                                          </button>
                                        </div>
                                        <button
                                          className="row-arrow"
                                          onClick={() => {
                                            setSelectedTaskId(item.id);
                                            setFilter("all");
                                          }}
                                          aria-label={`Открыть ${item.title}`}
                                        >
                                          <Icon name="chevron" size={19} />
                                        </button>
                                      </article>
                                    ))}
                                  </div>
                                ) : (
                                  <Empty
                                    icon="file"
                                    title="Первое задание ждёт вас"
                                    text="Выберите свой перевод или готовый вариант из каталога и отправьте его классу."
                                    action={
                                      <Button
                                        icon="plus"
                                        onClick={() =>
                                          setModal({ kind: "task" })
                                        }
                                      >
                                        Выдать задание
                                      </Button>
                                    }
                                  />
                                )}
                              </>
                            ))}
                          {classTab === "students" && (
                            <>
                              <div className="section-head task-list-head">
                                <div>
                                  <span className="eyebrow">СОСТАВ КЛАССА</span>
                                  <h2>Ученики</h2>
                                </div>
                                <Button
                                  icon="plus"
                                  onClick={() => setModal({ kind: "member" })}
                                >
                                  Добавить ученика
                                </Button>
                              </div>
                              {members.length ? (
                                <div className="student-list">
                                  <div className="student-list-head">
                                    <span>УЧЕНИК</span>
                                    <span>УЧАСТИЕ</span>
                                    <span>ДЕЙСТВИЯ</span>
                                  </div>
                                  {members.map((member) => {
                                    const participated =
                                      report?.tasks.filter((task) =>
                                        task.answers.some(
                                          (answer) =>
                                            answer.userId === member.userId &&
                                            answer.answer,
                                        ),
                                      ).length || 0;
                                    return (
                                      <div
                                        className="student-row"
                                        key={member.userId}
                                      >
                                        <div className="person">
                                          <div className="avatar">
                                            {initials(member.displayName)}
                                          </div>
                                          <div>
                                            <strong>
                                              {member.displayName}
                                            </strong>
                                            <small>@{member.username}</small>
                                          </div>
                                        </div>
                                        <div className="student-participation">
                                          <div className="progress-track">
                                            <span
                                              style={{
                                                width: `${percent(participated, tasks.length)}%`,
                                              }}
                                            />
                                          </div>
                                          <small>
                                            {participated} из {tasks.length}{" "}
                                            заданий
                                          </small>
                                        </div>
                                        <div className="student-actions">
                                          <button
                                            title="Изменить имя"
                                            onClick={() =>
                                              setModal({
                                                kind: "member",
                                                item: member,
                                              })
                                            }
                                          >
                                            <Icon name="edit" size={18} />
                                          </button>
                                          <button
                                            title="Убрать из класса"
                                            onClick={() => deleteMember(member)}
                                          >
                                            <Icon name="trash" size={18} />
                                          </button>
                                        </div>
                                      </div>
                                    );
                                  })}
                                </div>
                              ) : (
                                <Empty
                                  icon="users"
                                  title="Здесь пока нет учеников"
                                  text="Добавьте ученика по логину зарегистрированного аккаунта."
                                  action={
                                    <Button
                                      icon="plus"
                                      onClick={() =>
                                        setModal({ kind: "member" })
                                      }
                                    >
                                      Добавить ученика
                                    </Button>
                                  }
                                />
                              )}
                            </>
                          )}
                          {classTab === "analytics" && (
                            <>
                              <div className="section-head task-list-head">
                                <div>
                                  <span className="eyebrow">
                                    ПРОГРЕСС КЛАССА
                                  </span>
                                  <h2>Аналитика</h2>
                                </div>
                                <Button
                                  variant="outline"
                                  icon="download"
                                  onClick={() => exportFile("report")}
                                >
                                  Скачать отчёт
                                </Button>
                              </div>
                              <div className="analytics-summary">
                                <div
                                  className="analytics-ring"
                                  style={{
                                    background: `conic-gradient(#e87354 ${percent(totalAnswers, possibleAnswers)}%, #e8e9e4 0)`,
                                  }}
                                >
                                  <div>
                                    <strong>
                                      {percent(totalAnswers, possibleAnswers)}%
                                    </strong>
                                    <span>участие</span>
                                  </div>
                                </div>
                                <div>
                                  <h3>Общая активность</h3>
                                  <p>
                                    Сколько ответов получено от учеников по всем
                                    заданиям класса.
                                  </p>
                                  <div className="analytics-numbers">
                                    <span>
                                      <b>{totalAnswers}</b> ответов
                                    </span>
                                    <span>
                                      <b>{possibleAnswers - totalAnswers}</b>{" "}
                                      ожидаются
                                    </span>
                                  </div>
                                </div>
                              </div>
                              {report?.tasks.length ? (
                                <div className="analytics-table">
                                  <div className="analytics-table-head">
                                    <span>ЗАДАНИЕ</span>
                                    <span>УЧАСТИЕ</span>
                                    <span>ВЕРНО</span>
                                    <span>ОШИБКИ</span>
                                    <span>ПРОВЕРИТЬ</span>
                                  </div>
                                  {report.tasks.map((item) => (
                                    <button
                                      key={item.id}
                                      onClick={() => {
                                        setClassTab("tasks");
                                        setSelectedTaskId(item.id);
                                        setFilter("all");
                                      }}
                                    >
                                      <strong>{item.title}</strong>
                                      <span>
                                        {item.participated}/{report.memberCount}
                                      </span>
                                      <span className="cell-good">
                                        {item.correct}
                                      </span>
                                      <span className="cell-bad">
                                        {item.incorrect}
                                      </span>
                                      <span className="cell-wait">
                                        {item.needsReview}
                                      </span>
                                    </button>
                                  ))}
                                </div>
                              ) : (
                                <Empty
                                  icon="chart"
                                  title="Данных пока нет"
                                  text="Выдайте задание, чтобы увидеть прогресс класса."
                                />
                              )}
                            </>
                          )}
                        </>
                      ) : (
                        <Empty
                          icon="classes"
                          title="Выберите класс"
                          text="Создайте класс и добавьте учеников, чтобы начать работу."
                          action={
                            <Button
                              icon="plus"
                              onClick={() => setModal({ kind: "class" })}
                            >
                              Новый класс
                            </Button>
                          }
                        />
                      )}
                    </div>
                  </div>
                </>
              )}
              {view === "materials" && (
                <>
                  <div className="page-heading">
                    <div>
                      <span className="eyebrow">БИБЛИОТЕКА ДЛЯ УРОКОВ</span>
                      <h1>Переводы</h1>
                      <p>
                        Используйте готовые предложения или создавайте свои.
                      </p>
                    </div>
                    <Button
                      icon="plus"
                      onClick={() => setModal({ kind: "translation" })}
                    >
                      Создать перевод
                    </Button>
                  </div>
                  <div className="material-grid">
                    <section className="surface">
                      <div className="section-head">
                        <div>
                          <span className="eyebrow">СВОИ МАТЕРИАЛЫ</span>
                          <h2>Мои переводы</h2>
                        </div>
                        <span className="count-pill">
                          {ownTranslations.length}
                        </span>
                      </div>
                      {ownTranslations.length ? (
                        <div className="material-list">
                          {ownTranslations.map((item) => (
                            <article key={item.id} className="material-card">
                              <div className="material-top">
                                <span className="source-badge own">
                                  <Icon name="spark" size={14} /> Свой перевод
                                </span>
                                <small>{item.languagePair}</small>
                              </div>
                              <h3>{item.sourceText}</h3>
                              <p>{item.translationText}</p>
                              <div className="material-actions">
                                <Button variant="outline" icon="alert" onClick={() => requestExpert(item)}>
                                  Запросить экспертизу
                                </Button>
                                <Button
                                  variant="light"
                                  icon="arrow"
                                  disabled={!selectedClassId}
                                  onClick={() => {
                                    setView("classes");
                                    setModal({
                                      kind: "task",
                                      source: `own:${item.id}`,
                                    });
                                  }}
                                >
                                  Выдать классу
                                </Button>
                                <button
                                  title="Изменить"
                                  onClick={() =>
                                    setModal({ kind: "translation", item })
                                  }
                                >
                                  <Icon name="edit" size={18} />
                                </button>
                                <button
                                  title="Удалить"
                                  onClick={() => deleteTranslation(item)}
                                >
                                  <Icon name="trash" size={18} />
                                </button>
                              </div>
                            </article>
                          ))}
                        </div>
                      ) : (
                        <Empty
                          icon="book"
                          title="Свои переводы появятся здесь"
                              text="Добавьте предложение и предложенный вариант перевода для своего урока."
                          action={
                            <Button
                              variant="outline"
                              icon="plus"
                              onClick={() => setModal({ kind: "translation" })}
                            >
                              Создать перевод
                            </Button>
                          }
                        />
                      )}
                    </section>
                    <section className="surface">
                      <div className="section-head">
                        <div>
                          <span className="eyebrow">
                            ОСНОВНАЯ БАЗА ПЕРЕВОДОВ
                          </span>
                          <h2>Готовый каталог</h2>
                        </div>
                        <span className="count-pill">
                          {catalog.length}
                          {catalogHasMore ? "+" : ""}
                        </span>
                      </div>
                      <label className="search-box">
                        <Icon name="search" size={19} />
                        <input
                          value={catalogQuery}
                          onChange={(event) =>
                            setCatalogQuery(event.target.value)
                          }
                          placeholder="Найти среди загруженных переводов"
                        />
                      </label>
                      <div className="material-list">
                        {visibleCatalog.map((item) => (
                          <article key={item.id} className="material-card">
                            <div className="material-top">
                              <span
                                className={`source-badge ${item.status || "pending"}`}
                              >
                                {statusLabel(item.status || "")}
                              </span>
                              <small>
                                ★ {item.rating ?? 0} · {item.languagePair}
                              </small>
                            </div>
                            <h3>{item.sourceText}</h3>
                            <p>{item.translationText}</p>
                            <div className="material-actions">
                              <Button
                                variant="light"
                                icon="arrow"
                                disabled={!selectedClassId}
                                onClick={() => {
                                  setView("classes");
                                  setModal({
                                    kind: "task",
                                    source: `catalog:${item.id}`,
                                  });
                                }}
                              >
                                Выдать классу
                              </Button>
                            </div>
                          </article>
                        ))}
                        {!visibleCatalog.length && (
                          <p className="no-search">
                            Переводы не найдены среди загруженных.
                          </p>
                        )}
                      </div>
                      {catalogHasMore && (
                        <button
                          className="load-more"
                          onClick={() =>
                            loadMoreCatalog(catalogOffset).catch(fail)
                          }
                        >
                          Показать ещё переводы <Icon name="arrow" size={16} />
                        </button>
                      )}
                    </section>
                  </div>
                </>
              )}
              {view === "my-tasks" && (
                <>
                  <div className="page-heading">
                    <div>
                      <span className="eyebrow">КАК УЧЕНИК</span>
                      <h1>Мне выдали</h1>
                      <p>Задания классов, в которые вас добавили.</p>
                    </div>
                  </div>
                  {studentTasks.length ? (
                    <div className="my-task-grid">
                      {studentTasks.map((item) => (
                        <StudentAssignment
                          key={item.id}
                          task={item}
                          onSaved={async () => {
                            await loadBase();
                            show("Ответ сохранён.");
                          }}
                          onError={fail}
                        />
                      ))}
                    </div>
                  ) : (
                    <Empty
                      icon="student"
                      title="Новых заданий нет"
                      text="Когда вас добавят в класс и выдадут задание, оно появится здесь."
                    />
                  )}
                </>
              )}
            </>
          )}
        </main>
        <footer className="app-footer">
          <span>© Башперевод · Кабинет преподавателя</span>
          <span>Создано для тех, кто помогает понять язык.</span>
        </footer>
      </div>
      {toast && (
        <div className="toast" role="status">
          <Icon name="spark" size={18} />
          {toast}
          <button onClick={() => setToast("")} aria-label="Закрыть">
            <Icon name="close" size={16} />
          </button>
        </div>
      )}
      {modal && (
        <EditorModal
          modal={modal}
          selectedClassId={selectedClassId}
          ownTranslations={ownTranslations}
          catalog={catalog}
          onClose={() => setModal(null)}
          onSave={async (path, method, body, message) => {
            const result = await doMutation(
              () => api(path, method, body),
              message,
            );
            if (
              modal.kind === "class" &&
              !modal.item &&
              result &&
              typeof result === "object" &&
              "id" in result
            )
              setSelectedClassId(String(result.id));
          }}
        />
      )}
    </div>
  );
}

function StudentAssignment({
  task,
  onSaved,
  onError,
}: {
  task: StudentTask;
  onSaved: () => Promise<void>;
  onError: (cause: unknown) => void;
}) {
  const [answer, setAnswer] = useState(task.answer || "");
  const [busy, setBusy] = useState(false);
  const expired = !!task.dueAt && new Date(task.dueAt) < new Date();
  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      await api(`/api/lms/me/tasks/${task.id}/submission`, "PUT", { answer });
      await onSaved();
    } catch (cause) {
      onError(cause);
    } finally {
      setBusy(false);
    }
  }
  return (
    <article className="my-task-card">
      <div className="my-task-top">
        <span className="source-badge own">{task.className}</span>
        <span
          className={`status ${task.submittedAt ? "status-correct" : "status-review"}`}
        >
          {task.submittedAt ? "Ответ отправлен" : "Ожидает ответа"}
        </span>
      </div>
      <h2>{task.title}</h2>
      <div className="my-task-prompt">
        <small>ПЕРЕВЕДИТЕ ПРЕДЛОЖЕНИЕ</small>
        <p>{task.sourceText}</p>
      </div>
      <form onSubmit={submit}>
        <label>
          Ваш перевод
          <textarea
            value={answer}
            onChange={(event) => setAnswer(event.target.value)}
            maxLength={5000}
            required
            placeholder="Напишите перевод здесь…"
          />
        </label>
        <div className="my-task-foot">
          <span>
            <Icon name="clock" size={16} />
            {task.dueAt ? `До ${formatDate(task.dueAt, true)}` : "Без срока"}
          </span>
          <Button type="submit" icon="arrow" disabled={busy || expired}>
            {task.submittedAt ? "Обновить ответ" : "Отправить ответ"}
          </Button>
        </div>
      </form>
      {expired && <p className="form-hint">Срок сдачи истёк.</p>}
    </article>
  );
}

function EditorModal({
  modal,
  selectedClassId,
  ownTranslations,
  catalog,
  onClose,
  onSave,
}: {
  modal: NonNullable<Modal>;
  selectedClassId: string | null;
  ownTranslations: Translation[];
  catalog: Translation[];
  onClose: () => void;
  onSave: (
    path: string,
    method: string,
    body: unknown,
    message: string,
  ) => Promise<void>;
}) {
  const classItem = modal.kind === "class" ? modal.item : undefined;
  const memberItem = modal.kind === "member" ? modal.item : undefined;
  const translationItem = modal.kind === "translation" ? modal.item : undefined;
  const taskItem = modal.kind === "task" ? modal.item : undefined;
  const editing = Boolean(modal.item);
  const initialSource =
    modal.kind === "task"
      ? modal.source ||
        (taskItem?.teacherTranslationId
          ? `own:${taskItem.teacherTranslationId}`
          : taskItem?.catalogCaseId
            ? `catalog:${taskItem.catalogCaseId}`
            : "")
      : "";
  const [name, setName] = useState(
    modal.kind === "class" ? classItem?.name || "" : "",
  );
  const [description, setDescription] = useState(
    modal.kind === "class" ? classItem?.description || "" : "",
  );
  const [username, setUsername] = useState("");
  const [displayName, setDisplayName] = useState(
    modal.kind === "member" ? memberItem?.displayName || "" : "",
  );
  const [sourceText, setSourceText] = useState(
    modal.kind === "translation" ? translationItem?.sourceText || "" : "",
  );
  const [translationText, setTranslationText] = useState(
    modal.kind === "translation" ? translationItem?.translationText || "" : "",
  );
  const [languagePair, setLanguagePair] = useState(
    modal.kind === "translation"
      ? translationItem?.languagePair || "ba-ru"
      : "ba-ru",
  );
  const [title, setTitle] = useState(
    modal.kind === "task" ? taskItem?.title || "" : "",
  );
  const [source, setSource] = useState(initialSource);
  const [dueAt, setDueAt] = useState(
    modal.kind === "task" && taskItem?.dueAt
      ? new Date(
          new Date(taskItem.dueAt).getTime() -
            new Date().getTimezoneOffset() * 60000,
        )
          .toISOString()
          .slice(0, 16)
      : "",
  );
  const [busy, setBusy] = useState(false);
  const modalTitle =
    modal.kind === "class"
      ? classItem
        ? "Изменить класс"
        : "Новый класс"
      : modal.kind === "member"
        ? memberItem
          ? "Имя ученика"
          : "Добавить ученика"
        : modal.kind === "translation"
          ? translationItem
            ? "Изменить перевод"
            : "Свой перевод"
          : taskItem
            ? "Изменить задание"
            : "Выдать задание";
  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    try {
      if (modal.kind === "class")
        await onSave(
          `/api/lms/teacher/classes${classItem ? `/${classItem.id}` : ""}`,
          classItem ? "PUT" : "POST",
          { name, description },
          classItem ? "Класс обновлён." : "Класс создан.",
        );
      if (modal.kind === "member" && selectedClassId)
        await onSave(
          `/api/lms/teacher/classes/${selectedClassId}/members${memberItem ? `/${memberItem.userId}` : ""}`,
          memberItem ? "PUT" : "POST",
          memberItem ? { displayName } : { username },
          memberItem ? "Имя сохранено." : "Ученик добавлен.",
        );
      if (modal.kind === "translation")
        await onSave(
          `/api/lms/teacher/translations${translationItem ? `/${translationItem.id}` : ""}`,
          translationItem ? "PUT" : "POST",
          { sourceText, translationText, languagePair },
          translationItem ? "Перевод обновлён." : "Перевод создан.",
        );
      if (modal.kind === "task" && selectedClassId) {
        const [sourceType, translationId] = source.split(":");
        await onSave(
          `/api/lms/teacher/classes/${selectedClassId}/tasks${taskItem ? `/${taskItem.id}` : ""}`,
          taskItem ? "PUT" : "POST",
          {
            title,
            sourceType,
            translationId,
            dueAt: dueAt ? new Date(dueAt).toISOString() : null,
          },
          taskItem ? "Задание обновлено." : "Задание выдано.",
        );
      }
    } finally {
      setBusy(false);
    }
  }
  const sourceExists =
    ownTranslations.some((value) => `own:${value.id}` === source) ||
    catalog.some((value) => `catalog:${value.id}` === source);
  return (
    <div
      className="modal-backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose();
      }}
    >
      <div
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-label={modalTitle}
      >
        <div className="modal-top">
          <div>
            <span className="eyebrow">КАБИНЕТ ПРЕПОДАВАТЕЛЯ</span>
            <h2>{modalTitle}</h2>
          </div>
          <button
            className="icon-button"
            onClick={onClose}
            aria-label="Закрыть"
          >
            <Icon name="close" size={21} />
          </button>
        </div>
        <form onSubmit={submit} className="editor-form">
          {modal.kind === "class" && (
            <>
              <label>
                Название класса
                <input
                  autoFocus
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  maxLength={200}
                  placeholder="Например, 8А · Башкирский язык"
                  required
                />
              </label>
              <label>
                Описание
                <textarea
                  value={description}
                  onChange={(event) => setDescription(event.target.value)}
                  maxLength={2000}
                  placeholder="Коротко о классе и программе"
                />
              </label>
            </>
          )}
          {modal.kind === "member" &&
            (memberItem ? (
              <label>
                Имя в списке класса
                <input
                  autoFocus
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                  maxLength={200}
                  required
                />
              </label>
            ) : (
              <>
                <label>
                  Логин ученика
                  <input
                    autoFocus
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                    maxLength={100}
                    placeholder="Логин зарегистрированного ученика"
                    required
                  />
                </label>
                <p className="form-hint">
                  Ученик должен уже иметь аккаунт на платформе.
                </p>
              </>
            ))}
          {modal.kind === "translation" && (
            <>
              <label>
                Предложение
                <textarea
                  autoFocus
                  value={sourceText}
                  onChange={(event) => setSourceText(event.target.value)}
                  maxLength={5000}
                  placeholder="Введите предложение для перевода"
                  required
                />
              </label>
              <label>
                Предложенный перевод
                <textarea
                  value={translationText}
                  onChange={(event) => setTranslationText(event.target.value)}
                  maxLength={5000}
                  placeholder="Введите свой вариант перевода"
                  required
                />
              </label>
              <label>
                Направление
                <select
                  value={languagePair}
                  onChange={(event) => setLanguagePair(event.target.value)}
                >
                  <option value="ba-ru">Башкирский → русский</option>
                  <option value="ru-ba">Русский → башкирский</option>
                </select>
              </label>
            </>
          )}
          {modal.kind === "task" && (
            <>
              <label>
                Название задания
                <input
                  autoFocus
                  value={title}
                  onChange={(event) => setTitle(event.target.value)}
                  maxLength={200}
                  placeholder="Например, Перевод приветствия"
                  required
                />
              </label>
              <label>
                Предложение для перевода
                <select
                  value={source}
                  onChange={(event) => setSource(event.target.value)}
                  required
                >
                  <option value="">Выберите перевод</option>
                  {!sourceExists && source && (
                    <option value={source}>
                      {taskItem?.sourceText || "Выбранный перевод"}
                    </option>
                  )}
                  {ownTranslations.length > 0 && (
                    <optgroup label="Мои переводы">
                      {ownTranslations.map((value) => (
                        <option key={value.id} value={`own:${value.id}`}>
                          {value.sourceText.slice(0, 90)}
                        </option>
                      ))}
                    </optgroup>
                  )}
                  {catalog.length > 0 && (
                    <optgroup label="Готовый каталог">
                      {catalog.map((value) => (
                        <option key={value.id} value={`catalog:${value.id}`}>
                          {value.sourceText.slice(0, 85)} · ★{" "}
                          {value.rating ?? 0}
                        </option>
                      ))}
                    </optgroup>
                  )}
                </select>
              </label>
              <label>
                Срок сдачи <span className="optional">необязательно</span>
                <input
                  type="datetime-local"
                  value={dueAt}
                  onChange={(event) => setDueAt(event.target.value)}
                />
              </label>
              <div className="modal-info">
                <Icon name="spark" size={18} />
                <span>
                  Ученики увидят предложение без варианта перевода. Решение появится после проверки сообществом или экспертом.
                </span>
              </div>
            </>
          )}
          <div className="modal-footer">
            <Button variant="outline" onClick={onClose}>
              Отмена
            </Button>
            <Button
              type="submit"
              icon={editing ? "check" : "plus"}
              disabled={busy || (modal.kind === "task" && !source)}
            >
              {busy
                ? "Сохраняем…"
                : editing
                  ? "Сохранить"
                  : modal.kind === "task"
                    ? "Выдать задание"
                    : "Создать"}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
