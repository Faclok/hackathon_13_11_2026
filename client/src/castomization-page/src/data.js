export const RARITIES = {
  common: { label: "Обычный", weight: 45 },
  rare: { label: "Редкий", weight: 30 },
  epic: { label: "Эпический", weight: 18 },
  legendary: { label: "Легендарный", weight: 7 },
};

export const ITEMS = [
  { id: "hat_none", type: "hat", name: "Без головного убора", rarity: "common", isDefault: true, emoji: "🚫", description: "Классический вид без лишних деталей" },
  { id: "hat_tubeteika", type: "hat", name: "Тюбетейка", rarity: "common", emoji: "🧢", description: "Традиционный узорчатый головной убор" },
  { id: "hat_cap", type: "hat", name: "Академическая шапочка", rarity: "rare", emoji: "🎓", description: "Символ отличника башкирского языка" },
  { id: "hat_burek", type: "hat", name: "Бүрек (меховая шапка)", rarity: "epic", emoji: "🧢", description: "Тёплая национальная шапка батыра" },
  { id: "hat_crown", type: "hat", name: "Корона знатока", rarity: "legendary", emoji: "👑", description: "Награда мастера языка и культуры" },
  { id: "frame_none", type: "frame", name: "Без рамки", rarity: "common", isDefault: true, emoji: "⭕", description: "Стандартное обрамление аватара" },
  { id: "frame_neon_leaf", type: "frame", name: "Неоновый лист", rarity: "common", emoji: "🍃", description: "Свежее сияние весенней листвы" },
  { id: "frame_fire_streak", type: "frame", name: "Огненный стрик", rarity: "rare", emoji: "🔥", description: "Для тех, кто занимается каждый день" },
  { id: "frame_kurai_gold", type: "frame", name: "Золото курая", rarity: "epic", emoji: "✨", description: "Благородный блеск степного курая" },
  { id: "frame_ural_gems", type: "frame", name: "Самоцветы Урала", rarity: "legendary", emoji: "💎", description: "Драгоценное сияние Уральских гор" },
  { id: "bg_default", type: "bg", name: "Изумрудная школа", rarity: "common", isDefault: true, emoji: "🌿", description: "Фирменный зелёный градиент школы", background: "linear-gradient(135deg, #2e7d32, #25963f 57%, #1db954)" },
  { id: "bg_honey_dawn", type: "bg", name: "Медовый рассвет", rarity: "common", emoji: "🍯", description: "Тёплые оттенки башкирского мёда", background: "linear-gradient(135deg, #d97706, #f59e0b 58%, #fbbf24)" },
  { id: "bg_ural_mountains", type: "bg", name: "Уральские горы", rarity: "rare", emoji: "⛰️", description: "Величественные вершины и чистый воздух", background: "linear-gradient(135deg, #1e3a8a, #2563eb 58%, #38bdf8)" },
  { id: "bg_ufa_night", type: "bg", name: "Ночное небо Уфы", rarity: "epic", emoji: "🌌", description: "Огни ночной столицы под звёздами", background: "linear-gradient(135deg, #1e1b4b, #312e81 58%, #4338ca)" },
  { id: "bg_golden_ornament", type: "bg", name: "Золотой узор", rarity: "legendary", emoji: "⚜️", description: "Праздничный узор с золотым отливом", background: "linear-gradient(135deg, #143d1d, #1b5e20 58%, #b45309)" },
  { id: "pet_none", type: "pet", name: "Без питомца", rarity: "common", isDefault: true, emoji: "🐾", description: "Путешествие в одиночку" },
  { id: "pet_bee", type: "pet", name: "Бурзянская пчёлка", rarity: "common", emoji: "🐝", petName: "Бал-бал", description: "Трудолюбивая помощница с пасеки" },
  { id: "pet_marten", type: "pet", name: "Уфимская куница", rarity: "rare", emoji: "🦦", petName: "Алтын", description: "Ловкий и умный символ столицы" },
  { id: "pet_bear", type: "pet", name: "Бурый медвежонок", rarity: "epic", emoji: "🐻", petName: "Айыу", description: "Добрый и сильный хранитель лесов" },
  { id: "pet_eagle", type: "pet", name: "Степной беркут", rarity: "legendary", emoji: "🦅", petName: "Бөркөт", description: "Гордая птица бескрайних степей" },
];

export const CASES = [
  { id: "case_hat", type: "hat", title: "Кейс «Головные уборы»", subtitle: "Тюбетейка, бүрек и корона знатока", icon: "♛", badge: "👑" },
  { id: "case_frame", type: "frame", title: "Кейс «Рамки аватара»", subtitle: "Сияющие контуры и самоцветы Урала", icon: "▣", badge: "💎" },
  { id: "case_bg", type: "bg", title: "Кейс «Фоны профиля»", subtitle: "Уральские горы, мёд и огни Уфы", icon: "▧", badge: "⛰️" },
  { id: "case_pet", type: "pet", title: "Кейс «Питомцы»", subtitle: "Верные спутники рядом с профилем", icon: "♧", badge: "🐝" },
];

export const DEFAULT_EQUIPPED = {
  hat: "hat_none",
  frame: "frame_none",
  bg: "bg_default",
  pet: "pet_none",
};

export const WARDROBE_TABS = [
  { id: "hat", label: "Головные уборы" },
  { id: "frame", label: "Рамки" },
  { id: "bg", label: "Фоны" },
  { id: "pet", label: "Питомцы" },
];
