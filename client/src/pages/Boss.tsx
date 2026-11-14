import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/session";
import {
  answerBoss, answerTeacherBoss, getBossLevel, getLearningProgress, getNextBossSource, startBoss,
  type BossAnswer, type BossLevel, type BossQuestion, type LearningProgress, type TeacherBossTask,
} from "../api/learning";
import { PageTitle, ProgressBar } from "../components/ui";
import { useUserStore } from "../store";

interface PendingBoss { bossId: string; assignmentId: string; answer: BossAnswer }
const pendingKey = (userId: string) => `bashkir-boss-pending-v1:${userId}`;
function readPending(userId: string): PendingBoss | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(pendingKey(userId)) ?? "null");
    return value?.bossId && value?.assignmentId && value?.answer ? value as PendingBoss : null;
  } catch { return null; }
}
function message(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 401) { useUserStore.getState().logout(); return "Сессия закончилась. Войдите снова, чтобы продолжить Босса."; }
    if (error.status === 403) return "Нет доступа к проверке переводов для этого аккаунта.";
    if (error.status === 409) return "Выдача изменилась или истекла. Обновите задание.";
    return error.message;
  }
  return "Не удалось связаться с сервером. Проверьте соединение и повторите попытку.";
}

export default function Boss() {
  const userId = useUserStore((state) => state.user!.id);
  const swords = useUserStore((state) => state.user?.swords ?? 0);
  const [progress, setProgress] = useState<LearningProgress | null>(null);
  const [boss, setBoss] = useState<BossLevel | null>(null);
  const [teacherTask, setTeacherTask] = useState<TeacherBossTask | null>(null);
  const [pending, setPending] = useState<PendingBoss | null>(() => readPending(userId));
  const [feedback, setFeedback] = useState("");
  const [done, setDone] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    getLearningProgress().then((value) => { if (active) setProgress(value); })
      .catch((cause) => { if (active) setError(message(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  const refreshProgress = async () => setProgress(await getLearningProgress());
  const openBoss = async () => {
    setBusy(true); setError("");
    try {
      const source = await getNextBossSource();
      if (source?.source === "teacher") {
        const started = await startBoss();
        if (!started) { await refreshProgress(); return; }
        setTeacherTask(source.task);
        setBoss(null);
      } else {
        setTeacherTask(null);
        const next = await getBossLevel();
        setBoss(next);
        if (next === null) await refreshProgress();
      }
    } catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  };
  const submitTeacher = async (answer: string) => {
    if (!teacherTask || busy) return;
    setBusy(true); setError("");
    try {
      const result = await answerTeacherBoss(teacherTask.id, answer);
      setTeacherTask(null);
      if (result.bossCompleted) {
        setDone(true); setFeedback("");
        await Promise.all([refreshProgress(), useUserStore.getState().refreshProfile()]);
      } else {
        setFeedback("Ответ преподавателю принят. Переходим к следующему фрагменту.");
        await Promise.all([refreshProgress(), useUserStore.getState().refreshProfile()]);
        await openBoss();
      }
    } catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  };
  const send = async (attempt: PendingBoss) => {
    setBusy(true); setError("");
    try {
      const result = await answerBoss(attempt.bossId, attempt.assignmentId, attempt.answer);
      sessionStorage.removeItem(pendingKey(userId)); setPending(null);
      if (result.bossCompleted) {
        setBoss(null); setDone(true); setFeedback("");
        try { await Promise.all([refreshProgress(), useUserStore.getState().refreshProfile()]); } catch (cause) { setError(message(cause)); }
      } else {
        setFeedback(attempt.answer.action === "Skip" ? "Задание пропущено. Прогресс Босса не изменился." : result.archived ? "Ответ сохранён в истории. Переходим к следующему фрагменту." : "Ответ принят. Переходим к следующему фрагменту.");
        try { await openBoss(); await useUserStore.getState().refreshProfile(); }
        catch (cause) { setBoss(null); setError(message(cause)); }
      }
    } catch (cause) {
      if (cause instanceof ApiError && (cause.status === 409 || cause.status === 400 || cause.status === 404)) {
        sessionStorage.removeItem(pendingKey(userId)); setPending(null);
        try { await openBoss(); } catch { /* The refresh button remains available. */ }
      }
      setError(message(cause));
    } finally { setBusy(false); }
  };
  const submit = (answer: BossAnswer) => {
    if (!boss?.question || busy || pending) return;
    const attempt = { bossId: boss.id, assignmentId: boss.question.id, answer };
    sessionStorage.setItem(pendingKey(userId), JSON.stringify(attempt));
    setPending(attempt);
    void send(attempt);
  };
  return <>
    <PageTitle subtitle="Три проверки перевода">Испытание Босса</PageTitle>
    {error && <div className="surface learning-alert" role="alert"><p>{error}</p><button className="button button-soft" disabled={busy} onClick={() => void openBoss()}>Обновить задание</button></div>}
    {loading && <p className="loading-text" role="status">Проверяем доступ…</p>}
    {!loading && done && <section className="surface result-card" role="status"><div className="boss-emblem small">👑</div><h2>Босс пройден!</h2><p>За каждый ответ начислена 1 монета. Если перевод подтвердит большинство или эксперт, начислятся ещё 4.</p><Link className="button button-primary full-width" to="/learn">К обучению</Link>{(progress?.availableBosses ?? 0) > 0 && <button className="button button-soft full-width" onClick={() => { setDone(false); void openBoss(); }} disabled={busy}>Следующий Босс</button>}</section>}
    {!loading && !done && !boss && !teacherTask && <section className="surface intro-card">
      <div className={`boss-sword-cost ${swords > 0 ? "is-ready" : "is-empty"}`} role="status" aria-label={`Мечей в инвентаре: ${swords}. Для входа нужен 1 меч.`}>
        <span className="boss-sword-icon" aria-hidden="true">⚔️</span>
        <span className="boss-sword-value" aria-hidden="true"><strong>{swords}</strong><span>/1</span></span>
        <span className="boss-sword-label" aria-hidden="true">Для входа</span>
      </div>
      <span className="eyebrow-label">БӘЙГЕ · БОСС</span>
      <h2>{(progress?.availableBosses ?? 0) > 0 ? "Босс доступен" : "Нужен Меч"}</h2>
      <p>За каждые пять пройденных уровней получишь Меч. Один вход — один Меч. Задания преподавателя идут первыми.</p>
      {progress && <><p className="learning-hint">Пройдено уровней: {progress.completedLevels}</p><ProgressBar value={progress.completedLevels % 5} max={5} /></>}
      {pending ? <button className="button button-primary full-width" disabled={busy} onClick={() => void send(pending)}>Восстановить ответ</button> : (progress?.availableBosses ?? 0) > 0 ? <button className="button button-primary full-width" disabled={busy} onClick={() => void openBoss()}>Начать или продолжить Босса</button> : <Link className="button button-primary full-width" to="/learn">Продолжить обучение</Link>}
    </section>}
    {!loading && !done && teacherTask && <section className="surface task-panel boss-teacher-task">
      <span className="eyebrow-label">БОСС ОТ ПРЕПОДАВАТЕЛЯ · {teacherTask.className}</span>
      <h2>{teacherTask.title}</h2>
      <p className="bashkir-text" lang="ba">{teacherTask.sourceText}</p>
      <p className="learning-hint">Переведи фрагмент на русский. За ответ — 1 монета; если перевод позже подтвердит большинство или эксперт, ещё 4 монеты.</p>
      <form onSubmit={(event) => { event.preventDefault(); const value = new FormData(event.currentTarget).get("answer"); if (typeof value === "string" && value.trim()) void submitTeacher(value.trim()); }}>
        <label className="field-label" htmlFor="teacher-boss-answer">Твой перевод</label>
        <textarea id="teacher-boss-answer" name="answer" rows={4} maxLength={5000} required disabled={busy} />
        <button className="button button-primary full-width" disabled={busy}>Отправить преподавателю</button>
      </form>
    </section>}
    {!loading && !done && boss && <><div className="round-progress"><span>СЛУЧАЙНЫЙ БОСС · уровень {boss.checkpoint} · {boss.completedCount} из {boss.questionCount}</span><ProgressBar value={boss.completedCount} max={boss.questionCount} /></div>{feedback && <p className="neutral-feedback" role="status">{feedback}<button className="button button-primary full-width" onClick={() => setFeedback("")}>Продолжить</button></p>}{!feedback && <section className="surface task-panel boss-random-task">{pending ? <div role="status"><p>Ответ сохранён для безопасного повтора.</p><button className="button button-primary full-width" disabled={busy} onClick={() => void send(pending)}>{busy ? "Отправляем…" : "Повторить отправку"}</button></div> : boss.question ? <BossTask key={boss.question.id} question={boss.question} busy={busy} onSubmit={submit} onRefresh={() => void openBoss()} /> : <><h2>Ждём новый фрагмент</h2><p>Подходящий материал пока не найден. Прогресс этого Босса сохранён.</p><button className="button button-primary full-width" disabled={busy} onClick={() => void openBoss()}>Проверить снова</button></>}</section>}</>}
  </>;
}

function BossTask({ question, busy, onSubmit, onRefresh }: { question: BossQuestion; busy: boolean; onSubmit: (answer: BossAnswer) => void; onRefresh: () => void }) {
  const [custom, setCustom] = useState(question.kind === "translation-create");
  const [text, setText] = useState("");
  const submitCustom = (event: FormEvent) => { event.preventDefault(); if (text.trim()) onSubmit({ action: "Correct", correction: text.trim() }); };
  const expired = new Date(question.expiresAt).getTime() <= Date.now();
  const choice = question.kind === "translation-choice";
  return <>
    <span className="eyebrow-label">{question.kind === "translation-create" ? "СВОЙ ПЕРЕВОД" : choice ? "ВЫБОР ПЕРЕВОДА" : "ПРОВЕРКА ПЕРЕВОДА"}</span>
    <h2 className="bashkir-text" lang="ba">{question.original}</h2>
    <p className="learning-hint">За ответ — 1 монета. За подтверждённый позже перевод — ещё 4.</p>
    {question.kind === "translation-create" ? <p className="learning-hint">Предложи перевод этого фрагмента на русский.</p> : choice ? <p className="learning-hint">Выбери подходящий вариант или предложи свой перевод.</p> : <><p className="learning-hint">Подходит ли этот перевод?</p><div className="translation-panel"><span>ПРЕДЛОЖЕННЫЙ ПЕРЕВОД</span><p>{question.translation}</p></div></>}
    {expired && <><p className="error-text" role="alert">Срок выдачи истёк. Обнови задание.</p><button type="button" className="button button-primary full-width" disabled={busy} onClick={onRefresh}>Получить новый фрагмент</button></>}
    {!custom && !expired && <div className="options-list">
      {choice ? question.options.map((option) => <button type="button" className="option" key={option.id} disabled={busy} onClick={() => onSubmit({ action: "Confirm", selectedVariantId: option.id })}><span>{option.text}</span></button>)
        : <button type="button" className="button button-primary full-width" disabled={busy} onClick={() => onSubmit({ action: "Confirm" })}>Подтвердить перевод</button>}
    </div>}
    {!expired && question.allowCustom && (custom ? <form onSubmit={submitCustom}><label htmlFor="boss-correction" className="field-label">{question.kind === "translation-create" ? "Твой перевод" : "Свой вариант перевода"}</label><textarea id="boss-correction" rows={4} maxLength={1000} value={text} onChange={(event) => setText(event.target.value)} required disabled={busy} /><button className="button button-primary full-width" disabled={busy || !text.trim() || (question.kind === "translation-confirm" && text.trim() === question.translation)}>Отправить перевод</button>{question.kind !== "translation-create" && <button type="button" className="button button-soft full-width" disabled={busy} onClick={() => setCustom(false)}>К вариантам</button>}</form> : <button type="button" className="button button-soft full-width" disabled={busy} onClick={() => setCustom(true)}>Предложить свой перевод</button>)}
    {!expired && <button type="button" className="report-button" disabled={busy} onClick={() => onSubmit({ action: "Skip" })}>Пропустить фрагмент</button>}
  </>;
}
