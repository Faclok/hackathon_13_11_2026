import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/session";
import {
  answerLevel, getCurrentLevel, getLearningProgress, getLevelStatistics,
  type ExerciseAnswer, type Level, type LevelAnswerResult, type LearningProgress, type LevelStatistic,
} from "../api/learning";
import LearningExercise from "../components/LearningExercise";
import { PageTitle, ProgressBar } from "../components/ui";
import { useUserStore } from "../store";
import { plural } from "../utils/plural";

interface PendingAttempt { levelId: string; levelNumber: number; questionId: string; attemptToken: string; answer: ExerciseAnswer }
const pendingKey = (userId: string) => `bashkir-learning-pending-v1:${userId}`;
function readPending(userId: string): PendingAttempt | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(pendingKey(userId)) ?? "null");
    return value?.levelId && value?.questionId && value?.attemptToken && value?.answer ? value as PendingAttempt : null;
  } catch { return null; }
}
function message(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 401) { useUserStore.getState().logout(); return "Сессия закончилась. Войдите снова, затем продолжите уровень."; }
    if (error.status === 403) return "Нет доступа к обучению для этого аккаунта.";
    if (error.status === 409) return "Состояние уровня изменилось. Обновите текущий вопрос.";
    return error.message;
  }
  return "Не удалось связаться с сервером. Проверьте соединение и повторите попытку.";
}

