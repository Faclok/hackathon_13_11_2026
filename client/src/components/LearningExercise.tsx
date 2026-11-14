import { useState, type FormEvent } from "react";
import type { ExerciseAnswer, ExerciseQuestion } from "../api/learning";

interface Props {
  question: ExerciseQuestion;
  busy: boolean;
  onSubmit: (answer: ExerciseAnswer) => void;
  onRefreshAudio: () => void;
}

export default function LearningExercise({ question, busy, onSubmit, onRefreshAudio }: Props) {
  const [text, setText] = useState("");
  const [selectedOption, setSelectedOption] = useState("");
  const [custom, setCustom] = useState(false);
  const [ordered, setOrdered] = useState<string[]>([]);
  const [left, setLeft] = useState("");
  const [pairs, setPairs] = useState<{ leftId: string; rightId: string }[]>([]);
  const [audioError, setAudioError] = useState(false);
  const submitText = (event: FormEvent) => {
    event.preventDefault();
    if (text.trim()) onSubmit({ text: text.trim() });
  };

  if (question.schemaVersion !== 1) return <p role="alert" className="error-text">Эта версия упражнения пока не поддерживается. Обновите страницу или приложение.</p>;

  switch (question.kind) {
    case "translation-text":
      return <>
        <span className="eyebrow-label">ПЕРЕВОД</span>
        <h2 className="bashkir-text" lang={question.prompt.sourceLanguage}>{question.prompt.text}</h2>
        <form onSubmit={submitText}>
          <label className="field-label" htmlFor="learning-answer">Перевод на {question.prompt.targetLanguage === "ru" ? "русский" : question.prompt.targetLanguage}</label>
          <textarea id="learning-answer" value={text} onChange={(e) => setText(e.target.value)} rows={4} maxLength={1000} required disabled={busy} />
          <button className="button button-primary full-width" disabled={busy || !text.trim()}>Проверить ответ</button>
        </form>
      </>;
    case "audio-dictation":
    case "audio-translation":
      return <>
        <span className="eyebrow-label">{question.kind === "audio-dictation" ? "ДИКТАНТ" : "ПЕРЕВОД АУДИО"}</span>
        <h2>{question.kind === "audio-dictation" ? "Запиши услышанное" : "Переведи услышанное"}</h2>
        {question.prompt.audioUrl ? <audio className="learning-audio" controls src={question.prompt.audioUrl} onError={() => setAudioError(true)} aria-label="Аудио задания" /> : <p role="alert">Аудио временно недоступно.</p>}
        {(audioError || !question.prompt.audioUrl) && <button className="button button-soft full-width" onClick={() => { setAudioError(false); onRefreshAudio(); }} disabled={busy}>Обновить аудио</button>}
        <form onSubmit={submitText}>
          <label className="field-label" htmlFor="learning-answer">{question.kind === "audio-dictation" ? "Текст аудио" : "Перевод на русский"}</label>
          <textarea id="learning-answer" value={text} onChange={(e) => setText(e.target.value)} rows={4} maxLength={1000} required disabled={busy} />
          <button className="button button-primary full-width" disabled={busy || !text.trim()}>Проверить ответ</button>
        </form>
      </>;
    case "missing-word":
      return <>
        <span className="eyebrow-label">ПРОПУЩЕННОЕ СЛОВО</span>
        <h2 className="bashkir-text" lang="ba">{question.prompt.before} <span className="learning-gap">…</span> {question.prompt.after}</h2>
        <div className="options-list" role="group" aria-label="Варианты слова">
          {question.prompt.options.map((option) => <button key={option.id} type="button" className={`option ${selectedOption === option.id && !custom ? "selected" : ""}`} aria-pressed={selectedOption === option.id && !custom} disabled={busy} onClick={() => { setSelectedOption(option.id); setCustom(false); }}>{option.text}</button>)}
        </div>
        {question.prompt.allowCustom && <>
          <button type="button" className="button button-soft full-width" aria-pressed={custom} disabled={busy} onClick={() => { setCustom(true); setSelectedOption(""); }}>Ввести своё слово</button>
          {custom && <form onSubmit={submitText}>
            <label className="field-label" htmlFor="learning-answer">Своё слово</label>
            <input id="learning-answer" value={text} onChange={(e) => setText(e.target.value)} maxLength={200} required disabled={busy} />
            <button className="button button-primary full-width" disabled={busy || !text.trim()}>Проверить ответ</button>
          </form>}
        </>}
        {!custom && <button className="button button-primary full-width" disabled={busy || !selectedOption} onClick={() => onSubmit({ optionId: selectedOption })}>Проверить ответ</button>}
      </>;
    case "sentence-order": {
      const remaining = question.prompt.tokens.filter((token) => !ordered.includes(token.id));
      return <>
        <span className="eyebrow-label">ПОРЯДОК СЛОВ</span>
        <h2>Собери предложение</h2>
        <p className="learning-hint">Нажимай слова в нужном порядке. Нажми выбранное слово, чтобы вернуть его.</p>
        <div className="word-answer" role="group" aria-label="Твой ответ">
          {ordered.length ? ordered.map((id) => <button type="button" className="word-chip" key={id} disabled={busy} onClick={() => setOrdered(ordered.filter((value) => value !== id))}>{question.prompt.tokens.find((token) => token.id === id)?.text} ×</button>) : <span>Здесь появится предложение</span>}
        </div>
        <div className="word-bank" role="group" aria-label="Слова">
          {remaining.map((token) => <button type="button" className="word-chip" key={token.id} disabled={busy} onClick={() => setOrdered([...ordered, token.id])}>{token.text}</button>)}
        </div>
        <button className="button button-primary full-width" disabled={busy || remaining.length > 0} onClick={() => onSubmit({ tokenIds: ordered })}>Проверить ответ</button>
      </>;
    }
    case "word-match": {
      const usedLeft = new Set(pairs.map((pair) => pair.leftId));
      const usedRight = new Set(pairs.map((pair) => pair.rightId));
      return <>
        <span className="eyebrow-label">СОПОСТАВЛЕНИЕ</span>
        <h2>Соедини пять пар</h2>
        <p className="learning-hint">Выбери слово слева, затем подходящий перевод справа.</p>
        <div className="match-columns">
          <div role="group" aria-label="Слова">
            {question.prompt.left.map((item) => <button type="button" key={item.id} className={`option ${left === item.id ? "selected" : ""} ${usedLeft.has(item.id) ? "matched" : ""}`} aria-pressed={left === item.id} disabled={busy || usedLeft.has(item.id)} onClick={() => setLeft(item.id)}>{item.text}</button>)}
          </div>
          <div role="group" aria-label="Переводы">
            {question.prompt.right.map((item) => <button type="button" key={item.id} className={`option ${usedRight.has(item.id) ? "matched" : ""}`} disabled={busy || usedRight.has(item.id) || !left} onClick={() => { setPairs([...pairs, { leftId: left, rightId: item.id }]); setLeft(""); }}>{item.text}</button>)}
          </div>
        </div>
        <div className="learning-pairs" aria-label="Составленные пары">
          {pairs.map((pair) => <button key={pair.leftId} type="button" className="word-chip" disabled={busy} onClick={() => setPairs(pairs.filter((value) => value !== pair))}>{question.prompt.left.find((item) => item.id === pair.leftId)?.text} — {question.prompt.right.find((item) => item.id === pair.rightId)?.text} ×</button>)}
        </div>
        <button className="button button-primary full-width" disabled={busy || pairs.length !== 5} onClick={() => onSubmit({ pairs })}>Проверить ответ</button>
      </>;
    }
  }
  return <p role="alert" className="error-text">Этот вид упражнения пока не поддерживается.</p>;
}
