import { create } from "zustand";
import { persist } from "zustand/middleware";
import { apiRequest, ApiError, clearSession, hasSession, loginUsername } from "../api/session";
import type { BossAnswer, CaseOpening, DailyAnswer, IssueReport, User } from "../types";
import { schoolDay as today, dayNumber } from "../utils/calendar";

interface UserState {
  user: User | null;
  ready: boolean;
  lastActiveDay: string | null;
  reports: IssueReport[];
  caseOpening: CaseOpening | null;
  caseHistory: CaseOpening[];
  bootstrap: () => Promise<void>;
  login: (username: string) => Promise<void>;
  logout: () => void;
  refreshProfile: () => Promise<void>;
  updateProfile: (firstName: string, lastName: string) => Promise<number>;
  reward: (xp: number, coins: number, tasks: number, trophy?: string) => void;
  buyCaseKeys: (quantity: number) => Promise<boolean>;
  openCase: (caseId: string) => Promise<CaseOpening | null>;
  dismissCaseOpening: () => void;
  equipDecoration: (id: string) => Promise<void>;
  unequipDecoration: (id: string) => Promise<void>;
  reportIssue: (exerciseId: string, text: string) => void;
}

let openingPending = false;
let bootstrapping: Promise<void> | null = null;
export const useUserStore = create<UserState>()((set, get) => ({
  user: null,
  ready: false,
  lastActiveDay: null,
  reports: [],
  caseOpening: null,
  caseHistory: [],
  bootstrap: async () => {
    if (get().ready) return;
    bootstrapping ??= (async () => {
      if (!hasSession()) {
        set({ user: null, ready: true });
        return;
      }
      try {
        const [user, caseHistory] = await Promise.all([
          apiRequest<User>("/api/player/profile"),
          apiRequest<CaseOpening[]>("/api/player/cases/history"),
        ]);
        set({ user, caseHistory, ready: true });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) clearSession();
        set({ user: null, ready: true });
      }
    })();
    try { await bootstrapping; } finally { bootstrapping = null; }
  },
  login: async (username) => {
    await loginUsername(username);
    try {
      const [user, caseHistory] = await Promise.all([
        apiRequest<User>("/api/player/profile"),
        apiRequest<CaseOpening[]>("/api/player/cases/history"),
      ]);
      set({ user, caseHistory, caseOpening: null, ready: true });
    } catch (error) {
      clearSession();
      throw error;
    }
  },
  logout: () => {
    clearSession();
    set({ user: null, caseOpening: null, caseHistory: [], reports: [] });
  },
  refreshProfile: async () => {
    const user = await apiRequest<User>("/api/player/profile");
    set({ user });
  },
  updateProfile: async (firstName, lastName) => {
    const saved = await apiRequest<{
      firstName: string | null;
      lastName: string | null;
      bonusCoinsAwarded: number;
    }>("/api/me", { method: "PUT", body: JSON.stringify({ firstName, lastName }) });
    set((state) => ({ user: state.user ? {
      ...state.user,
      firstName: saved.firstName,
      lastName: saved.lastName,
      coins: state.user.coins + saved.bonusCoinsAwarded,
    } : null }));
    void get().refreshProfile().catch(() => {});
    return saved.bonusCoinsAwarded;
  },
  // Boss and Learn keep their existing local reward behavior.
  reward: (xp, coins, tasks, trophy) => set((state) => {
    if (!state.user) return state;
    const user = state.user;
    const elapsed = state.lastActiveDay ? dayNumber(today()) - dayNumber(state.lastActiveDay) : 1;
    const streak = elapsed === 0 ? user.streak : elapsed === 1 ? user.streak + 1 : 1;
    const trophyIds = new Set(user.trophyIds);
    if (trophy) trophyIds.add(trophy);
    if (streak >= 7) trophyIds.add("week");
    if (streak >= 30) trophyIds.add("month");
    if (user.tasksSolved + tasks >= 100) trophyIds.add("epic");
    return { lastActiveDay: today(), user: { ...user, xp: user.xp + xp,
      level: Math.floor((user.xp + xp) / 100) + 1, coins: user.coins + coins,
      tasksSolved: user.tasksSolved + tasks, streak, trophyIds: [...trophyIds] } };
  }),
  buyCaseKeys: async (quantity) => {
    try {
      const user = await apiRequest<User>("/api/player/keys", {
        method: "POST", body: JSON.stringify({ quantity }),
      });
      set({ user });
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) return false;
      throw error;
    }
  },
  openCase: async (caseId) => {
    if (openingPending || get().caseOpening) return null;
    openingPending = true;
    try {
      const opening = await apiRequest<CaseOpening>(
        `/api/player/cases/${encodeURIComponent(caseId)}/open`, { method: "POST" },
      );
      set((state) => ({
        caseOpening: opening,
        caseHistory: [opening, ...state.caseHistory].slice(0, 50),
        user: state.user ? {
          ...state.user,
          caseKeys: state.user.caseKeys - 1,
          coins: state.user.coins + opening.compensation,
          ownedDecorationIds: opening.duplicate
            ? state.user.ownedDecorationIds
            : [...state.user.ownedDecorationIds, opening.decorationId],
        } : null,
      }));
      try { await get().refreshProfile(); } catch { /* The confirmed prize remains visible. */ }
      return opening;
    } finally { openingPending = false; }
  },
  dismissCaseOpening: () => set({ caseOpening: null }),
  equipDecoration: async (id) => {
    const user = await apiRequest<User>(`/api/player/equipment/${encodeURIComponent(id)}`, {
      method: "PUT", body: JSON.stringify({ equipped: true }),
    });
    set({ user });
  },
  unequipDecoration: async (id) => {
    const user = await apiRequest<User>(`/api/player/equipment/${encodeURIComponent(id)}`, {
      method: "PUT", body: JSON.stringify({ equipped: false }),
    });
    set({ user });
  },
  reportIssue: (exerciseId, text) => set((state) => ({
    reports: [...state.reports, { exerciseId, text, createdAt: Date.now() }],
  })),
}));

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
      completedLessons: [], dailyAnswers: [], bossAnswers: [],
      completeLesson: (id) => {
        if (get().completedLessons.includes(id)) return false;
        set((s) => ({ completedLessons: [...s.completedLessons, id] }));
        return true;
      },
      answerDaily: (answer) => {
        if (get().dailyAnswers.some((a) => a.date === today())) return false;
        set((s) => ({ dailyAnswers: [...s.dailyAnswers, { ...answer, date: today() }] }));
        return true;
      },
      saveBoss: (bossAnswers) => set({ bossAnswers }),
      reset: () => set({ completedLessons: [], dailyAnswers: [], bossAnswers: [] }),
    }),
    { name: "bashkir-school-progress-v1" },
  ),
);
export const isDailyDone = () =>
  useProgressStore.getState().dailyAnswers.some((a) => a.date === today());
