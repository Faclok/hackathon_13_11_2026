export interface User {
  id: string;
  nick: string;
  avatarUrl: string;
  level: number;
  xp: number;
  coins: number;
  streak: number;
  tasksSolved: number;
  accuracy: number;
  trophyIds: string[];
  ownedDecorationIds: string[];
  backgroundId?: string;
  frameId?: string;
  langPair: "ba-ru";
}
export interface DailyFragment {
  id: string;
  textBa: string;
  autoTranslation: string;
  source: string;
  hasError: boolean;
}
export interface DailyAnswer {
  fragmentId: string;
  action: "confirm" | "fix";
  fixedText?: string;
}
export type Exercise =
  | {
      id: string;
      type: "choice";
      prompt: string;
      options: string[];
      correct: string;
    }
  | { id: string; type: "match"; pairs: { ba: string; ru: string }[] }
  | {
      id: string;
      type: "order";
      ruSentence: string;
      words: string[];
      correctOrder: string[];
    };
export interface Lesson {
  id: string;
  unitId: string;
  title: string;
  exercises: Exercise[];
}
export interface BossPhrase {
  id: string;
  bashkir: string;
  options: { id: string; ru: string; votes: number }[];
  totalVotes: number;
}
export interface BossAnswer {
  phraseId: string;
  chosenOptionId?: string;
  customText?: string;
  isCustom: boolean;
}
export interface Decoration {
  id: string;
  type: "background" | "frame" | "item";
  title: string;
  description: string;
  previewUrl: string;
  price: number;
  source: "shop" | "wheel";
}
export interface Trophy {
  id: string;
  title: string;
  description: string;
  icon: string;
  rarity: "common" | "rare" | "epic" | "legendary";
}
export interface IssueReport {
  exerciseId: string;
  text: string;
  createdAt: number;
}
