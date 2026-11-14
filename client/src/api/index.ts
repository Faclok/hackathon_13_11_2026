import type { User } from "../types";
import { decorationCatalog, rewardCases } from "../mocks/customization";
import { apiRequest } from "./session";
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
