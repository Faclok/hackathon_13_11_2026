import { apiRequest } from "./session";
export interface Word { id: string; text: string }
export interface Question {
  id: string; attemptToken: string; kind: string; schemaVersion: number;
  prompt: { text?: string; audioUrl?: string; before?: string; after?: string; options?: Word[]; tokens?: Word[]; left?: Word[]; right?: Word[]; allowCustom?: boolean };
}
export interface Level { id: string; number: number; questionCount: number; correctCount: number; question: Question }
export interface Progress { completedLevels: number; completedBosses: number; availableBosses: number; attempts: number; mistakes: number }
export interface LevelStats { number: number; questionCount: number; correctCount: number; attempts: number; mistakes: number; completedAt: string | null }
export type ExerciseAnswer = { text: string } | { optionId: string } | { tokenIds: string[] } | { pairs: { leftId: string; rightId: string }[] };
export interface Review { id: string; original: string; translation: string; kind: string; options: Word[]; allowCustom: boolean; expiresAt: string }
export interface Boss { id: string; checkpoint: number; questionCount: number; completedCount: number; question: Review | null }
export type ReviewAnswer = { action: "Confirm" | "Correct" | "Skip"; correction?: string; selectedVariantId?: string };
export const learningProgress = () => apiRequest<Progress>("/api/learning/progress");
export const learningHistory = () => apiRequest<LevelStats[]>("/api/learning/statistics?offset=0&limit=20");
export const currentLevel = () => apiRequest<Level | undefined>("/api/learning/current", { method: "POST" });
export const currentBoss = () => apiRequest<Boss | undefined>("/api/learning/boss", { method: "POST" });
export interface SavedAnswer { path: string; body: unknown }
export const sendLearningAnswer = <T,>(saved: SavedAnswer) => apiRequest<T>(saved.path, { method: "POST", body: JSON.stringify(saved.body) });
