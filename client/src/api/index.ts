import {
  bossPhrases,
  dailyFragments,
  leaderboardUsers,
  lessons,
} from "../mocks/data";
import type { DailyAnswer, User } from "../types";
import { schoolDay, dayNumber } from "../utils/calendar";
import { decorationCatalog, rewardCases } from "../mocks/customization";
// Replace mock implementations with fetch calls when the API is connected.
export const getLessons = async () => lessons;
export const getDaily = async () =>
  dailyFragments[dayNumber(schoolDay()) % dailyFragments.length];
export const submitDaily = async (answer: DailyAnswer) => answer;
export const getBossPhrases = async () => bossPhrases;
export const getDecorations = async () => decorationCatalog;
export const getCases = async () => rewardCases;
export const getLeaderboard = async (
  user: User,
  period: "global" | "monthly",
  metric: "tasks" | "streak",
) =>
  [...leaderboardUsers, user]
    .map((entry, i) => ({
      user: entry,
      value:
        metric === "streak"
          ? period === "global"
            ? entry.streak
            : Math.min(entry.streak, Number(schoolDay().slice(-2)))
          : period === "global"
            ? entry.tasksSolved
            : Math.round(entry.tasksSolved * (0.25 + (i % 4) * 0.15)),
    }))
    .sort(
      (a, b) =>
        b.value - a.value ||
        Number(b.user.id === user.id) - Number(a.user.id === user.id),
    )
    .map((entry, i) => ({ ...entry, rank: i + 1 }));
