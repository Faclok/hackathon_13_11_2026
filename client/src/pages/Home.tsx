import { Link } from "react-router-dom";
import { useUserStore } from "../store";
import { displayName } from "../utils/displayName";
import {
  BookArt,
  FlameArt,
  Ornament,
} from "../components/Illustrations";
import hatsCase from "../assets/cases/hats-case.png";
import { Icon } from "../components/ui";
import { plural, pluralWord } from "../utils/plural";
const quickActions = [
  {
    to: "/boss",
    icon: "swords",
    title: "Испытание Босса",
    detail: "Открывается за каждые 5 уровней",
    color: "peach",
  },
  {
    to: "/leaderboard",
    icon: "trophy",
    title: "Турнирная таблица",
    detail: "Учимся и растём вместе",
    color: "yellow",
  },
  {
    to: "/inventory",
    icon: "bag",
    title: "Инвентарь",
    detail: "Твои находки и украшения",
    color: "purple",
  },
  {
    to: "/profile",
    icon: "user",
    title: "Мой прогресс",
    detail: "Звания и достижения",
    color: "blue",
  },
];
export default function Home() {
  const user = useUserStore((s) => s.user)!;
  const weekProgress = user.streak === 0 ? 0 : ((user.streak - 1) % 7) + 1;
  return (
    <>
      <h1 className="home-greeting">Привет, {displayName(user)}</h1>
      {(!user.firstName || !user.lastName) && <Link to="/profile" className="profile-prompt">
        <span className="profile-prompt-coin" aria-hidden="true">+50</span>
        <span className="profile-prompt-copy"><strong>Заполни профиль</strong><small>Имя и фамилия · +50 монет</small></span>
        <Icon name="arrow" size={18} />
      </Link>}
      <div className="home-cards">
        <section className="daily-card">
          <Ornament className="daily-ornament" />
          <div className="daily-heading">
            <Icon name="calendar" size={31} />
            <div>
              <h2>Уровни обучения</h2>
              <p>
                Проходи вопросы
                <br />в своём темпе
              </p>
            </div>
          </div>
          <BookArt />
          <Link className="daily-button" to="/learn">
            Начать уровень
            <Icon name="arrow" size={15} />
          </Link>
        </section>
        <section
          className="streak-card surface"
          aria-label={`Серия занятий: ${plural(user.streak, "день", "дня", "дней")} подряд`}
        >
          <div className="streak-description">
            <FlameArt />
            <div>
              <strong>{user.streak}</strong>
              <span>{pluralWord(user.streak, "день", "дня", "дней")} подряд</span>
            </div>
          </div>
          <div className="week-dots" aria-label={`${weekProgress} из 7 дней`}>
            {["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"].map((day, index) => (
              <span
                key={day}
                className={`week-dot ${index < weekProgress ? "complete" : ""}`}
                title={`${day}: ${index < weekProgress ? "выполнено" : "впереди"}`}
              >
                {index < weekProgress && <Icon name="check" size={13} />}
              </span>
            ))}
          </div>
        </section>
        <section className="mini-game-card surface">
          <img className="mini-case-art" src={hatsCase} alt="" />
          <div className="mini-game-copy">
            <h2>Кейсы</h2>
            <p>
              Собирай украшения
              <br />для своего образа
            </p>
          </div>
          <Link to="/cases" className="play-button">
            Открыть
            <Icon name="arrow" size={15} />
          </Link>
          <span className="game-bubble bubble-one" />
          <span className="game-bubble bubble-two" />
        </section>
      </div>
      <section className="quick-section" aria-labelledby="discover-heading">
        <div className="section-caption">
          <h2 id="discover-heading">Больше открытий</h2>
          <span>Каждый день — новое</span>
        </div>
        <div className="quick-grid">
          {quickActions.map((action) => (
            <Link to={action.to} key={action.to} className="quick-card surface">
              <span className={`quick-icon ${action.color}`}>
                <Icon name={action.icon} size={23} />
              </span>
              <h3>{action.title}</h3>
              <p>
                {action.detail}
              </p>
              <span className="quick-arrow">
                <Icon name="arrow" size={17} />
              </span>
            </Link>
          ))}
        </div>
      </section>
      <section className="phrase-card surface">
        <span className="phrase-eyebrow">БАШҠОРТСА · ФРАЗА ДНЯ</span>
        <span className="phrase-flower" aria-hidden="true">
          ✳
        </span>
        <h2 lang="ba">Хәйерле көн!</h2>
        <p>Добрый день!</p>
      </section>
      <p className="home-footer">Маленькие шаги. Большие открытия.</p>
    </>
  );
}
