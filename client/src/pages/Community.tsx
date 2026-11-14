import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import TelegramLink from "../components/TelegramLink";
import TelegramAccess from "../components/TelegramAccess";
import { isMiniApp } from "../api/telegram";
import { getLeaderboard } from "../api";
import { trophies } from "../mocks/data";
import { decorationCatalog, rarities } from "../mocks/customization";
import { useUserStore } from "../store";
import {
  ranks,
  getCurrentRank,
} from "../utils/ranks";
import type { Decoration } from "../types";
import { Mascot, SchoolLogo } from "../components/Illustrations";
import {
  Avatar,
  DecorationArtwork,
  FloatingItems,
  ProfileBackground,
} from "../components/Personalization";
import { Icon, Modal, PageTitle, ProgressBar, useToast } from "../components/ui";
import { displayName } from "../utils/displayName";
import { plural, pluralWord } from "../utils/plural";

export function Login() {
  const [nick, setNick] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const user = useUserStore((state) => state.user);
  const navigate = useNavigate();
  const location = useLocation();
  const destination = location.state?.from === "/profile#telegram" ? "/profile#telegram" : "/";
  useEffect(() => {
    document.title = "Вход — Башкирская языковая школа";
    if (user) navigate(destination, { replace: true });
  }, [user, navigate, destination]);
  if (isMiniApp() && !user) return <TelegramAccess />;
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
          onSubmit={async (e) => {
            e.preventDefault();
            setBusy(true);
            setError("");
            try {
              await useUserStore.getState().login(nick);
              navigate(destination, { replace: true });
            } catch (cause) {
              setError(cause instanceof Error ? cause.message : "Не удалось войти.");
            } finally {
              setBusy(false);
            }
          }}
        >
          <label htmlFor="nick" className="field-label">
            Твой username
          </label>
          <input
            id="nick"
            autoComplete="nickname"
            value={nick}
            onChange={(e) => setNick(e.target.value)}
            minLength={3}
            maxLength={20}
            required
            placeholder="Придумай username"
            autoFocus
          />
          <button
            className="button button-primary full-width"
            disabled={busy || nick.trim().length < 3}
          >
            Войти <Icon name="arrow" size={18} />
          </button>
        </form>
        {error && <p className="form-error" role="alert">{error}</p>}
        <p className="demo-note">При регистрации — 50 бонусных монет. После входа заполни имя и фамилию в профиле, чтобы тебя узнали в классе и рейтинге.</p>
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
  const [error, setError] = useState("");
  useEffect(() => {
    getLeaderboard(user, period, metric).then((result) => {
      setEntries(result);
      setError("");
    }).catch((cause) => setError(cause instanceof Error ? cause.message : "Не удалось загрузить рейтинг."));
  }, [user, period, metric]);
  const own = entries.find((e) => e.user.id === user.id);
  const ownInPodium = entries.slice(0, 5).some((entry) => entry.user.id === user.id);
  const otherEntries = entries.slice(5).filter((entry) => entry.user.id !== user.id);
  return (
    <>
      <PageTitle subtitle="Вдохновляем друг друга учиться">
        Турнирная таблица
      </PageTitle>
      {error && <p role="alert">{error}</p>}
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
                {displayName(entry.user)}
                {entry.user.id === user.id && (
                  <small className="you-label">Вы</small>
                )}
              </h2>
              <p>Уровень {entry.user.level}</p>
              <strong className="podium-value">{entry.value}</strong>
              <span className="podium-label">
                {metric === "tasks"
                  ? pluralWord(entry.value, "задание", "задания", "заданий")
                  : `${pluralWord(entry.value, "день", "дня", "дней")} подряд`}
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
      {otherEntries.length > 0 && <div className="surface leader-list">
        {otherEntries.map((entry) => (
          <div
            className="leader-row"
            key={entry.user.id}
          >
            <span className="leader-rank">{entry.rank}</span>
            <Avatar user={entry.user} framed={false} />
            <div>
              <strong>
                {displayName(entry.user)}
              </strong>
              <span>
                Уровень {entry.user.level}
                {entry.user.trophyIds.length > 0 && <> · {entry.user.trophyIds
                  .slice(0, 2)
                  .map((id) => trophies.find((t) => t.id === id)?.icon)
                  .join(" ")}</>}
              </span>
            </div>
            <b>{entry.value}</b>
          </div>
        ))}
      </div>}
      {own && !ownInPodium && (
        <div className="your-rank">
          <span>#{own.rank}</span>
          <strong>{displayName(user)} · Вы</strong>
          <b>
            {metric === "tasks"
              ? plural(own.value, "задание", "задания", "заданий")
              : plural(own.value, "день", "дня", "дней")}
          </b>
        </div>
      )}
    </>
  );
}

