import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/session";
import { currentBoss, currentLevel, learningHistory, learningProgress, sendLearningAnswer, type Boss, type ExerciseAnswer, type Level, type LevelStats, type Progress, type ReviewAnswer, type SavedAnswer } from "../api/learning";
import { useUserStore } from "../store";
import { BossReview, LearningExercise } from "../components/LearningExercise";
import { PageTitle, ProgressBar } from "../components/ui";

export default function LearningFlow({ bossMode = false }: { bossMode?: boolean }) {
  const userId = useUserStore(s => s.user!.id);
  const storageKey = `school-answer:${userId}:${bossMode ? "boss" : "level"}`;
  const [level, setLevel] = useState<Level>();
  const [boss, setBoss] = useState<Boss>();
  const [progress, setProgress] = useState<Progress>();
  const [history, setHistory] = useState<LevelStats[]>([]);
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const [error, setError] = useState("");
  const [result, setResult] = useState("");
  const [started, setStarted] = useState(false);
  const [pending, setPending] = useState<SavedAnswer | null>(() => {
    try {
      const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? "null") as SavedAnswer | null;
      return saved?.path.startsWith(bossMode ? "/api/learning/bosses/" : "/api/learning/levels/") ? saved : null;
    } catch { return null; }
  });
  const clearPending = () => {
    setPending(null);
    try { sessionStorage.removeItem(storageKey); } catch { /* In-memory state is already cleared. */ }
  };
  async function totals() {
    const [value, rows] = await Promise.all([learningProgress(), learningHistory()]);
    setProgress(value); setHistory(rows);
  }
  useEffect(() => { totals().catch(() => setError("Не удалось загрузить прогресс. Попробуй ещё раз.")); }, []);
  async function load() {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError(""); setResult(""); setStarted(true);
    try {
      if (bossMode) setBoss(await currentBoss()); else setLevel(await currentLevel());
      await totals();
    } catch (cause) { setError(cause instanceof ApiError && cause.status === 409 ? "Состояние изменилось. Попробуй открыть задание ещё раз." : "Не удалось загрузить задание. Попробуй ещё раз."); }
    finally { lock.current = false; setBusy(false); }
  }
  async function send(saved: SavedAnswer) {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError("");
    setPending(saved);
    // Keep the exact token and answer until a definitive server response.
    try { sessionStorage.setItem(storageKey, JSON.stringify(saved)); } catch { /* In-memory retry remains available. */ }
    try {
      if (bossMode) {
        const receipt = await sendLearningAnswer<{ bossCompleted: boolean }>(saved);
        setResult(receipt.bossCompleted ? "Босс пройден! Три перевода сохранены для согласования." : "Перевод сохранён для согласования.");
        setBoss(undefined);
      } else {
        const receipt = await sendLearningAnswer<{ recognized: boolean; levelCompleted: boolean }>(saved);
        setResult(receipt.levelCompleted ? "Уровень пройден! Можно начать следующий." : receipt.recognized ? "Верно! Ответ сохранён." : "Пока неверно. Вопрос вернётся в конце уровня.");
        setLevel(undefined);
      }
      clearPending();
      void totals().catch(() => {});
      void useUserStore.getState().refreshProfile().catch(() => {});
    } catch (cause) {
      if (cause instanceof ApiError && [400, 404, 409].includes(cause.status)) {
        clearPending(); setLevel(undefined); setBoss(undefined);
        setError("Задание изменилось или срок выдачи истёк. Открой его заново.");
      } else setError("Не удалось получить подтверждение. Повтори сохранённый ответ — новая попытка не добавится.");
    } finally { lock.current = false; setBusy(false); }
  }
  const answerLevel = (answer: ExerciseAnswer) => {
    if (level) void send({ path: `/api/learning/levels/${level.id}/questions/${level.question.id}/answer`, body: { attemptToken: level.question.attemptToken, answer } });
  };
  const answerBoss = (answer: ReviewAnswer) => {
    if (boss?.question) void send({ path: `/api/learning/bosses/${boss.id}/questions/${boss.question.id}/answer`, body: answer });
  };
  return <>
    <PageTitle subtitle={bossMode ? "Три перевода для сообщества" : "Учись в своём темпе"}>{bossMode ? "Испытание Босса" : "Текущий уровень"}</PageTitle>
    {progress && <section className="surface profile-section learning-totals">
      <p>Пройдено уровней: <strong>{progress.completedLevels}</strong></p><p>Боссов: <strong>{progress.completedBosses}</strong> · доступно: <strong>{progress.availableBosses}</strong></p>
      <p>Попыток: {progress.attempts} · ошибок: {progress.mistakes}</p>
    </section>}
    <section className="surface profile-section learning-flow">
      {error && <p role="alert" className="learning-error">{error}</p>}
      {result && <p role="status" className="learning-result">{result}</p>}
      {pending ? <><p>Ответ сохранён и ожидает подтверждения сервера.</p><button className="button button-primary full-width" disabled={busy} onClick={() => void send(pending)}>Повторить отправку</button></> : <>
        {level && !bossMode && <><p className="learning-caption">Уровень {level.number} · {level.correctCount}/{level.questionCount}</p><ProgressBar value={level.correctCount} max={level.questionCount} /><LearningExercise key={level.question.attemptToken} question={level.question} disabled={busy} onAnswer={answerLevel} /></>}
        {boss && bossMode && <><p className="learning-caption">Босс за {boss.checkpoint} уровней · {boss.completedCount}/{boss.questionCount}</p><ProgressBar value={boss.completedCount} max={boss.questionCount} />{boss.question ? <BossReview key={boss.question.id} review={boss.question} disabled={busy} onAnswer={answerBoss} /> : <p>Новых переводов пока нет. Прогресс босса сохранён; вернись позже.</p>}</>}
        {!level && !boss && <p>{bossMode ? "За каждые пять пройденных уровней открывается один босс. Его можно пройти позже." : started && !result && !busy && !error ? "Для нового уровня пока нет материалов. Попробуй позже." : "Продолжай с того же вопроса на сайте или в Telegram."}</p>}
        {(!level && !boss || bossMode && boss && !boss.question) && <button className="button button-primary full-width" disabled={busy} onClick={() => void load()}>{busy ? "Загружаем…" : result ? "Продолжить" : bossMode ? "Открыть босса" : "Открыть текущий уровень"}</button>}
      </>}
    </section>
    <Link className="button button-soft full-width" to={bossMode ? "/learn" : "/boss"}>{bossMode ? "Продолжить обучение" : "Перейти к боссу"}</Link>
    {!bossMode && history.length > 0 && <section className="surface profile-section learning-history"><h2>История уровней</h2>{history.map(row => <div key={row.number}><strong>Уровень {row.number}</strong><p>{row.correctCount}/{row.questionCount} · попыток: {row.attempts} · ошибок: {row.mistakes}{row.completedAt ? " · пройден" : " · в процессе"}</p></div>)}</section>}
  </>;
}
