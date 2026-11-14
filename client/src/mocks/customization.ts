import type { Decoration, RewardCase, User } from "../types";
import { decorations as legacyDecorations } from "./data";
import hatsCase from "../assets/cases/hats-case.png";
import framesCase from "../assets/cases/frames-case.png";
import backgroundsCase from "../assets/cases/backgrounds-case.png";
import petsCase from "../assets/cases/pets-case.png";
import { culturalPrizes } from "./prizes";

// Catalog, artwork and drop weights adapted from castomization-page.
export const KEY_PRICE = 10;
export const DUPLICATE_COMPENSATION = 5;
export const rarities = {
  common: { label: "Обычный", weight: 45 },
  rare: { label: "Редкий", weight: 30 },
  epic: { label: "Эпический", weight: 18 },
  legendary: { label: "Легендарный", weight: 7 },
} as const;

export const caseDecorations: Decoration[] = [
  ...culturalPrizes,
  {
    id: "hat_none",
    type: "hat",
    title: "Без головного убора",
    rarity: "common",
    isDefault: true,
    previewUrl: "◌",
    description: "Классический вид без лишних деталей",
    price: 0,
    source: "wheel",
  },
  {
    id: "hat_tubeteika",
    type: "hat",
    title: "Тюбетейка",
    rarity: "common",
    previewUrl: "🧢",
    description: "Традиционный узорчатый головной убор",
    price: 0,
    source: "wheel",
  },
  {
    id: "hat_cap",
    type: "hat",
    title: "Академическая шапочка",
    rarity: "rare",
    previewUrl: "🎓",
    description: "Символ отличника башкирского языка",
    price: 0,
    source: "wheel",
  },
  {
    id: "hat_burek",
    type: "hat",
    title: "Бүрек",
    rarity: "epic",
    previewUrl: "🧢",
    description: "Тёплая национальная шапка батыра",
    price: 0,
    source: "wheel",
  },
  {
    id: "hat_crown",
    type: "hat",
    title: "Корона знатока",
    rarity: "legendary",
    previewUrl: "👑",
    description: "Награда мастера языка и культуры",
    price: 0,
    source: "wheel",
  },
  {
    id: "frame_none",
    type: "frame",
    title: "Без рамки",
    rarity: "common",
    isDefault: true,
    previewUrl: "⭕",
    description: "Стандартное обрамление аватара",
    price: 0,
    source: "wheel",
  },
  {
    id: "frame_neon_leaf",
    type: "frame",
    title: "Неоновый лист",
    rarity: "common",
    previewUrl: "🍃",
    description: "Свежее сияние весенней листвы",
    price: 0,
    source: "wheel",
  },
  {
    id: "frame_fire_streak",
    type: "frame",
    title: "Огненный стрик",
    rarity: "rare",
    previewUrl: "🔥",
    description: "Для тех, кто занимается каждый день",
    price: 0,
    source: "wheel",
  },
  {
    id: "frame_kurai_gold",
    type: "frame",
    title: "Золото курая",
    rarity: "epic",
    previewUrl: "✨",
    description: "Благородный блеск степного курая",
    price: 0,
    source: "wheel",
  },
  {
    id: "frame_ural_gems",
    type: "frame",
    title: "Самоцветы Урала",
    rarity: "legendary",
    previewUrl: "💎",
    description: "Драгоценное сияние Уральских гор",
    price: 0,
    source: "wheel",
  },
  {
    id: "bg_default",
    type: "background",
    title: "Изумрудная школа",
    rarity: "common",
    isDefault: true,
    previewUrl: "🌿",
    description: "Фирменный зелёный градиент школы",
    background: "linear-gradient(135deg, #2e7d32, #25963f 57%, #1db954)",
    price: 0,
    source: "wheel",
  },
  {
    id: "bg_honey_dawn",
    type: "background",
    title: "Медовый рассвет",
    rarity: "common",
    previewUrl: "🍯",
    description: "Тёплые оттенки башкирского мёда",
    background: "linear-gradient(135deg, #d97706, #f59e0b 58%, #fbbf24)",
    price: 0,
    source: "wheel",
  },
  {
    id: "bg_ural_mountains",
    type: "background",
    title: "Уральские горы",
    rarity: "rare",
    previewUrl: "⛰️",
    description: "Величественные вершины и чистый воздух",
    background: "linear-gradient(135deg, #1e3a8a, #2563eb 58%, #38bdf8)",
    price: 0,
    source: "wheel",
  },
  {
    id: "bg_ufa_night",
    type: "background",
    title: "Ночное небо Уфы",
    rarity: "epic",
    previewUrl: "🌌",
    description: "Огни ночной столицы под звёздами",
    background: "linear-gradient(135deg, #1e1b4b, #312e81 58%, #4338ca)",
    price: 0,
    source: "wheel",
  },
  {
    id: "bg_golden_ornament",
    type: "background",
    title: "Золотой узор",
    rarity: "legendary",
    previewUrl: "⚜️",
    description: "Праздничный узор с золотым отливом",
    background: "linear-gradient(135deg, #143d1d, #1b5e20 58%, #b45309)",
    price: 0,
    source: "wheel",
  },
  {
    id: "pet_none",
    type: "pet",
    title: "Без питомца",
    rarity: "common",
    isDefault: true,
    previewUrl: "🐾",
    description: "Путешествие в одиночку",
    price: 0,
    source: "wheel",
  },
];

