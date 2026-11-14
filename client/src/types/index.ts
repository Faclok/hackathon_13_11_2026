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
  hatId?: string;
  petId?: string;
  caseKeys: number;
  equippedItemIds: string[];
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
  source?: {
    fragmentId: string;
    dataset: string;
    recordId: string;
    collection: string;
    attribution: string;
    license: string;
  };
}
export interface BossAnswer {
  phraseId: string;
  chosenOptionId?: string;
  customText?: string;
  isCustom: boolean;
}
export interface Decoration {
  id: string;
  type: "background" | "frame" | "item" | "hat" | "pet";
  title: string;
  description: string;
  previewUrl: string;
  imageUrl?: string;
  price: number;
  source: "shop" | "wheel";
  rarity?: "common" | "rare" | "epic" | "legendary";
  isDefault?: boolean;
  background?: string;
  petName?: string;
}
export interface RewardCase {
  id: string;
  type: "hat" | "frame" | "background" | "pet" | "item";
  title: string;
  description: string;
  imageUrl: string;
}
export interface CaseOpening {
  id: string;
  caseId: string;
  decorationId: string;
  duplicate: boolean;
  compensation: number;
  openedAt: string;
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