export function Profile() {
  const user = useUserStore((s) => s.user)!;
  const levelRewards = useUserStore((s) => s.levelRewards);
  const navigate = useNavigate();
  const [firstName, setFirstName] = useState(user.firstName ?? "");
  const [lastName, setLastName] = useState(user.lastName ?? "");
  const [saving, setSaving] = useState(false);
  const [editingProfile, setEditingProfile] = useState(!user.firstName || !user.lastName);
  const [profileMessage, setProfileMessage] = useState("");
  const toast = useToast();
  const currentRank = getCurrentRank(user);
  const [itemInfo, setItemInfo] = useState<Decoration | null>(null);
  const profileComplete = !!user.firstName && !!user.lastName;
  useEffect(() => {
    void useUserStore.getState().refreshProfile().catch(() => {});
  }, []);
  useEffect(() => {
    setFirstName(user.firstName ?? "");
    setLastName(user.lastName ?? "");
  }, [user.firstName, user.lastName]);
  return (
    <>
      <PageTitle>Профиль</PageTitle>
      {editingProfile && <section className="surface profile-completion" id="profile-name-form">
        <div className="profile-completion-heading">
          <div>
            <span className="profile-completion-eyebrow">ТВОЙ ПРОФИЛЬ</span>
            <h2>{profileComplete ? "Изменить имя и фамилию" : "Давай познакомимся"}</h2>
          </div>
          {!profileComplete && <span className="profile-completion-reward">+50 монет</span>}
        </div>
        <p>{profileComplete ? "Исправь данные, которые видны преподавателю и в рейтинге." : "Укажи имя и фамилию. Преподаватель найдёт тебя в классе, а мы подарим 50 монет."}</p>
        <form onSubmit={async (event) => {
          event.preventDefault();
          setSaving(true);
          setProfileMessage("");
          try {
            const bonus = await useUserStore.getState().updateProfile(firstName, lastName);
            setEditingProfile(false);
            toast(bonus > 0 ? "Профиль заполнен · +50 монет" : "Профиль сохранён");
          } catch (error) {
            setProfileMessage(error instanceof Error ? error.message : "Не удалось сохранить профиль.");
          } finally { setSaving(false); }
        }}>
          <label className="field-label" htmlFor="profile-first-name">Имя</label>
          <input id="profile-first-name" value={firstName} maxLength={100} autoComplete="given-name"
            onChange={(event) => setFirstName(event.target.value)} placeholder="Имя" required />
          <label className="field-label" htmlFor="profile-last-name">Фамилия</label>
          <input id="profile-last-name" value={lastName} maxLength={100} autoComplete="family-name"
            onChange={(event) => setLastName(event.target.value)} placeholder="Фамилия" required />
          <button className="button button-primary full-width" disabled={saving || !firstName.trim() || !lastName.trim()}>
            {saving ? "Сохраняем…" : profileComplete ? "Сохранить изменения" : "Сохранить и получить 50 монет"} <Icon name="arrow" size={17} />
          </button>
          {profileComplete && <button type="button" className="button button-soft full-width" onClick={() => { setEditingProfile(false); setFirstName(user.firstName ?? ""); setLastName(user.lastName ?? ""); }} disabled={saving}>Отмена</button>}
        </form>
        {profileMessage && <p className="form-error" role="alert">{profileMessage}</p>}
      </section>}
      <section
        className="surface profile-header personal-card"
        data-background={user.backgroundId}
      >
        <ProfileBackground backgroundId={user.backgroundId} />
        <FloatingItems user={user} onSelect={setItemInfo} />
        <div className="personal-content">
          <Avatar user={user} className="profile-avatar" />
          <h2>{displayName(user)}</h2>
          <span className="rank-badge" lang="ba">
            {currentRank[0]}
          </span>
          <div className="xp-caption">
            <span>Уровень {user.level}</span>
            <span>{user.xp % 100} / 100 XP</span>
          </div>
          <ProgressBar value={user.xp % 100} max={100} />
          {profileComplete && <button type="button" className="customize-link profile-edit-link" aria-expanded={editingProfile} onClick={() => { setEditingProfile(true); requestAnimationFrame(() => document.getElementById("profile-name-form")?.scrollIntoView({ behavior: "smooth", block: "start" })); }}>Изменить имя и фамилию <Icon name="arrow" size={13} /></button>}
          <Link to="/inventory" className="customize-link">
            Изменить оформление <Icon name="arrow" size={13} />
          </Link>
        </div>
      </section>
      <section className="surface profile-section">
        <h2>Скины за уровни</h2>
        <p>За каждый новый уровень — обычный скин. На каждом 5-м уровне — редкий вместо обычного, на каждом 10-м — эпический.</p>
        <p>Легендарные скины за уровни не выдаются. Сначала выдаются скины, которых у тебя ещё нет; если собрана вся редкость, дубликат приносит 5 монет.</p>
        <p>Следующая награда: уровень {user.level + 1} · {rarities[(user.level + 1) % 10 === 0 ? "epic" : (user.level + 1) % 5 === 0 ? "rare" : "common"].label} скин.</p>
        {levelRewards.length > 0 && <div role="status">
          <h3>Получено за уровни</h3>
          {levelRewards.map((reward) => <p key={reward.level}>
            Уровень {reward.level}: {decorationCatalog.find((item) => item.id === reward.decorationId)?.title ?? reward.decorationId}
            {reward.duplicate ? ` · дубликат, +${reward.compensation} монет` : " · добавлен в инвентарь"}
          </p>)}
        </div>}
        <Link to="/inventory" className="customize-link">Открыть инвентарь <Icon name="arrow" size={13} /></Link>
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
          ["🔥", user.streak, `${pluralWord(user.streak, "день", "дня", "дней")} подряд`],
          ["🪙", user.coins, pluralWord(user.coins, "монета", "монеты", "монет")],
          ["✓", user.tasksSolved, pluralWord(user.tasksSolved, "задача решена", "задачи решены", "задач решено")],
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
      <TelegramLink />
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
                  {plural(tasks, "задача", "задачи", "задач")}{streak > 0 && ` · серия ${plural(streak, "день", "дня", "дней")}`}
                </small>
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
        to="/boss"
        className="surface profile-boss-link"
      >
        <Icon name="swords" />
        <span>
          <strong>Босс-испытание</strong>
          <small>
            За каждые пять пройденных уровней
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
