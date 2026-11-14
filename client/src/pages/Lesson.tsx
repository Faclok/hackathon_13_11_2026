import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { getLessons } from "../api";
import { useProgressStore, useUserStore } from "../store";
import type { Exercise, Lesson as LessonType } from "../types";
import {
  Icon,
  IssueReportButton,
  Modal,
  ProgressBar,
  RewardResult,
} from "../components/ui";

type Answer = { correct: boolean; expected: string; prompt: string };
export function ExerciseView({
  exercise,
  onNext,
}: {
  exercise: Exercise;
  onNext: (answer: Answer) => void;
}) {
  const [selection, setSelection] = useState("");
  const [words, setWords] = useState<string[]>([]);
  const [left, setLeft] = useState("");
  const [matched, setMatched] = useState<string[]>([]);
  const [matchError, setMatchError] = useState(false);
  const [hadMatchError, setHadMatchError] = useState(false);
  const [answer, setAnswer] = useState<Answer | null>(null);
  const check = () => {
    if (exercise.type === "choice")
      setAnswer({
        correct: selection === exercise.correct,
        expected: exercise.correct,
        prompt: exercise.prompt,
      });
    if (exercise.type === "order")
      setAnswer({
        correct: words.join(" ") === exercise.correctOrder.join(" "),
        expected: exercise.correctOrder.join(" "),
        prompt: exercise.ruSentence,
      });
  };
  const match = (ru: string) => {
    if (exercise.type !== "match" || !left || answer) return;
    const pair = exercise.pairs.find((p) => p.ba === left)!;
    if (pair.ru !== ru) {
      setMatchError(true);
      setHadMatchError(true);
      return;
    }
    const next = [...matched, left];
    setMatched(next);
    setLeft("");
    setMatchError(false);
    if (next.length === exercise.pairs.length)
      setAnswer({
        correct: !hadMatchError,
        expected: exercise.pairs.map((p) => `${p.ba} — ${p.ru}`).join(", "),
        prompt: "Соедини пары",
      });
  };
  return (
    <section className="surface task-panel exercise-panel">
      <span className="eyebrow-label">
        {exercise.type === "choice"
          ? "ВЫБЕРИ ПЕРЕВОД"
          : exercise.type === "match"
            ? "СОЕДИНИ ПАРЫ"
            : "СОБЕРИ ПРЕДЛОЖЕНИЕ"}
      </span>
      {exercise.type === "choice" ? (
        <>
          <h2 className="bashkir-text" lang="ba">
            {exercise.prompt}
          </h2>
          <div className="options-list">
            {exercise.options.map((option) => (
              <button
                key={option}
                className={`option ${selection === option ? "selected" : ""}`}
                disabled={!!answer}
                onClick={() => setSelection(option)}
              >
                {option}
              </button>
            ))}
          </div>
        </>
      ) : exercise.type === "order" ? (
        <>
          <h2>{exercise.ruSentence}</h2>
          <div className="word-answer" aria-label="Собранное предложение">
            {words.length ? (
              words.map((word, i) => (
                <button
                  className="word-chip"
                  key={word}
                  disabled={!!answer}
                  onClick={() =>
                    setWords(words.filter((_, index) => index !== i))
                  }
                >
                  {word} ×
                </button>
              ))
            ) : (
              <span>Нажимай слова в нужном порядке</span>
            )}
          </div>
          <div className="word-bank">
            {exercise.words.map((word) => (
              <button
                key={word}
                className="word-chip"
                disabled={words.includes(word) || !!answer}
                onClick={() => setWords([...words, word])}
              >
                {word}
              </button>
            ))}
          </div>
        </>
      ) : (
        <>
          <h2>Найди слова с одним значением</h2>
          <div className="match-columns">
            <div>
              {exercise.pairs.map((pair) => (
                <button
                  key={pair.ba}
                  className={`option ${left === pair.ba ? "selected" : ""} ${matched.includes(pair.ba) ? "matched" : ""}`}
                  disabled={matched.includes(pair.ba) || !!answer}
                  onClick={() => {
                    setLeft(pair.ba);
                    setMatchError(false);
                  }}
                >
                  {pair.ba}
                </button>
              ))}
            </div>
            <div>
              {[...exercise.pairs].reverse().map((pair) => (
                <button
                  key={pair.ru}
                  className={`option ${matched.includes(pair.ba) ? "matched" : ""}`}
                  disabled={matched.includes(pair.ba) || !!answer}
                  onClick={() => match(pair.ru)}
                >
                  {pair.ru}
                </button>
              ))}
            </div>
          </div>
          {matchError && (
            <p role="status" className="error-text">
              Эта пара не совпала. Попробуй ещё раз.
            </p>
          )}
        </>
      )}
      {answer ? (
        <>
          <div
            className={`answer-feedback ${answer.correct ? "correct" : "incorrect"}`}
            role="status"
          >
            <strong>
              {answer.correct
                ? "Отлично получилось!"
                : "Запомним и попробуем ещё!"}
            </strong>
            {!answer.correct && <p>Верный ответ: {answer.expected}</p>}
          </div>
          <button
            className="button button-primary full-width"
            onClick={() => onNext(answer)}
          >
            Продолжить <Icon name="arrow" size={18} />
          </button>
        </>
      ) : (
        exercise.type !== "match" && (
          <button
            className="button button-primary full-width"
            disabled={
              exercise.type === "choice"
                ? !selection
                : words.length !== exercise.words.length
            }
            onClick={check}
          >
            Проверить
          </button>
        )
      )}
      <IssueReportButton exerciseId={exercise.id} />
    </section>
  );
}

