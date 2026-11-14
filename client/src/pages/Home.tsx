import { Link } from "react-router-dom";
import { useUserStore } from "../store";
import {
  BookArt,
  FlameArt,
  GamepadArt,
  Ornament,
} from "../components/Illustrations";
import { Icon } from "../components/ui";
const quickActions = [
  {
    to: "/boss",
    icon: "swords",
    title: "Испытание Босса",
    detail: "Рейтинговый перевод",
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
    to: "/wheel",
    icon: "wheel",
    title: "Барабан",
    detail: "Испытай удачу",
    color: "blue",
  },
];
export default function Home() {
  const user = useUserStore((s) => s.user)!;
  const weekProgress = user.streak === 0 ? 0 : ((user.streak - 1) % 7) + 1;
  return (
    <>
      <h1 className="home-greeting">Привет, {user.nick}</h1>
      <div className="home-cards">
        <section className="daily-card">
          <Ornament className="daily-ornament" />
          <div className="daily-heading">
            <Icon name="calendar" size={31} />
            <div>
              <h2>Ежедневные задания</h2>
              <p>
                Пройди короткий урок
                <br />и узнай что-то новое
              </p>
            </div>
          </div>
          <BookArt />
          <Link className="daily-button" to="/learn">
            Начать задание
            <Icon name="arrow" size={15} />
          </Link>
        </section>
        <section
          className="streak-card surface"
          aria-label={`Серия занятий: ${user.streak} дней подряд`}
        >
          <div className="streak-description">
            <FlameArt />
            <div>
              <strong>{user.streak}</strong>
              <span>дней подряд</span>
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
          <GamepadArt />
          <div className="mini-game-copy">
            <h2>Мини-игра</h2>
            <p>
              Проверь свои знания
              <br />в увлекательной игре
            </p>
          </div>
          <Link to="/learn" className="play-button">
            Играть
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
              <p>{action.detail}</p>
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
