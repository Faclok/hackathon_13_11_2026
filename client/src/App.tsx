import {
  BrowserRouter,
  Navigate,
  Outlet,
  Route,
  Routes,
} from "react-router-dom";
import { useEffect } from "react";
import { useUserStore } from "./store";
import Layout from "./components/Layout";
import { ToastProvider } from "./components/ui";
import Home from "./pages/Home";
import Learn from "./pages/Learn";
import Lesson, { LessonResult } from "./pages/Lesson";
import Boss from "./pages/Boss";
import {
  Leaderboard,
  Login,
  NotFound,
  Profile,
  Shop,
  Wheel,
} from "./pages/Community";
import "./index.css";

function AuthGuard() {
  return useUserStore((s) => s.user) ? (
    <Outlet />
  ) : (
    <Navigate to="/login" replace />
  );
}
export default function App() {
  useEffect(() => {
    document.title = "Башкирская языковая школа";
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
          <Route element={<AuthGuard />}>
            <Route element={<Layout />}>
              <Route index element={<Home />} />
              <Route path="daily" element={<Navigate to="/learn" replace />} />
              <Route path="learn" element={<Learn />} />
              <Route path="lesson/:lessonId" element={<Lesson />} />
              <Route
                path="lesson/:lessonId/result"
                element={<LessonResult />}
              />
              <Route path="boss" element={<Boss />} />
              <Route path="leaderboard" element={<Leaderboard />} />
              <Route path="profile" element={<Profile />} />
              <Route path="shop" element={<Shop />} />
              <Route path="wheel" element={<Wheel />} />
              <Route path="*" element={<NotFound />} />
            </Route>
          </Route>
        </Routes>
      </ToastProvider>
    </BrowserRouter>
  );
}