export default function Lesson() {
  const { lessonId } = useParams();
  const navigate = useNavigate();
  const [lesson, setLesson] = useState<LessonType | null>(null);
  const [loading, setLoading] = useState(true);
  const [index, setIndex] = useState(0);
  const [answers, setAnswers] = useState<Answer[]>([]);
  const [exit, setExit] = useState(false);
  useEffect(() => {
    getLessons().then((items) => {
      setLesson(items.find((l) => l.id === lessonId) ?? null);
      setLoading(false);
    });
  }, [lessonId]);
  if (loading) return <p className="loading-text">Открываем урок…</p>;
  if (!lesson)
    return (
      <section className="surface intro-card">
        <h1>Урок не найден</h1>
        <Link to="/learn" className="button button-primary">
          К урокам
        </Link>
      </section>
    );
  const next = (answer: Answer) => {
    const nextAnswers = [...answers, answer];
    setAnswers(nextAnswers);
    if (index + 1 < lesson.exercises.length) {
      setIndex(index + 1);
      return;
    }
    const firstCompletion = useProgressStore
      .getState()
      .completeLesson(lesson.id);
    const correct = nextAnswers.filter((a) => a.correct).length;
    const xp = firstCompletion
      ? Math.round((40 * correct) / nextAnswers.length)
      : 0;
    const coins = firstCompletion
      ? Math.round((15 * correct) / nextAnswers.length)
      : 0;
    if (firstCompletion) useUserStore.getState().reward(xp, coins, correct);
    navigate(`/lesson/${lesson.id}/result`, {
      replace: true,
      state: { answers: nextAnswers, xp, coins },
    });
  };
  return (
    <>
      <div className="lesson-top">
        <button
          className="icon-button"
          onClick={() => setExit(true)}
          aria-label="Выйти из урока"
        >
          ×
        </button>
        <ProgressBar value={index} max={lesson.exercises.length} />
        <span>
          {index + 1}/{lesson.exercises.length}
        </span>
      </div>
      <ExerciseView
        key={`${lesson.id}-${index}`}
        exercise={lesson.exercises[index]}
        onNext={next}
      />
      {exit && (
        <Modal title="Выйти из урока?" onClose={() => setExit(false)}>
          <p>Ответы этого урока ещё не сохранены.</p>
          <Link to="/learn" className="button button-soft full-width">
            Выйти
          </Link>
          <button
            className="button button-primary full-width"
            onClick={() => setExit(false)}
          >
            Продолжить урок
          </button>
        </Modal>
      )}
    </>
  );
}
export function LessonResult() {
  const { state } = useLocation() as {
    state: { answers: Answer[]; xp: number; coins: number } | null;
  };
  if (!state)
    return (
      <section className="surface intro-card">
        <h1>Результат появится после урока</h1>
        <Link to="/learn" className="button button-primary">
          К урокам
        </Link>
      </section>
    );
  const accuracy = Math.round(
    (state.answers.filter((a) => a.correct).length / state.answers.length) *
      100,
  );
  return (
    <RewardResult
      title={accuracy >= 50 ? "Урок пройден!" : "Почти получилось!"}
      xp={state.xp}
      coins={state.coins}
    >
      <div
        className="accuracy-ring"
        style={{
          background: `conic-gradient(var(--primary) ${accuracy}%, var(--primary-soft) 0)`,
        }}
      >
        <span>
          {accuracy}%<small>точность</small>
        </span>
      </div>
      {state.answers
        .filter((a) => !a.correct)
        .map((a) => (
          <div className="mistake-note" key={a.prompt}>
            <strong>{a.prompt}</strong>
            <p>{a.expected}</p>
          </div>
        ))}
      <Link to="/learn" className="button button-soft full-width">
        Продолжить обучение
      </Link>
    </RewardResult>
  );
}
