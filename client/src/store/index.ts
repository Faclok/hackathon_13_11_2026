import { create } from "zustand";
import { persist } from "zustand/middleware";
import { demoUser } from "../mocks/data";
import {
  decorationCatalog,
  defaultDecorationIds,
  rewardCases,
  rollCaseDrop,
  KEY_PRICE,
  DUPLICATE_COMPENSATION,
} from "../mocks/customization";
import type {
  BossAnswer,
  CaseOpening,
  DailyAnswer,
  IssueReport,
  User,
} from "../types";
import { schoolDay as today, dayNumber } from "../utils/calendar";

function withCaseInventory(user: User): User {
  let importedIds: string[] = [];
  let importedEquipment: Partial<User> = {};
  try {
    const standalone = JSON.parse(
      localStorage.getItem("profile-customization-v2") ?? "null",
    );
    if (standalone && Array.isArray(standalone.unlocked)) {
      importedIds = standalone.unlocked.filter(
        (id: unknown): id is string =>
          typeof id === "string" &&
          decorationCatalog.some((item) => item.id === id),
      );
      const slots = {
        hat: "hatId",
        frame: "frameId",
        bg: "backgroundId",
        pet: "petId",
      } as const;
      for (const [slot, field] of Object.entries(slots)) {
        const item = decorationCatalog.find(
          (item) => item.id === standalone.equipped?.[slot],
        );
        if (
          item &&
          item.type === (slot === "bg" ? "background" : slot) &&
          !item.isDefault &&
          importedIds.includes(item.id)
        )
          importedEquipment[field] = item.id;
      }
    }
  } catch {
    /* An unreadable standalone save does not affect the main profile. */
  }
  return {
    ...user,
    ...importedEquipment,
    caseKeys:
      Number.isInteger(user.caseKeys) && user.caseKeys >= 0 ? user.caseKeys : 0,
    equippedItemIds:
      user.equippedItemIds ??
      user.ownedDecorationIds.filter((id) =>
        decorationCatalog.some(
          (item) => item.id === id && item.type === "item",
        ),
      ),
    ownedDecorationIds: [
      ...new Set([
        ...user.ownedDecorationIds,
        ...defaultDecorationIds,
        ...importedIds,
      ]),
    ],
  };
}
interface UserState {
  user: User | null;
  lastActiveDay: string | null;
  reports: IssueReport[];
  login: (nick: string) => void;
  logout: () => void;
  reward: (xp: number, coins: number, tasks: number, trophy?: string) => void;
  caseOpening: CaseOpening | null;
  caseHistory: CaseOpening[];
  buyCaseKeys: (quantity: number) => boolean;
  openCase: (caseId: string) => CaseOpening | null;
  dismissCaseOpening: () => void;
  equipDecoration: (id: string) => void;
  unequipDecoration: (id: string) => void;
  reportIssue: (exerciseId: string, text: string) => void;
}
export const useUserStore = create<UserState>()(
  persist(
    (set, get) => ({
      user: withCaseInventory(demoUser),
      lastActiveDay: null,
      reports: [],
      caseOpening: null,
      caseHistory: [],
      login: (nick) =>
        set({
          user: { ...demoUser, nick: nick.trim() },
          caseOpening: null,
          caseHistory: [],
        }),
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
      buyCaseKeys: (quantity) => {
        const { user } = get();
        if (
          !user ||
          !Number.isInteger(quantity) ||
          quantity < 1 ||
          quantity > 100 ||
          user.coins < quantity * KEY_PRICE
        )
          return false;
        set({
          user: {
            ...user,
            coins: user.coins - quantity * KEY_PRICE,
            caseKeys: user.caseKeys + quantity,
          },
        });
        return true;
      },
      openCase: (caseId) => {
        const { user, caseOpening, caseHistory } = get();
        const caseItem = rewardCases.find((item) => item.id === caseId);
        if (!user || user.caseKeys < 1 || caseOpening || !caseItem) return null;
        const winner = rollCaseDrop(caseItem);
        const duplicate = user.ownedDecorationIds.includes(winner.id);
        const opening: CaseOpening = {
          id:
            globalThis.crypto?.randomUUID?.() ??
            `case-${Date.now()}-${Math.random().toString(36).slice(2)}`,
          caseId,
          decorationId: winner.id,
          duplicate,
          compensation: duplicate ? DUPLICATE_COMPENSATION : 0,
          openedAt: new Date().toISOString(),
        };
        // Award and key consumption are one persisted update; animation never changes the balance.
        set({
          user: {
            ...user,
            caseKeys: user.caseKeys - 1,
            coins: user.coins + opening.compensation,
            ownedDecorationIds: duplicate
              ? user.ownedDecorationIds
              : [...user.ownedDecorationIds, winner.id],
          },
          caseOpening: opening,
          caseHistory: [opening, ...caseHistory].slice(0, 50),
        });
        return opening;
      },
      dismissCaseOpening: () => set({ caseOpening: null }),
      equipDecoration: (id) =>
        set(({ user }) => {
          const item = decorationCatalog.find((d) => d.id === id);
          if (!user || !item || !user.ownedDecorationIds.includes(id))
            return {};
          return {
            user: {
              ...user,
              ...(item.type === "background"
                ? { backgroundId: id }
                : item.type === "frame"
                  ? { frameId: item.isDefault ? undefined : id }
                  : item.type === "hat"
                    ? { hatId: item.isDefault ? undefined : id }
                    : item.type === "pet"
                      ? { petId: item.isDefault ? undefined : id }
                      : {
                          equippedItemIds: [
                            ...new Set([...user.equippedItemIds, id]),
                          ],
                        }),
            },
          };
        }),
      unequipDecoration: (id) =>
        set(({ user }) => {
          const item = decorationCatalog.find((item) => item.id === id);
          if (!user || !item || !user.ownedDecorationIds.includes(id))
            return {};
          return {
            user: {
              ...user,
              ...(item.type === "frame" && user.frameId === id
                ? { frameId: undefined }
                : item.type === "hat" && user.hatId === id
                  ? { hatId: undefined }
                  : item.type === "pet" && user.petId === id
                    ? { petId: undefined }
                    : item.type === "item"
                      ? {
                          equippedItemIds: user.equippedItemIds.filter(
                            (itemId) => itemId !== id,
                          ),
                        }
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
      version: 2,
      migrate: (stored, version) => {
        const previous = stored as UserState;
        if (!previous.user) return previous;
        if (version >= 1)
          return {
            ...previous,
            user: withCaseInventory(previous.user),
            caseOpening: null,
            caseHistory: [],
          };
        const user = previous.user;
        const xp = Math.max(user.xp, demoUser.xp);
        return {
          ...previous,
          caseOpening: null,
          caseHistory: [],
          user: withCaseInventory({
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
          }),
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
