import { NavLink, Link, Outlet, useLocation } from "react-router-dom";
import { useEffect } from "react";
import { useUserStore } from "../store";
import { canAccessBoss, BOSS_MIN_RANK } from "../utils/ranks";
import { Ornament, SchoolLogo } from "./Illustrations";
import { Avatar } from "./Personalization";
import { Icon } from "./ui";
const navigation = [
  ["/", "home", "Главная"],
  ["/learn", "book", "Учить"],
  ["/boss", "swords", "Босс"],
  ["/leaderboard", "trophy", "Рейтинг"],
  ["/profile", "user", "Профиль"],
];
export default function Layout() {
  const user = useUserStore((s) => s.user);
  const { pathname } = useLocation();
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);
  return (
    <div className="school-app">
      <div className="background-pattern">
        <Ornament />
        <Ornament />
        <Ornament />
      </div>
      <header className="top-bar">
        <Link
          to="/"
          className="brand-link"
          aria-label="Башкирская языковая школа — главная"
        >
          <SchoolLogo />
        </Link>
        <Link to="/profile" className="user-link" aria-label="Открыть профиль">
          {user && <Avatar user={user} framed={false} />}
          <span className="user-name">{user?.nick}</span>
        </Link>
      </header>
      <main className="page-content">
        <Outlet />
      </main>
      <nav className="bottom-nav" aria-label="Основная навигация">
        {navigation.map(([to, icon, label]) => (
          <NavLink
            key={to}
            to={to}
            end={to === "/"}
            title={
              to === "/boss" && !canAccessBoss(user)
                ? `Доступ со звания «${BOSS_MIN_RANK[0]}»`
                : undefined
            }
            className={({ isActive }) =>
              isActive ? "nav-item active" : "nav-item"
            }
          >
            <Icon
              name={to === "/boss" && !canAccessBoss(user) ? "lock" : icon}
            />
            <span>{label}</span>
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
