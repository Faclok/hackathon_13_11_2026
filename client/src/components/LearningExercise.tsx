import { useState } from "react";
import type { ExerciseAnswer, Question, Review, ReviewAnswer } from "../api/learning";

export function LearningExercise({ question: q, disabled, onAnswer }: { question: Question; disabled: boolean; onAnswer: (answer: ExerciseAnswer) => void }) {
  const [text, setText] = useState("");
  const [tokens, setTokens] = useState<string[]>([]);
  const [left, setLeft] = useState<string | null>(null);
  const [pairs, setPairs] = useState<{ leftId: string; rightId: string }[]>([]);
  const p = q.prompt;
  const submitText = (event: React.FormEvent) => { event.preventDefault(); if (text.trim()) onAnswer({ text: text.trim() }); };
  if (q.schemaVersion !== 1) return <p role="alert">Этот формат задания пока не поддерживается. Обновите приложение.</p>;
  const textForm = <form onSubmit={submitText} className="learning-answer-form">
    <label className="field-label" htmlFor="learning-answer">Твой ответ</label>
    <textarea id="learning-answer" value={text} onChange={e => setText(e.target.value)} disabled={disabled} required maxLength={4000} rows={4} />
    <button className="button button-primary full-width" disabled={disabled || !text.trim()}>Отправить ответ</button>
  </form>;
  return <div className="learning-exercise">
    {q.kind === "translation-text" && <><h2>Переведи на русский</h2><p className="learning-source" lang="ba">{p.text}</p>{textForm}</>}
    {(q.kind === "audio-dictation" || q.kind === "audio-translation") && <>
      <h2>{q.kind === "audio-dictation" ? "Запиши услышанное" : "Переведи аудио"}</h2>
      {p.audioUrl ? <audio src={p.audioUrl} controls preload="none" /> : <p role="alert">Аудио недоступно. Попробуй открыть задание позже.</p>}
      {p.audioUrl && textForm}
    </>}
    {q.kind === "missing-word" && <>
      <h2>Вставь пропущенное слово</h2><p className="learning-source" lang="ba">{p.before} ___ {p.after}</p>
      <div className="learning-options">{p.options?.map(word => <button className="button button-soft" key={word.id} disabled={disabled} onClick={() => onAnswer({ optionId: word.id })}>{word.text}</button>)}</div>
      {p.allowCustom && textForm}
    </>}
    {q.kind === "sentence-order" && <>
      <h2>Собери предложение</h2><p className="learning-source" aria-live="polite">{tokens.map(id => p.tokens?.find(t => t.id === id)?.text).join(" ") || "Нажимай слова в нужном порядке"}</p>
      <div className="learning-options">{p.tokens?.map(word => <button className="button button-soft" key={word.id} disabled={disabled || tokens.includes(word.id)} onClick={() => setTokens([...tokens, word.id])}>{word.text}</button>)}</div>
      <button className="button button-soft full-width" disabled={disabled || !tokens.length} onClick={() => setTokens(tokens.slice(0, -1))}>Отменить выбор</button>
      <button className="button button-primary full-width" disabled={disabled || !tokens.length || tokens.length !== p.tokens?.length} onClick={() => onAnswer({ tokenIds: tokens })}>Отправить ответ</button>
    </>}
    {q.kind === "word-match" && <>
      <h2>Найди пары слов</h2><p>Выбери слово слева, затем его перевод справа.</p>
      <div className="learning-pairs">{[p.left ?? [], p.right ?? []].map((words, side) => <div key={side}>{words.map(word => <button key={word.id} className={`button ${left === word.id && side === 0 ? "button-primary" : "button-soft"}`} aria-pressed={side === 0 ? left === word.id : undefined} disabled={disabled || pairs.some(pair => (side === 0 ? pair.leftId : pair.rightId) === word.id) || (side === 1 && !left)} onClick={() => {
        if (side === 0) setLeft(word.id);
        else if (left) { setPairs([...pairs, { leftId: left, rightId: word.id }]); setLeft(null); }
      }}>{word.text}</button>)}</div>)}</div>
      <div aria-live="polite">{pairs.map(pair => <p key={pair.leftId}>{p.left?.find(w => w.id === pair.leftId)?.text} — {p.right?.find(w => w.id === pair.rightId)?.text}</p>)}</div>
      <button className="button button-soft full-width" disabled={disabled || (!left && !pairs.length)} onClick={() => { if (left) setLeft(null); else setPairs(pairs.slice(0, -1)); }}>Отменить выбор</button>
      <button className="button button-primary full-width" disabled={disabled || !pairs.length || pairs.length !== p.left?.length} onClick={() => onAnswer({ pairs })}>Отправить ответ</button>
    </>}
  </div>;
}

export function BossReview({ review, disabled, onAnswer }: { review: Review; disabled: boolean; onAnswer: (answer: ReviewAnswer) => void }) {
  const [text, setText] = useState("");
  const [custom, setCustom] = useState(review.kind === "translation-create");
  return <div className="learning-exercise">
    <h2>{review.kind === "translation-create" ? "Предложи перевод" : "Проверь перевод"}</h2>
    <p className="learning-source" lang="ba">{review.original}</p>
    {review.kind === "translation-confirm" && <p className="learning-source">{review.translation}</p>}
    {!custom && review.kind === "translation-choice" && <div className="learning-options">{review.options.map(option => <button className="button button-soft" key={option.id} disabled={disabled} onClick={() => onAnswer({ action: "Confirm", selectedVariantId: option.id })}>{option.text}</button>)}</div>}
    {!custom && review.kind === "translation-confirm" && <button className="button button-primary full-width" disabled={disabled} onClick={() => onAnswer({ action: "Confirm" })}>Подтвердить перевод</button>}
    {custom ? <form onSubmit={event => { event.preventDefault(); if (text.trim()) onAnswer({ action: "Correct", correction: text.trim() }); }} className="learning-answer-form">
      <label className="field-label" htmlFor="boss-answer">Твой перевод</label><textarea id="boss-answer" rows={4} required maxLength={4000} value={text} onChange={e => setText(e.target.value)} disabled={disabled} />
      <button className="button button-primary full-width" disabled={disabled || !text.trim()}>Сохранить перевод</button>
    </form> : review.allowCustom && <button className="button button-soft full-width" disabled={disabled} onClick={() => setCustom(true)}>Свой перевод</button>}
  </div>;
}
