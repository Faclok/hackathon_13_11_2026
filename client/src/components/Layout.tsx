import { NavLink, Link, Outlet, useLocation } from "react-router-dom";
import { useEffect } from "react";
import { useUserStore } from "../store";
import { Ornament, SchoolLogo } from "./Illustrations";
import { displayName } from "../utils/displayName";
import { Avatar } from "./Personalization";
import { Icon } from "./ui";
import RewardNotifications from "./RewardNotifications";
const navigation = [
  ["/", "home", "Главная"],
  ["/learn", "book", "Учить"],
  ["/boss", "swords", "Босс"],
  ["/leaderboard", "trophy", "Рейтинг"],
  ["/profile", "user", "Профиль"],
];
const pageTitles: Record<string, string> = {
  "/": "Главная",
  "/learn": "Учить башкирский",
  "/boss": "Испытание Босса",
  "/leaderboard": "Рейтинг",
  "/profile": "Профиль",
  "/inventory": "Инвентарь",
  "/cases": "Кейсы",
};
export default function Layout() {
  const user = useUserStore((s) => s.user);
  const { pathname, hash } = useLocation();
  useEffect(() => {
    let refreshing = false;
    const refresh = async () => {
      if (document.hidden || refreshing || !useUserStore.getState().user) return;
      refreshing = true;
      try { await useUserStore.getState().refreshProfile(); } catch { /* Retry on the next refresh. */ }
      finally { refreshing = false; }
    };
    const timer = window.setInterval(() => { void refresh(); }, 60000);
    window.addEventListener("focus", refresh);
    void refresh();
    return () => { window.clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, [pathname]);
  useEffect(() => {
    document.title = `${pageTitles[pathname] ?? "Страница не найдена"} — Башкирская языковая школа`;
    if (pathname === "/profile" && hash === "#telegram") {
      document.getElementById("telegram")?.scrollIntoView();
    } else {
      window.scrollTo(0, 0);
    }
  }, [pathname, hash]);
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
          <span className="user-name">{user ? displayName(user) : ""}</span>
        </Link>
      </header>
      <main className="page-content">
        <RewardNotifications />
        <Outlet />
      </main>
      <nav className="bottom-nav" aria-label="Основная навигация">
        {navigation.map(([to, icon, label]) => (
          <NavLink
            key={to}
            to={to}
            end={to === "/"}
            className={({ isActive }) =>
              isActive || (to === "/profile" && pathname === "/inventory")
                ? "nav-item active" : "nav-item"
            }
          >
            <Icon
              name={icon}
            />
            <span>{label}</span>
          </NavLink>
        ))}
      </nav>
    </div>
  );
}