export default function Learn() {
  const user = useUserStore((state) => state.user!);
  const userId = useUserStore((state) => state.user!.id);
  const [progress, setProgress] = useState<LearningProgress | null>(null);
  const [history, setHistory] = useState<LevelStatistic[]>([]);
  const [level, setLevel] = useState<Level | null>(null);
  const [completedNumber, setCompletedNumber] = useState<number | null>(null);
  const [completedCoins, setCompletedCoins] = useState(0);
  const [completedXp, setCompletedXp] = useState(0);
  const [feedback, setFeedback] = useState<LevelAnswerResult | null>(null);
  const [pending, setPending] = useState<PendingAttempt | null>(() => readPending(userId));
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [noMaterial, setNoMaterial] = useState(false);
  const [error, setError] = useState("");

  const refreshOverview = useCallback(async () => {
    const [nextProgress, nextHistory] = await Promise.all([getLearningProgress(), getLevelStatistics()]);
    setProgress(nextProgress);
    setHistory(nextHistory);
  }, []);
  useEffect(() => {
    let active = true;
    Promise.all([getLearningProgress(), getLevelStatistics()]).then(([nextProgress, nextHistory]) => {
      if (active) { setProgress(nextProgress); setHistory(nextHistory); }
    }).catch((cause) => { if (active) setError(message(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const current = async () => {
    const next = await getCurrentLevel();
    setLevel(next);
    setNoMaterial(next === null);
    return next;
  };
  const start = async () => {
    setBusy(true); setError(""); setNoMaterial(false);
    try { await current(); setCompletedNumber(null); }
    catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  };
  const send = async (attempt: PendingAttempt) => {
    setBusy(true); setError("");
    try {
      const result = await answerLevel(attempt.levelId, attempt.questionId, attempt.attemptToken, attempt.answer);
      sessionStorage.removeItem(pendingKey(userId));
      setPending(null);
      setFeedback(result);
      if (result.levelCompleted) {
        setLevel(null);
        setCompletedNumber(attempt.levelNumber);
        setCompletedCoins(result.coinsAwarded);
        setCompletedXp(result.xpAwarded);
        try { await Promise.all([refreshOverview(), useUserStore.getState().refreshProfile()]); } catch (cause) { setError(message(cause)); }
      } else {
        try { await current(); } catch (cause) { setLevel(null); setError(message(cause)); }
      }
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 409) {
        sessionStorage.removeItem(pendingKey(userId));
        setPending(null);
        try {
          await refreshOverview();
          const latest = await getLevelStatistics(0, 5);
          if (latest.some((item) => item.number === attempt.levelNumber && item.completedAt)) {
            const completed = latest.find((item) => item.number === attempt.levelNumber && item.completedAt)!;
            setCompletedCoins(completed.coinsAwarded);
            setCompletedXp(completed.xpAwarded);
            setCompletedNumber(attempt.levelNumber); setLevel(null); setFeedback(null);
          } else await current();
        } catch { /* Recovery remains available through the page controls. */ }
      }
      setError(message(cause));
    } finally { setBusy(false); }
  };
  const submit = (answer: ExerciseAnswer) => {
    if (!level || busy || pending) return;
    const attempt: PendingAttempt = {
      levelId: level.id, levelNumber: level.number,
      questionId: level.question.id, attemptToken: level.question.attemptToken, answer,
    };
    sessionStorage.setItem(pendingKey(userId), JSON.stringify(attempt));
    setPending(attempt);
    void send(attempt);
  };
  const continueLevel = async () => {
    setFeedback(null);
    if (completedNumber !== null) return;
    if (level) return;
    setBusy(true);
    try { await current(); setError(""); }
    catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  };
  const refreshAudio = async () => {
    setBusy(true); setError("");
    try { await current(); } catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  };
  const showOverview = !level && completedNumber === null && !feedback;

  return <>
    <PageTitle subtitle="Личные уровни без дневного лимита">Учить башкирский</PageTitle>
    {error && <div className="surface learning-alert" role="alert"><p>{error}</p>{!pending && <button className="button button-soft" onClick={() => { void refreshOverview().catch((cause) => setError(message(cause))); }} disabled={busy}>Обновить данные</button>}</div>}
    {loading && <p className="loading-text" role="status">Загружаем прогресс…</p>}
    {showOverview && !loading && <>
      {progress && <section className="surface learning-progress">
        <div><strong>Твой путь</strong><span>Пройдено: {plural(progress.completedLevels, "уровень", "уровня", "уровней")}</span></div>
        <p className="learning-totals">{plural(progress.attempts, "попытка", "попытки", "попыток")} · {plural(progress.mistakes, "ошибка", "ошибки", "ошибок")} · {plural(progress.completedBosses, "босс", "босса", "боссов")}</p>
        <ProgressBar value={progress.completedLevels % 5} max={5} />
        <p className="learning-hint">До следующего Меча: {5 - progress.completedLevels % 5} ур. · В инвентаре: {plural(user.swords ?? 0, "Меч", "Меча", "Мечей")}</p>
        {progress.availableBosses > 0 && <Link className="button button-soft full-width" to="/boss">Доступно: {plural(progress.availableBosses, "Босс", "Босса", "Боссов")}</Link>}
      </section>}
      <section className="surface intro-card">
        <span className="eyebrow-label">УРОВЕНЬ {history.find((item) => !item.completedAt)?.number ?? (progress?.completedLevels ?? 0) + 1}</span>
        <h2>Продолжай обучение</h2>
        <p>В каждом уровне от 6 до 10 вопросов. За уровень получишь число монет, равное количеству заданий, делённому на 3 без остатка, и 5 XP за каждый вопрос. За каждые 5 уровней — Меч.</p>
        {pending ? <button className="button button-primary full-width" disabled={busy} onClick={() => void send(pending)}>Восстановить ответ</button>
          : <button className="button button-primary full-width" disabled={busy} onClick={() => void start()}>{history.some((item) => !item.completedAt) ? "Продолжить уровень" : "Начать уровень"}</button>}
        {noMaterial && <p className="learning-hint" role="status">Пока нет подходящих материалов. Попробуй позже.</p>}
      </section>
      {history.length > 0 && <section className="surface learning-history"><h2>История уровней</h2><ul>{history.map((item) => <li key={item.number}><span>Уровень {item.number}{item.completedAt ? " · пройден" : " · в процессе"}</span><small>{item.correctCount}/{plural(item.questionCount, "вопрос", "вопроса", "вопросов")} · {plural(item.mistakes, "ошибка", "ошибки", "ошибок")}</small></li>)}</ul></section>}
    </>}
    {level && <>
      <div className="round-progress"><span>Уровень {level.number} · решено {level.correctCount}/{level.questionCount}</span><ProgressBar value={level.correctCount} max={level.questionCount} /></div>
      <section className="surface task-panel">
        {feedback ? <div className={`answer-feedback ${feedback.recognized ? "correct" : "incorrect"}`} role="status"><strong>{feedback.recognized ? "Верно!" : "Пока не совпало"}</strong>{!feedback.recognized && feedback.correctAnswer && <p><strong>Правильный ответ: {feedback.correctAnswer}</strong></p>}<p>{feedback.recognized ? "Ответ принят. Продолжай." : "Вопрос вернётся в конец очереди. Попробуешь снова позже."}</p><button className="button button-primary full-width" onClick={() => void continueLevel()} disabled={busy}>Продолжить</button></div>
          : pending ? <div role="status"><p>Ответ сохранён для безопасного повтора.</p><button className="button button-primary full-width" disabled={busy} onClick={() => void send(pending)}>{busy ? "Отправляем…" : "Повторить отправку"}</button></div>
          : <LearningExercise key={`${level.question.id}:${level.question.attemptToken}`} levelId={level.id} question={level.question} busy={busy} onSubmit={submit} onRefreshAudio={() => void refreshAudio()} />}
      </section>
    </>}
    {!level && feedback && completedNumber === null && <section className="surface task-panel"><div className={`answer-feedback ${feedback.recognized ? "correct" : "incorrect"}`} role="status"><strong>{feedback.recognized ? "Ответ принят" : "Ответ пока не совпал"}</strong>{!feedback.recognized && feedback.correctAnswer && <p><strong>Правильный ответ: {feedback.correctAnswer}</strong></p>}<p>Обнови текущий вопрос, чтобы продолжить.</p><button className="button button-primary full-width" disabled={busy} onClick={() => void continueLevel()}>Загрузить текущий вопрос</button></div></section>}
    {completedNumber !== null && <section className="surface result-card" role="status"><div className="result-symbol">✓</div><h2>Уровень {completedNumber} пройден!</h2><p>Награда: {plural(completedCoins, "монета", "монеты", "монет")} · +{completedXp} XP{completedNumber % 5 === 0 ? " и Меч ⚔️" : ""}.</p><p>Все вопросы решены. Следующий уровень начнётся, когда ты нажмёшь кнопку.</p>{feedback && <p>Последний ответ принят.</p>}<button className="button button-primary full-width" disabled={busy} onClick={() => { setFeedback(null); void start(); }}>Начать следующий уровень</button><button className="button button-soft full-width" onClick={() => { setCompletedNumber(null); setFeedback(null); }} disabled={busy}>К истории уровней</button></section>}
  </>;
}
