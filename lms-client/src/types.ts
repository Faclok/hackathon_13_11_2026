export type TeacherClass = {
  id: string;
  teacherId: string;
  name: string;
  description: string;
  createdAt: string;
};
export type Member = {
  classId: string;
  userId: string;
  username: string;
  displayName: string;
  joinedAt: string;
};
export type Translation = {
  id: string;
  sourceText: string;
  translationText: string;
  languagePair: string;
  rating?: number;
  status?: string;
  teacherId?: string;
  coreCaseId?: string | null;
};
export type Assignment = {
  id: string;
  classId: string;
  title: string;
  sourceText: string;
  referenceTranslation: string;
  translationStatus: string;
  translationRating: number;
  catalogCaseId?: string | null;
  teacherTranslationId?: string | null;
  dueAt?: string | null;
  createdAt: string;
};
export type Answer = {
  userId: string;
  username: string;
  displayName: string;
  answer: string | null;
  submittedAt: string | null;
  verdict: "missing" | "correct" | "incorrect" | "review";
  referenceTranslation: string;
};
export type TaskReport = Assignment & {
  participated: number;
  correct: number;
  incorrect: number;
  needsReview: number;
  acceptedTranslations: string[];
  answers: Answer[];
};
export type ClassReport = {
  classId: string;
  className: string;
  memberCount: number;
  tasks: TaskReport[];
};
export type StudentTask = {
  id: string;
  classId: string;
  className: string;
  title: string;
  sourceText: string;
  dueAt: string | null;
  createdAt: string;
  answer: string | null;
  submittedAt: string | null;
};
