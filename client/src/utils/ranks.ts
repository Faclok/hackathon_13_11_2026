import type { User } from "../types";

export const ranks = [
  ["Яңы башлаусы", 0, 0],
  ["Уҡыусы", 10, 0],
  ["Тырыш", 50, 3],
  ["Белгес", 100, 7],
  ["Оста", 250, 14],
  ["Батыр", 500, 30],
  ["Аҡыл эйәһе", 1000, 60],
] as const;

type RankProgress = Pick<User, "tasksSolved" | "streak">;
export const BOSS_MIN_RANK = ranks[4];
export const getCurrentRank = (user: RankProgress) =>
  ranks
    .filter(
      ([, tasks, streak]) => user.tasksSolved >= tasks && user.streak >= streak,
    )
    .at(-1) ?? ranks[0];
export const canAccessBoss = (user: RankProgress | null | undefined) =>
  !!user &&
  user.tasksSolved >= BOSS_MIN_RANK[1] &&
  user.streak >= BOSS_MIN_RANK[2];
