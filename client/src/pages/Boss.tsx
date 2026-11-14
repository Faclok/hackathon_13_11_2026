import { useEffect, useState } from "react";
import BossLocked from "../components/BossLocked";
import { BOSS_MIN_RANK, canAccessBoss, getCurrentRank } from "../utils/ranks";
import { getBossPhrases } from "../api";
import { useProgressStore, useUserStore } from "../store";
import type { BossAnswer, BossPhrase, User } from "../types";
import {
  Icon,
  IssueReportButton,
  PageTitle,
  ProgressBar,
  RewardResult,
} from "../components/ui";

export default function Boss() {
  const user = useUserStore((state) => state.user);
  if (!user) return null;
  return canAccessBoss(user) ? (
    <BossChallenge user={user} />
  ) : (
    <BossLocked user={user} />
  );
}

function BossChallenge({ user }: { user: User }) {
  const [phrases, setPhrases] = useState<BossPhrase[]>([]);
  const [stage, setStage] = useState("intro");
  const [answers, setAnswers] = useState<BossAnswer[]>([]);
  const [custom, setCustom] = useState(false);
  const [text, setText] = useState("");
  const [accepted, setAccepted] = useState(false);
  const [reward, setReward] = useState({ xp: 0, coins: 0 });
  useEffect(() => {
    getBossPhrases(user).then(setPhrases);
  }, []);
  const index = answers.length - (accepted ? 1 : 0);
  const phrase = phrases[index];
  const submit = (chosenOptionId?: string) => {
    if (accepted || !canAccessBoss(useUserStore.getState().user)) return;
    setAnswers([
      ...answers,
      {
        phraseId: phrase.id,
        chosenOptionId,
        ...(chosenOptionId ? {} : { customText: text.trim() }),
        isCustom: !chosenOptionId,
      },
    ]);
    setAccepted(true);
  };
  const next = () => {
    if (!canAccessBoss(useUserStore.getState().user)) return;
    if (answers.length === phrases.length) {
      const first = !useUserStore.getState().user?.trophyIds.includes("boss");
      useProgressStore.getState().saveBoss(answers);
      if (first) {
        useUserStore.getState().reward(100, 50, phrases.length, "boss");
        setReward({ xp: 100, coins: 50 });
      }
      setStage("done");
    }
    setAccepted(false);
    setCustom(false);
    setText("");
  };
  const popularMatches = answers.filter(
    (a) =>
      !a.isCustom &&
      a.chosenOptionId ===
        phrases
          .find((p) => p.id === a.phraseId)
          ?.options.reduce((best, option) =>
            option.votes > best.votes ? option : best,
          ).id,
  ).length;
  return (
    <>
      <PageTitle subtitle="Рейтинговый перевод">Испытание Босса</PageTitle>
      {stage === "intro" ? (
        <section className="surface intro-card">
          <div className="boss-emblem">👑</div>
          <span className="eyebrow-label">БӘЙГЕ · ХАН ПЕРЕВОДА</span>
          <span className="boss-access-badge">
            {getCurrentRank(user)[0]} · доступ открыт
          </span>
          <h2>Почувствуй язык вместе с другими</h2>
          <p>
            {phrases.length || 3} фразы на башкирском. Выбери перевод, который
            кажется самым популярным, или предложи свой.
          </p>
          <p className="soft-note">
            Для званий «{BOSS_MIN_RANK[0]}», «Батыр» и «Аҡыл эйәһе». Здесь нет
            правильного ответа — есть мнение сообщества.
          </p>
          <div className="reward-row">
            <span>✦ +100 XP</span>
            <span>🪙 +50</span>
          </div>
          <button
            className="button button-primary full-width"
            disabled={!phrases.length}
            onClick={() => setStage("round")}
          >
            Начать <Icon name="arrow" size={18} />
          </button>
        </section>
      ) : stage === "done" ? (
        <RewardResult
          title="Испытание пройдено!"
          xp={reward.xp}
          coins={reward.coins}
        >
          <div className="boss-emblem small">👑</div>
          <h3>Хан перевода</h3>
          <p>
            С популярным вариантом совпало: {popularMatches} из {phrases.length}
          </p>
          <button
            className="button button-soft full-width"
            onClick={() => {
              setAnswers([]);
              setReward({ xp: 0, coins: 0 });
              setStage("intro");
            }}
          >
            Ещё раз
          </button>
        </RewardResult>
      ) : (
        phrase && (
          <>
            <div className="round-progress">
              <span>
                Фраза {index + 1} из {phrases.length}
              </span>
              <ProgressBar value={index} max={phrases.length} />
            </div>
            <section className="surface task-panel">
              {phrase.source && (
                <span className="boss-source">
                  Источник: {phrase.source.collection} ·{" "}
                  {phrase.source.recordId}
                </span>
              )}
              <h2 className="bashkir-text" lang="ba">
                {phrase.bashkir}
              </h2>
              <div className="options-list">
                {phrase.options.map((option) => (
                  <button
                    className={`option ${accepted && answers.at(-1)?.chosenOptionId === option.id ? "selected" : ""}`}
                    key={option.id}
                    disabled={accepted}
                    onClick={() => submit(option.id)}
                  >
                    <span>{option.ru}</span>
                    {accepted && (
                      <span className="vote-count">{option.votes}%</span>
                    )}
                  </button>
                ))}
              </div>
              {!accepted &&
                (custom ? (
                  <form
                    onSubmit={(e) => {
                      e.preventDefault();
                      submit();
                    }}
                  >
                    <label htmlFor="custom-answer" className="field-label">
                      Свой перевод
                    </label>
                    <textarea
                      id="custom-answer"
                      value={text}
                      onChange={(e) => setText(e.target.value)}
                      rows={3}
                      required
                      maxLength={1000}
                      autoFocus
                    />
                    <button
                      className="button button-primary full-width"
                      disabled={!text.trim()}
                    >
                      Отправить
                    </button>
                  </form>
                ) : (
                  <button
                    className="button button-soft full-width"
                    onClick={() => setCustom(true)}
                  >
                    Предложить свой перевод
                  </button>
                ))}
              {accepted && (
                <>
                  <p className="neutral-feedback" role="status">
                    {answers.at(-1)?.isCustom
                      ? "Ваш перевод добавлен в голосование ✓"
                      : "Ответ принят ✓"}
                  </p>
                  <button
                    className="button button-primary full-width"
                    onClick={next}
                  >
                    Дальше <Icon name="arrow" size={18} />
                  </button>
                </>
              )}
              <IssueReportButton exerciseId={phrase.id} />
            </section>
          </>
        )
      )}
    </>
  );
}
