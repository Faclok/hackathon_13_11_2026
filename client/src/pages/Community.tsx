import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getLeaderboard } from "../api";
import { trophies } from "../mocks/data";
import { useProgressStore, useUserStore } from "../store";
import {
  ranks,
  BOSS_MIN_RANK,
  getCurrentRank,
  canAccessBoss,
} from "../utils/ranks";
import type { Decoration } from "../types";
import { Mascot, SchoolLogo } from "../components/Illustrations";
import {
  Avatar,
  DecorationArtwork,
  FloatingItems,
  ProfileBackground,
} from "../components/Personalization";
import { Icon, Modal, PageTitle, ProgressBar } from "../components/ui";

export function Login() {
  const [nick, setNick] = useState("");
  const navigate = useNavigate();
  return (
    <div className="school-app login-app">
      <Link to="/" className="login-logo">
        <SchoolLogo />
      </Link>
      <section className="surface intro-card login-card">
        <Mascot className="login-mascot" />
        <h1>Һаумы! Привет!</h1>
        <p>Давай знакомиться и открывать башкирский язык вместе.</p>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            useProgressStore.getState().reset();
            useUserStore.setState({ lastActiveDay: null, reports: [] });
            useUserStore.getState().login(nick);
            navigate("/", { replace: true });
          }}
        >
          <label htmlFor="nick" className="field-label">
            Как тебя зовут?
          </label>
          <input
            id="nick"
            autoComplete="nickname"
            value={nick}
            onChange={(e) => setNick(e.target.value)}
            minLength={3}
            maxLength={20}
            required
            placeholder="Твой ник"
            autoFocus
          />
          <button
            className="button button-primary full-width"
            disabled={nick.trim().length < 3}
          >
            Войти <Icon name="arrow" size={18} />
          </button>
        </form>
        <p className="demo-note">Демо-режим. Данные хранятся локально.</p>
      </section>
    </div>
  );
}

export function Leaderboard() {
  const user = useUserStore((s) => s.user)!;
  const [period, setPeriod] = useState<"global" | "monthly">("global");
  const [metric, setMetric] = useState<"tasks" | "streak">("tasks");
  const [entries, setEntries] = useState<
    Awaited<ReturnType<typeof getLeaderboard>>
  >([]);
  useEffect(() => {
    getLeaderboard(user, period, metric).then(setEntries);
  }, [user, period, metric]);
  const own = entries.find((e) => e.user.id === user.id);
  return (
    <>
      <PageTitle subtitle="Вдохновляем друг друга учиться">
        Турнирная таблица
      </PageTitle>
      <div className="tabs" aria-label="Период">
        {(["global", "monthly"] as const).map((value) => (
          <button
            key={value}
            aria-pressed={period === value}
            className={period === value ? "active" : ""}
            onClick={() => setPeriod(value)}
          >
            {value === "global" ? "Глобальная" : "Месячная"}
          </button>
        ))}
      </div>
      <div className="tabs metric-tabs" aria-label="Показатель">
        {(["tasks", "streak"] as const).map((value) => (
          <button
            key={value}
            aria-pressed={metric === value}
            className={metric === value ? "active" : ""}
            onClick={() => setMetric(value)}
          >
            {value === "tasks" ? "Задачи" : "Серия"}
          </button>
        ))}
      </div>
      <div className="podium-scroll">
        {entries.slice(0, 5).map((entry) => (
          <article
            key={entry.user.id}
            className={`podium-card surface personal-card ${entry.rank === 1 ? "first-place" : ""}`}
            data-user-id={entry.user.id}
            data-background={entry.user.backgroundId}
          >
            <ProfileBackground backgroundId={entry.user.backgroundId} />
            <FloatingItems user={entry.user} />
            <div className="personal-content">
              <span className="place-medal">
                {["🥇", "🥈", "🥉"][entry.rank - 1] ?? `#${entry.rank}`}
              </span>
              <Avatar user={entry.user} className="podium-avatar" />
              <h2>
                {entry.user.nick}
                {entry.user.id === user.id && (
                  <small className="you-label">Вы</small>
                )}
              </h2>
              <p>Уровень {entry.user.level}</p>
              <strong className="podium-value">{entry.value}</strong>
              <span className="podium-label">
                {metric === "tasks" ? "заданий" : "дней подряд"}
              </span>
              <div className="trophy-strip">
                {entry.user.trophyIds.slice(0, 5).map((id) => (
                  <span
                    key={id}
                    title={trophies.find((t) => t.id === id)?.title}
                  >
                    {trophies.find((t) => t.id === id)?.icon}
                  </span>
                ))}
              </div>
            </div>
          </article>
        ))}
      </div>
      <div className="surface leader-list">
        {entries.slice(5).map((entry) => (
          <div
            className={`leader-row ${entry.user.id === user.id ? "is-you" : ""}`}
            key={entry.user.id}
          >
            <span className="leader-rank">{entry.rank}</span>
            <Avatar user={entry.user} framed={false} />
            <div>
              <strong>
                {entry.user.nick}
                {entry.user.id === user.id ? " · Вы" : ""}
              </strong>
              <span>
                Уровень {entry.user.level} ·{" "}
                {entry.user.trophyIds
                  .slice(0, 2)
                  .map((id) => trophies.find((t) => t.id === id)?.icon)
                  .join(" ")}
              </span>
            </div>
            <b>{entry.value}</b>
          </div>
        ))}
      </div>
      {own && (
        <div className="your-rank">
          <span>#{own.rank}</span>
          <strong>{user.nick} · Вы</strong>
          <b>
            {own.value} {metric === "tasks" ? "заданий" : "дней"}
          </b>
        </div>
      )}
    </>
  );
}

