import { useEffect, useState } from "react";
import { getDaily, submitDaily } from "../api";
import { isDailyDone, useProgressStore, useUserStore } from "../store";
import type { DailyFragment } from "../types";
import { BookArt } from "../components/Illustrations";
import {
  Icon,
  IssueReportButton,
  PageTitle,
  RewardResult,
} from "../components/ui";

export default function Daily() {
  const [fragment, setFragment] = useState<DailyFragment | null>(null);
  const [stage, setStage] = useState(isDailyDone() ? "done" : "intro");
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [rewarded, setRewarded] = useState(false);
  useEffect(() => {
    getDaily().then((data) => {
      setFragment(data);
      setText(data.autoTranslation);
    });
  }, []);
  const submit = async (action: "confirm" | "fix") => {
    if (!fragment || busy) return;
    setBusy(true);
    try {
      const answer = await submitDaily({
        fragmentId: fragment.id,
        action,
        ...(action === "fix" ? { fixedText: text.trim() } : {}),
      });
      if (useProgressStore.getState().answerDaily(answer)) {
        useUserStore.getState().reward(20, 10, 1);
        setRewarded(true);
      }
      setStage("accepted");
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <PageTitle subtitle="Немного практики каждый день">
        Ежедневное задание
      </PageTitle>
      {stage === "intro" ? (
        <section className="surface intro-card">
          <BookArt />
          <span className="eyebrow-label">БАШКИРСКИЙ → РУССКИЙ</span>
          <h2>Помоги переводу стать точнее</h2>
          <p>
            Прочитай фрагмент на башкирском. Подтверди перевод или исправь
            ошибку.
          </p>
          <div className="reward-row">
            <span>✦ +20 XP</span>
            <span>🪙 +10</span>
          </div>
          <button
            className="button button-primary full-width"
            disabled={!fragment}
            onClick={() => setStage("task")}
          >
            Начать <Icon name="arrow" size={18} />
          </button>
        </section>
      ) : stage === "task" && fragment ? (
        <section className="surface task-panel">
          <span className="eyebrow-label">Источник: {fragment.source}</span>
          <h2 className="bashkir-text" lang="ba">
            {fragment.textBa}
          </h2>
          <div className="translation-panel">
            <span>Автоперевод</span>
            <p>{fragment.autoTranslation}</p>
          </div>
          {editing ? (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                void submit("fix");
              }}
            >
              <label className="field-label" htmlFor="translation">
                Твой перевод
              </label>
              <textarea
                id="translation"
                value={text}
                onChange={(e) => setText(e.target.value)}
                rows={4}
                maxLength={1000}
                autoFocus
              />
              <button
                className="button button-primary full-width"
                disabled={busy || !text.trim()}
              >
                Отправить
              </button>
              <button
                type="button"
                className="button button-soft full-width"
                onClick={() => setEditing(false)}
              >
                Отмена
              </button>
            </form>
          ) : (
            <div className="stack">
              <button
                className="button button-primary"
                disabled={busy}
                onClick={() => void submit("confirm")}
              >
                Перевод верный <Icon name="check" size={18} />
              </button>
              <button
                className="button button-soft"
                onClick={() => setEditing(true)}
              >
                Исправить перевод
              </button>
            </div>
          )}
          <IssueReportButton exerciseId={fragment.id} />
        </section>
      ) : stage === "accepted" ? (
        <section className="surface intro-card">
          <div className="accepted-icon">
            <Icon name="check" size={32} />
          </div>
          <h2>Спасибо! Ваш ответ учтён</h2>
          <p>Ответы участников помогают согласовать точный перевод.</p>
          <button
            className="button button-primary full-width"
            onClick={() => setStage("done")}
          >
            Дальше <Icon name="arrow" size={18} />
          </button>
        </section>
      ) : (
        <RewardResult
          title="Задание выполнено!"
          xp={rewarded ? 20 : 0}
          coins={rewarded ? 10 : 0}
        >
          {!rewarded && (
            <p>Сегодня ты уже позанимался. Новое задание появится завтра.</p>
          )}
        </RewardResult>
      )}
    </>
  );
}
