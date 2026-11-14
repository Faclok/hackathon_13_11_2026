import {
  bossPhrases,
  dailyFragments,
  lessons,
} from "../mocks/data";
import type { DailyAnswer, User } from "../types";
import { schoolDay, dayNumber } from "../utils/calendar";
import { decorationCatalog, rewardCases } from "../mocks/customization";
import { canAccessBoss } from "../utils/ranks";
import { apiRequest } from "./session";
// Boss and Learn keep their existing local data until those screens are in scope.
export const getLessons = async () => lessons;
export const getDaily = async () =>
  dailyFragments[dayNumber(schoolDay()) % dailyFragments.length];
export const submitDaily = async (answer: DailyAnswer) => answer;
export const getBossPhrases = async (
  user: Pick<User, "tasksSolved" | "streak">,
) => {
  if (!canAccessBoss(user)) throw new Error("BOSS_RANK_REQUIRED");
  return bossPhrases;
};
export const getDecorations = async () => {
  const catalog = await apiRequest<{ id: string }[]>("/api/player/decorations");
  const ids = new Set(catalog.map((item) => item.id));
  return decorationCatalog.filter((item) => ids.has(item.id));
};
export const getCases = async () => {
  const catalog = await apiRequest<{ id: string }[]>("/api/player/cases");
  const ids = new Set(catalog.map((item) => item.id));
  return rewardCases.filter((item) => ids.has(item.id));
};
export const getLeaderboard = async (
  _user: User,
  period: "global" | "monthly",
  metric: "tasks" | "streak",
) => apiRequest<{ user: User; value: number; rank: number }[]>(
  `/api/leaderboard?period=${period}&metric=${metric}`,
);
