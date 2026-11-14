import { apiRequest } from "./session";

export interface ExerciseItem { id: string; text: string }
export type ExerciseQuestion =
  | { id: string; attemptToken: string; kind: "translation-text"; schemaVersion: number; prompt: { text: string; sourceLanguage: string; targetLanguage: string } }
  | { id: string; attemptToken: string; kind: "audio-dictation" | "audio-translation"; schemaVersion: number; prompt: { audioId: string; audioUrl: string | null; sourceLanguage: string; targetLanguage: string } }
  | { id: string; attemptToken: string; kind: "missing-word"; schemaVersion: number; prompt: { before: string; after: string; options: ExerciseItem[]; allowCustom: boolean } }
  | { id: string; attemptToken: string; kind: "sentence-order"; schemaVersion: number; prompt: { tokens: ExerciseItem[] } }
  | { id: string; attemptToken: string; kind: "word-match"; schemaVersion: number; prompt: { left: ExerciseItem[]; right: ExerciseItem[]; languagePair: string } };
export type ExerciseAnswer = { text: string } | { optionId: string } | { tokenIds: string[] } | { pairs: { leftId: string; rightId: string }[] };
export interface Level { id: string; number: number; questionCount: number; correctCount: number; question: ExerciseQuestion }
export interface LevelAnswerResult { attemptToken: string; recognized: boolean; levelCompleted: boolean }
export interface LearningProgress { completedLevels: number; completedBosses: number; availableBosses: number; attempts: number; mistakes: number }
export interface LevelStatistic { number: number; questionCount: number; correctCount: number; attempts: number; mistakes: number; startedAt: string; completedAt: string | null }

export interface BossQuestion {
  id: string; caseId: string; variantId: string; original: string; translation: string; expiresAt: string;
  kind: "translation-create" | "translation-confirm" | "translation-choice";
  options: { id: string; text: string; votes: number }[];
  allowCustom: boolean;
}
export interface BossLevel { id: string; checkpoint: number; questionCount: number; completedCount: number; question: BossQuestion | null }
export type BossAnswer = { action: "Confirm"; selectedVariantId?: string } | { action: "Correct"; correction: string } | { action: "Skip" };
export interface BossAnswerResult { responseId: string; archived: boolean; bossCompleted: boolean }

export const getLearningProgress = () => apiRequest<LearningProgress>("/api/learning/progress");
export const getLevelStatistics = (offset = 0, limit = 20) =>
  apiRequest<LevelStatistic[]>(`/api/learning/statistics?offset=${offset}&limit=${limit}`);
export const getCurrentLevel = () => apiRequest<Level | null>("/api/learning/current", { method: "POST" });
export const answerLevel = (levelId: string, questionId: string, attemptToken: string, answer: ExerciseAnswer) =>
  apiRequest<LevelAnswerResult>(`/api/learning/levels/${encodeURIComponent(levelId)}/questions/${encodeURIComponent(questionId)}/answer`, {
    method: "POST", body: JSON.stringify({ attemptToken, answer }),
  });
export const getBossLevel = () => apiRequest<BossLevel | null>("/api/learning/boss", { method: "POST" });
export const answerBoss = (bossId: string, assignmentId: string, answer: BossAnswer) =>
  apiRequest<BossAnswerResult>(`/api/learning/bosses/${encodeURIComponent(bossId)}/questions/${encodeURIComponent(assignmentId)}/answer`, {
    method: "POST", body: JSON.stringify(answer),
  });
