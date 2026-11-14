import { Link } from "react-router-dom";
import type { User } from "../types";
import { BOSS_MIN_RANK, getCurrentRank } from "../utils/ranks";
import { Icon, PageTitle, ProgressBar } from "./ui";

export default function BossLocked({ user }: { user: User }) {
  const [title, tasks, streak] = BOSS_MIN_RANK;
  return (
    <>
      <PageTitle subtitle="Рейтинговый перевод">Испытание Босса</PageTitle>
      <section className="surface boss-locked-card">
        <div className="boss-lock-icon">
          <Icon name="lock" size={34} />
        </div>
        <span className="eyebrow-label">ДЛЯ ОПЫТНЫХ УЧАСТНИКОВ</span>
        <h2>Откроется со звания «{title}»</h2>
        <p>
          Сначала укрепи знания в уроках. Затем попробуй себя в роли эксперта и
          предложи свой перевод.
        </p>
        <span className="boss-current-rank">
          Твоё звание: <strong lang="ba">{getCurrentRank(user)[0]}</strong>
        </span>
        <div className="boss-unlock-progress">
          <div>
            <span>Решённые задания</span>
            <strong>
              {Math.min(user.tasksSolved, tasks)} / {tasks}
            </strong>
          </div>
          <ProgressBar value={Math.min(user.tasksSolved, tasks)} max={tasks} />
          <div>
            <span>Серия занятий</span>
            <strong>
              {Math.min(user.streak, streak)} / {streak} дней
            </strong>
          </div>
          <ProgressBar value={Math.min(user.streak, streak)} max={streak} />
        </div>
        <Link to="/learn" className="button button-primary full-width">
          Продолжить обучение <Icon name="arrow" size={17} />
        </Link>
        <Link to="/profile" className="button button-soft full-width">
          Посмотреть звания
        </Link>
        <p className="boss-eligible-ranks" lang="ba">
          Оста · Батыр · Аҡыл эйәһе
        </p>
      </section>
    </>
  );
}