export function Profile() {
  const user = useUserStore((s) => s.user)!;
  const navigate = useNavigate();
  const currentRank = getCurrentRank(user);
  const [itemInfo, setItemInfo] = useState<Decoration | null>(null);
  return (
    <>
      <PageTitle>Профиль</PageTitle>
      <section
        className="surface profile-header personal-card"
        data-background={user.backgroundId}
      >
        <ProfileBackground backgroundId={user.backgroundId} />
        <FloatingItems user={user} onSelect={setItemInfo} />
        <div className="personal-content">
          <Avatar user={user} className="profile-avatar" />
          <h2>{user.nick}</h2>
          <span className="rank-badge" lang="ba">
            {currentRank[0]}
          </span>
          <div className="xp-caption">
            <span>Уровень {user.level}</span>
            <span>{user.xp % 100} / 100 XP</span>
          </div>
          <ProgressBar value={user.xp % 100} max={100} />
          <Link to="/inventory" className="customize-link">
            Изменить оформление <Icon name="arrow" size={13} />
          </Link>
        </div>
      </section>
      {itemInfo && (
        <Modal title={itemInfo.title} onClose={() => setItemInfo(null)}>
          <div className="item-detail-icon">
            <DecorationArtwork item={itemInfo} />
          </div>
          <p>{itemInfo.description}</p>
          <button
            className="button button-soft full-width"
            onClick={() => setItemInfo(null)}
          >
            Понятно
          </button>
        </Modal>
      )}
      <div className="stats-grid">
        {[
          ["🔥", user.streak, "дней подряд"],
          ["🪙", user.coins, "монет"],
          ["✓", user.tasksSolved, "решено задач"],
          ["◎", `${user.accuracy}%`, "точность"],
        ].map(([icon, value, label]) => (
          <div className="surface stat" key={label}>
            <span>{icon}</span>
            <strong>{value}</strong>
            <small>{label}</small>
          </div>
        ))}
      </div>
      <div className="surface language-card">
        <Icon name="book" />
        <div>
          <strong>Башкирский ↔ Русский</strong>
          <p>Твоя языковая пара</p>
        </div>
      </div>
      <section className="surface profile-section">
        <h2>Твои звания</h2>
        <div className="rank-list">
          {ranks.map(([name, tasks, streak]) => (
            <div
              key={name}
              className={
                currentRank[0] === name
                  ? "current-rank"
                  : user.tasksSolved >= tasks && user.streak >= streak
                    ? "earned-rank"
                    : "future-rank"
              }
            >
              <span>
                {currentRank[0] === name
                  ? "✦"
                  : user.tasksSolved >= tasks && user.streak >= streak
                    ? "✓"
                    : "○"}
              </span>
              <div>
                <strong lang="ba">{name}</strong>
                <small>
                  {tasks} задач{streak > 0 && ` · серия ${streak} дней`}
                </small>
                {name === BOSS_MIN_RANK[0] && (
                  <small className="rank-unlocks-boss">
                    Открывает Босс-испытание
                  </small>
                )}
                {user.tasksSolved < tasks && (
                  <ProgressBar
                    value={Math.min(user.tasksSolved, tasks)}
                    max={tasks}
                  />
                )}
              </div>
            </div>
          ))}
        </div>
      </section>
      <Link
        to={canAccessBoss(user) ? "/boss" : "/learn"}
        className="surface profile-boss-link"
      >
        <Icon name={canAccessBoss(user) ? "swords" : "lock"} />
        <span>
          <strong>Босс-испытание</strong>
          <small>
            {canAccessBoss(user)
              ? "Твоё звание открывает рейтинговые переводы"
              : `Доступ со звания «${BOSS_MIN_RANK[0]}»`}
          </small>
        </span>
        <Icon name="arrow" size={17} />
      </Link>
      <section className="surface profile-section">
        <h2>Витрина трофеев</h2>
        <div className="trophy-grid">
          {trophies.map((trophy) => (
            <div
              key={trophy.id}
              className={
                user.trophyIds.includes(trophy.id) ? "trophy earned" : "trophy"
              }
              title={trophy.description}
            >
              <span>{trophy.icon}</span>
              <strong>{trophy.title}</strong>
              <small>{trophy.description}</small>
            </div>
          ))}
        </div>
      </section>
      <Link className="button button-soft full-width" to="/inventory">
        Открыть инвентарь <Icon name="bag" size={18} />
      </Link>
      <button
        className="logout-button"
        onClick={() => {
          useUserStore.getState().logout();
          navigate("/login", { replace: true });
        }}
      >
        Выйти из аккаунта
      </button>
    </>
  );
}

export function NotFound() {
  return (
    <section className="surface intro-card">
      <span className="not-found-number">404</span>
      <h1>Страница не найдена</h1>
      <p>Кажется, мы немного сбились с пути.</p>
      <Link to="/" className="button button-primary full-width">
        На главную <Icon name="arrow" size={18} />
      </Link>
    </section>
  );
}
