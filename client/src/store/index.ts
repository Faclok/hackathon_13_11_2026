import { create } from "zustand";
import { persist } from "zustand/middleware";
import { decorations, demoUser } from "../mocks/data";
import type { BossAnswer, DailyAnswer, IssueReport, User } from "../types";
import { schoolDay as today, dayNumber } from "../utils/calendar";
interface UserState {
  user: User | null;
  lastActiveDay: string | null;
  reports: IssueReport[];
  login: (nick: string) => void;
  logout: () => void;
  reward: (xp: number, coins: number, tasks: number, trophy?: string) => void;
  buyDecoration: (id: string) => boolean;
  equipDecoration: (id: string) => void;
  reportIssue: (exerciseId: string, text: string) => void;
}
export const useUserStore = create<UserState>()(
  persist(
    (set, get) => ({
      user: demoUser,
      lastActiveDay: null,
      reports: [],
      login: (nick) => set({ user: { ...demoUser, nick: nick.trim() } }),
      logout: () => set({ user: null }),
      reward: (xp, coins, tasks, trophy) =>
        set((state) => {
          if (!state.user) return state;
          const user = state.user;
          const elapsed = state.lastActiveDay
            ? dayNumber(today()) - dayNumber(state.lastActiveDay)
            : 1;
          const streak =
            elapsed === 0 ? user.streak : elapsed === 1 ? user.streak + 1 : 1;
          const trophyIds = new Set(user.trophyIds);
          if (trophy) trophyIds.add(trophy);
          if (streak >= 7) trophyIds.add("week");
          if (streak >= 30) trophyIds.add("month");
          if (user.tasksSolved + tasks >= 100) trophyIds.add("epic");
          return {
            lastActiveDay: today(),
            user: {
              ...user,
              xp: user.xp + xp,
              level: Math.floor((user.xp + xp) / 100) + 1,
              coins: user.coins + coins,
              tasksSolved: user.tasksSolved + tasks,
              streak,
              trophyIds: [...trophyIds],
            },
          };
        }),
      buyDecoration: (id) => {
        const { user } = get();
        const item = decorations.find((d) => d.id === id);
        if (
          !user ||
          !item ||
          item.source !== "shop" ||
          user.coins < item.price ||
          user.ownedDecorationIds.includes(id)
        )
          return false;
        set({
          user: {
            ...user,
            coins: user.coins - item.price,
            ownedDecorationIds: [...user.ownedDecorationIds, id],
            ...(item.type === "background"
              ? { backgroundId: id }
              : item.type === "frame"
                ? { frameId: id }
                : {}),
          },
        });
        return true;
      },
      equipDecoration: (id) =>
        set(({ user }) => {
          const item = decorations.find((d) => d.id === id);
          if (!user || !item || !user.ownedDecorationIds.includes(id))
            return {};
          return {
            user: {
              ...user,
              ...(item.type === "background"
                ? { backgroundId: id }
                : item.type === "frame"
                  ? { frameId: id }
                  : {}),
            },
          };
        }),
      reportIssue: (exerciseId, text) =>
        set((state) => ({
          reports: [
            ...state.reports,
            { exerciseId, text, createdAt: Date.now() },
          ],
        })),
    }),
    {
      name: "bashkir-school-user-v1",
      version: 1,
      migrate: (stored) => {
        const previous = stored as UserState;
        if (!previous.user) return previous;
        const user = previous.user;
        const xp = Math.max(user.xp, demoUser.xp);
        return {
          ...previous,
          user: {
            ...demoUser,
            ...user,
            nick: "Денис",
            avatarUrl: demoUser.avatarUrl,
            tasksSolved: Math.max(user.tasksSolved, demoUser.tasksSolved),
            streak: Math.max(user.streak, demoUser.streak),
            xp,
            level: Math.floor(xp / 100) + 1,
            coins: user.coins + (demoUser.coins - 180),
            accuracy: Math.max(user.accuracy, demoUser.accuracy),
            trophyIds: [...new Set([...demoUser.trophyIds, ...user.trophyIds])],
            ownedDecorationIds: [
              ...new Set([
                ...demoUser.ownedDecorationIds,
                ...user.ownedDecorationIds,
              ]),
            ],
            backgroundId: user.backgroundId ?? demoUser.backgroundId,
            frameId: user.frameId ?? demoUser.frameId,
          },
        };
      },
    },
  ),
);
interface ProgressState {
  completedLessons: string[];
  dailyAnswers: (DailyAnswer & { date: string })[];
  bossAnswers: BossAnswer[];
  completeLesson: (id: string) => boolean;
  answerDaily: (answer: DailyAnswer) => boolean;
  saveBoss: (answers: BossAnswer[]) => void;
  reset: () => void;
}
export const useProgressStore = create<ProgressState>()(
  persist(
    (set, get) => ({
      completedLessons: [],
      dailyAnswers: [],
      bossAnswers: [],
      completeLesson: (id) => {
        if (get().completedLessons.includes(id)) return false;
        set((s) => ({ completedLessons: [...s.completedLessons, id] }));
        return true;
      },
      answerDaily: (answer) => {
        if (get().dailyAnswers.some((a) => a.date === today())) return false;
        set((s) => ({
          dailyAnswers: [...s.dailyAnswers, { ...answer, date: today() }],
        }));
        return true;
      },
      saveBoss: (bossAnswers) => set({ bossAnswers }),
      reset: () =>
        set({ completedLessons: [], dailyAnswers: [], bossAnswers: [] }),
    }),
    { name: "bashkir-school-progress-v1" },
  ),
);
export const isDailyDone = () =>
  useProgressStore.getState().dailyAnswers.some((a) => a.date === today());
