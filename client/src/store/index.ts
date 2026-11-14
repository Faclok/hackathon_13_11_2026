import { create } from "zustand";
import { apiRequest, ApiError, clearSession, hasSession, loginUsername, loginTelegram } from "../api/session";
import { isMiniApp } from "../api/telegram";
import type { CaseOpening, IssueReport, LevelReward, LevelRewardReceipt, RewardNotice, User } from "../types";

interface UserState {
  user: User | null;
  ready: boolean;
  authError: string;
  lastActiveDay: string | null;
  reports: IssueReport[];
  caseOpening: CaseOpening | null;
  caseHistory: CaseOpening[];
  levelRewards: LevelReward[];
  rewardNotices: RewardNotice[];
  dismissRewards: () => Promise<void>;
  bootstrap: () => Promise<void>;
  login: (username: string) => Promise<void>;
  logout: () => void;
  refreshProfile: () => Promise<void>;
  updateProfile: (firstName: string, lastName: string) => Promise<number>;
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
  authError: "",
  lastActiveDay: null,
  reports: [],
  caseOpening: null,
  caseHistory: [],
  levelRewards: [],
  rewardNotices: [],
  dismissRewards: async () => {
    const notices = get().rewardNotices;
    const levels = get().levelRewards;
    if (notices.length > 0) await apiRequest<void>("/api/player/rewards/acknowledge", {
      method: "POST", body: JSON.stringify(notices.map((notice) => notice.id)),
    });
    set((state) => ({
      rewardNotices: state.rewardNotices.filter((notice) => !notices.some((item) => item.id === notice.id)),
      levelRewards: state.levelRewards.filter((reward) => !levels.some((item) => item.level === reward.level)),
    }));
  },
  bootstrap: async () => {
    if (get().ready) return;
    bootstrapping ??= (async () => {
      if (isMiniApp()) {
        try { await loginTelegram(); }
        catch (cause) {
          set({ user: null, ready: true, authError: cause instanceof Error ? cause.message : "Не удалось войти через Telegram." });
          return;
        }
      }
      if (!hasSession()) {
        set({ user: null, ready: true });
        return;
      }
      try {
        const [receipt, caseHistory] = await Promise.all([
          apiRequest<LevelRewardReceipt>("/api/player/level-rewards/claim", { method: "POST" }),
          apiRequest<CaseOpening[]>("/api/player/cases/history"),
        ]);
        set({ user: receipt.profile, levelRewards: receipt.rewards, rewardNotices: receipt.notices, caseHistory, ready: true });
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) clearSession();
        set({ user: null, ready: true, authError: "Не удалось загрузить профиль. Откройте приложение заново." });
      }
    })();
    try { await bootstrapping; } finally { bootstrapping = null; }
  },
  login: async (username) => {
    await loginUsername(username);
    try {
      const [receipt, caseHistory] = await Promise.all([
        apiRequest<LevelRewardReceipt>("/api/player/level-rewards/claim", { method: "POST" }),
        apiRequest<CaseOpening[]>("/api/player/cases/history"),
      ]);
      set({ user: receipt.profile, levelRewards: receipt.rewards, rewardNotices: receipt.notices, caseHistory, caseOpening: null, ready: true });
    } catch (error) {
      clearSession();
      throw error;
    }
  },
  logout: () => {
    clearSession();
    set({ user: null, levelRewards: [], rewardNotices: [], caseOpening: null, caseHistory: [], reports: [] });
  },
  refreshProfile: async () => {
    const receipt = await apiRequest<LevelRewardReceipt>("/api/player/level-rewards/claim", { method: "POST" });
    set((state) => ({ user: receipt.profile, rewardNotices: receipt.notices,
      levelRewards: [...state.levelRewards, ...receipt.rewards].filter((reward, index, all) =>
        all.findIndex((other) => other.level === reward.level) === index),
    }));
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