// Retired pets stay resolvable for existing inventories and opening history,
// but are not included in any case's reward pool.
const retiredPets: Decoration[] = [
  {
    id: "pet_bee",
    type: "pet",
    title: "Бурзянская пчёлка",
    rarity: "common",
    previewUrl: "🐝",
    petName: "Бал-бал",
    description: "Трудолюбивая помощница с пасеки",
    price: 0,
    source: "wheel",
  },
  {
    id: "pet_marten",
    type: "pet",
    title: "Уфимская куница",
    rarity: "rare",
    previewUrl: "🦦",
    petName: "Алтын",
    description: "Ловкий и умный символ столицы",
    price: 0,
    source: "wheel",
  },
  {
    id: "pet_bear",
    type: "pet",
    title: "Бурый медвежонок",
    rarity: "epic",
    previewUrl: "🐻",
    petName: "Айыу",
    description: "Добрый и сильный хранитель лесов",
    price: 0,
    source: "wheel",
  },
  {
    id: "pet_eagle",
    type: "pet",
    title: "Степной беркут",
    rarity: "legendary",
    previewUrl: "🦅",
    petName: "Бөркөт",
    description: "Гордая птица бескрайних степей",
    price: 0,
    source: "wheel",
  },
];

export const decorationCatalog: Decoration[] = [
  ...legacyDecorations.map((item) => ({
    ...item,
    source: "wheel" as const,
    price: 0,
    rarity: "common" as const,
  })),
  ...caseDecorations,
  ...retiredPets,
];
export const defaultDecorationIds = caseDecorations
  .filter((item) => item.isDefault)
  .map((item) => item.id);
export const rewardCases: RewardCase[] = [
  {
    id: "case_hat",
    type: "hat",
    title: "Головные уборы",
    description: "От тюбетейки до короны знатока",
    imageUrl: hatsCase,
  },
  {
    id: "case_frame",
    type: "frame",
    title: "Рамки аватара",
    description: "Сияющие контуры и самоцветы Урала",
    imageUrl: framesCase,
  },
  {
    id: "case_bg",
    type: "background",
    title: "Фоны профиля",
    description: "Агидель, Шульган-Таш и башкирские узоры",
    imageUrl: backgroundsCase,
  },
  {
    id: "case_pet",
    type: "pet",
    dropTypes: ["pet", "item"],
    title: "Питомцы и предметы",
    description: "Акбузат, курай и другие открытия о Башкортостане",
    imageUrl: petsCase,
  },
];
export const getCaseDrops = (caseItem: RewardCase) =>
  caseDecorations.filter(
    (item) =>
      (caseItem.dropTypes ?? [caseItem.type]).includes(item.type) &&
      !item.isDefault,
  );
// Rarity weights apply to the whole tier, then split equally among its items.
// If a case has no items of a tier, the remaining weights are normalized.
export function getCaseDropChance(caseItem: RewardCase, item: Decoration) {
  const pool = getCaseDrops(caseItem);
  if (!pool.some((drop) => drop.id === item.id)) return 0;
  const tiers = new Set(pool.map((drop) => drop.rarity ?? "common"));
  const total = [...tiers].reduce(
    (sum, rarity) => sum + rarities[rarity].weight,
    0,
  );
  const rarity = item.rarity ?? "common";
  const count = pool.filter(
    (drop) => (drop.rarity ?? "common") === rarity,
  ).length;
  return (rarities[rarity].weight / total / count) * 100;
}
export function rollCaseDrop(caseItem: RewardCase): Decoration {
  const pool = getCaseDrops(caseItem);
  if (!pool.length) throw new Error(`Пустой кейс: ${caseItem.id}`);
  let point = Math.random() * 100;
  for (const item of pool) {
    point -= getCaseDropChance(caseItem, item);
    if (point < 0) return item;
  }
  return pool[pool.length - 1];
}
export function isDecorationEquipped(user: User, item: Decoration) {
  switch (item.type) {
    case "background":
      return user.backgroundId === item.id;
    case "frame":
      return item.id === "frame_none"
        ? !user.frameId || user.frameId === item.id
        : user.frameId === item.id;
    case "hat":
      return item.id === "hat_none"
        ? !user.hatId || user.hatId === item.id
        : user.hatId === item.id;
    case "pet":
      return item.id === "pet_none"
        ? !user.petId || user.petId === item.id
        : user.petId === item.id;
    case "item":
      return (
        user.equippedItemIds?.includes(item.id) ??
        user.ownedDecorationIds.includes(item.id)
      );
  }
}
