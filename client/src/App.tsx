import {
  BrowserRouter,
  Navigate,
  Outlet,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import { useEffect } from "react";
import { useUserStore } from "./store";
import Layout from "./components/Layout";
import { ToastProvider } from "./components/ui";
import Home from "./pages/Home";
import Learn from "./pages/Learn";
import Boss from "./pages/Boss";
import Inventory from "./pages/Inventory";
import Cases from "./pages/Cases";
import { Leaderboard, Login, NotFound, Profile } from "./pages/Community";
import "./index.css";
import { isMiniApp } from "./api/telegram";
import TelegramAccess from "./components/TelegramAccess";

function AuthGuard() {
  const location = useLocation();
  const ready = useUserStore((s) => s.ready);
  const user = useUserStore((s) => s.user);
  if (!ready) return <div className="school-app">Загружаем профиль…</div>;
  if (!user && isMiniApp()) return <TelegramAccess />;
  return user ? (
    <Outlet />
  ) : (
    <Navigate to="/login" state={{ from: location.pathname + location.hash }} replace />
  );
}
export default function App() {
  useEffect(() => {
    void useUserStore.getState().bootstrap();
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute("content", "#f5f8f5");
    document
      .querySelector('meta[name="description"]')
      ?.setAttribute(
        "content",
        "Изучай башкирский язык: ежедневные задания, мини-игры и маленькие открытия каждый день.",
      );
  }, []);
  return (
    <BrowserRouter>
      <ToastProvider>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/telegram.html" element={<Navigate to="/profile#telegram" replace />} />
          <Route element={<AuthGuard />}>
            <Route element={<Layout />}>
              <Route index element={<Home />} />
              <Route path="daily" element={<Navigate to="/learn" replace />} />
              <Route path="learn" element={<Learn />} />
              <Route path="lesson/:lessonId" element={<Navigate to="/learn" replace />} />
              <Route
                path="lesson/:lessonId/result"
                element={<Navigate to="/learn" replace />}
              />
              <Route path="boss" element={<Boss />} />
              <Route path="leaderboard" element={<Leaderboard />} />
              <Route path="profile" element={<Profile />} />
              <Route path="inventory" element={<Inventory />} />
              <Route
                path="shop"
                element={<Navigate to="/inventory" replace />}
              />
              <Route path="cases" element={<Cases />} />
              <Route path="wheel" element={<Navigate to="/cases" replace />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Route>
        </Routes>
      </ToastProvider>
    </BrowserRouter>
  );
}
