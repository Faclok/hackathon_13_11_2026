import { apiRequest } from "./session";

export interface ExerciseItem { id: string; text: string }
export type ExerciseQuestion =
  | { id: string; attemptToken: string; kind: "translation-text"; schemaVersion: 1; prompt: { text: string; sourceLanguage: string; targetLanguage: string } }
  | { id: string; attemptToken: string; kind: "audio-dictation" | "audio-translation"; schemaVersion: 1; prompt: { audioId: string; audioUrl: string | null; sourceLanguage: string; targetLanguage: string } }
  | { id: string; attemptToken: string; kind: "translation-text"; schemaVersion: 2; prompt: { text: string; sourceLanguage: string; targetLanguage: string; options: ExerciseItem[]; wordHints: { word: string; translation: string }[]; hintTranslation: string } }
  | { id: string; attemptToken: string; kind: "audio-dictation" | "audio-translation"; schemaVersion: 2; prompt: { audioId: string; audioUrl: string | null; sourceLanguage: string; targetLanguage: string; original: string; options: ExerciseItem[] } }
  | { id: string; attemptToken: string; kind: "missing-word"; schemaVersion: number; prompt: { before: string; after: string; options: ExerciseItem[]; allowCustom: boolean } }
  | { id: string; attemptToken: string; kind: "sentence-order"; schemaVersion: number; prompt: { tokens: ExerciseItem[] } }
  | { id: string; attemptToken: string; kind: "word-match"; schemaVersion: number; prompt: { left: ExerciseItem[]; right: ExerciseItem[]; languagePair: string } };
export type ExerciseAnswer = { text: string } | { optionId: string } | { tokenIds: string[] } | { pairs: { leftId: string; rightId: string }[] };
export interface Level { id: string; number: number; questionCount: number; correctCount: number; question: ExerciseQuestion }
export interface LevelAnswerResult { attemptToken: string; recognized: boolean; levelCompleted: boolean; correctAnswer: string | null }
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
export interface TeacherBossTask { id: string; title: string; sourceText: string; className: string; dueAt: string | null }
export type BossSource = { source: "teacher"; task: TeacherBossTask } | { source: "random"; assignment: BossQuestion };

export const getLearningProgress = () => apiRequest<LearningProgress>("/api/learning/progress");
export const getLevelStatistics = (offset = 0, limit = 20) =>
  apiRequest<LevelStatistic[]>(`/api/learning/statistics?offset=${offset}&limit=${limit}`);
export const getCurrentLevel = () => apiRequest<Level | null>("/api/learning/current", { method: "POST" });
export const answerLevel = (levelId: string, questionId: string, attemptToken: string, answer: ExerciseAnswer) =>
  apiRequest<LevelAnswerResult>(`/api/learning/levels/${encodeURIComponent(levelId)}/questions/${encodeURIComponent(questionId)}/answer`, {
    method: "POST", body: JSON.stringify({ attemptToken, answer }),
  });
export const checkWordPair = (levelId: string, questionId: string, attemptToken: string, leftId: string, rightId: string) =>
  apiRequest<{ correct: boolean }>(`/api/learning/levels/${encodeURIComponent(levelId)}/questions/${encodeURIComponent(questionId)}/pairs/check`, {
    method: "POST", body: JSON.stringify({ attemptToken, leftId, rightId }),
  });
export const getBossLevel = () => apiRequest<BossLevel | null>("/api/learning/boss", { method: "POST" });
export const getNextBossSource = () => apiRequest<BossSource | null>("/api/lms/me/next-translation", { method: "POST" });
export const startBoss = () => apiRequest<{ id: string; checkpoint: number } | null>("/api/learning/boss/start", { method: "POST" });
export const answerTeacherBoss = (taskId: string, answer: string) =>
  apiRequest<{ bossCompleted: boolean }>(`/api/lms/me/tasks/${encodeURIComponent(taskId)}/submission`, {
    method: "PUT", body: JSON.stringify({ answer, boss: true }),
  });
export const answerBoss = (bossId: string, assignmentId: string, answer: BossAnswer) =>
  apiRequest<BossAnswerResult>(`/api/learning/bosses/${encodeURIComponent(bossId)}/questions/${encodeURIComponent(assignmentId)}/answer`, {
    method: "POST", body: JSON.stringify(answer),
  });
