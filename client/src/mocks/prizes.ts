import type { Decoration } from "../types";
import kuraiBackground from "../assets/prizes/1/bgs/1_kurai.svg";
import yurtBackground from "../assets/prizes/1/bgs/2_yurta.svg";
import agidelBackground from "../assets/prizes/1/bgs/3_agidel.svg";
import shulganTashBackground from "../assets/prizes/1/bgs/4_shulgan_tash.svg";
import honeyBackground from "../assets/prizes/1/bgs/5_bortevoy_med.svg";
import koshkarBackground from "../assets/prizes/1/bgs/6_koshkar.svg";
import horse from "../assets/prizes/2/bashkortostan_pets/1_bashkir_horse.svg";
import akbuzat from "../assets/prizes/2/bashkortostan_pets/2_akbuzat.svg";
import bee from "../assets/prizes/2/bashkortostan_pets/3_burzyan_bee.svg";
import eagle from "../assets/prizes/2/bashkortostan_pets/4_berkut.svg";
import bear from "../assets/prizes/2/bashkortostan_pets/5_brown_bear.svg";
import wolf from "../assets/prizes/2/bashkortostan_pets/6_wolf.svg";
import tubetey from "../assets/prizes/3/items/1_tubetey.svg";
import kurai from "../assets/prizes/3/items/2_kurai.svg";
import kubyz from "../assets/prizes/3/items/3_kubyz.svg";
import saba from "../assets/prizes/3/items/4_saba.svg";
import ichigi from "../assets/prizes/3/items/5_ichigi.svg";
import chakChak from "../assets/prizes/3/items/6_chak_chak.svg";

// The nested asset folders are canonical; the loose SVGs are duplicate exports.
// Descriptions are adapted from the supplied texts into complete collection cards.
type Prize = Omit<Decoration, "price" | "source">;
const prizes: Prize[] = [
  {
    id: "prize_bg_kurai",
    type: "background",
    title: "Курай",
    rarity: "rare",
    previewUrl: "🎶",
    imageUrl: kuraiBackground,
    description:
      "Курай — башкирский духовой инструмент из стебля одноимённого растения. Его мелодии сопровождают песни и сказания. На этом фоне музыка встречается с мягкими красками уральских склонов.",
  },
  {
    id: "prize_bg_yurt",
    type: "background",
    title: "Юрта в степи",
    rarity: "common",
    previewUrl: "⛺",
    imageUrl: yurtBackground,
    description:
      "Юрта, или тирмә, — традиционное переносное жилище башкир. Войлок сохранял тепло, а разборный каркас помогал переезжать вслед за стадами. Степной пейзаж напоминает о кочевом укладе и домашнем гостеприимстве.",
  },
  {
    id: "prize_bg_agidel",
    type: "background",
    title: "Агидель",
    rarity: "common",
    previewUrl: "🌊",
    imageUrl: agidelBackground,
    description:
      "Агидель — башкирское название реки Белой. Её имя означает «белая река» и звучит в песнях и легендах края. Спокойные изгибы воды превращают профиль в маленький речной пейзаж.",
  },
  {
    id: "prize_bg_shulgan_tash",
    type: "background",
    title: "Шульган-Таш",
    rarity: "epic",
    previewUrl: "🦣",
    imageUrl: shulganTashBackground,
    description:
      "Шульган-Таш, известная также как Капова пещера, находится в Бурзянском районе. В ней сохранились древние изображения мамонтов, носорогов и лошадей. Фон посвящён этой памяти, оставленной на камне.",
  },
  {
    id: "prize_bg_honey",
    type: "background",
    title: "Бортевой мёд",
    rarity: "rare",
    previewUrl: "🍯",
    imageUrl: honeyBackground,
    description:
      "Бортничество — традиционное лесное пчеловодство: пчёл содержат в бортях, дуплах деревьев. Башкирский мёд связывает мастерство человека с жизнью леса. Золотые соты на фоне напоминают об этом ремесле.",
  },
  {
    id: "prize_bg_koshkar",
    type: "background",
    title: "Ҡусҡар · узор рогов",
    rarity: "legendary",
    previewUrl: "🌀",
    imageUrl: koshkarBackground,
    description:
      "Ҡусҡар — орнаментальный мотив с парными завитками, напоминающими бараньи рога. Такой рисунок можно встретить в традиционном декоративном искусстве. Здесь он стал центральным украшением профиля.",
  },
  {
    id: "prize_pet_horse",
    type: "pet",
    title: "Башкирская лошадь",
    rarity: "common",
    previewUrl: "🐎",
    imageUrl: horse,
    petName: "Ат",
    description:
      "Лошадь — давний спутник башкира в дороге и хозяйстве. Башкирская порода славится выносливостью. Поговорка «Ат — ир канаты» сравнивает коня с крыльями мужчины — рядом с тобой тоже будет свой верный спутник.",
  },
  {
    id: "prize_pet_akbuzat",
    type: "pet",
    title: "Акбузат",
    rarity: "legendary",
    previewUrl: "🪽",
    imageUrl: akbuzat,
    petName: "Аҡбуҙат",
    description:
      "Акбузат — волшебный крылатый конь башкирского эпоса. Этот образ связан со свободой, верностью и силой. Легендарный спутник переносит частицу мира сказаний в твой профиль.",
  },
  {
    id: "prize_pet_bee",
    type: "pet",
    title: "Бурзянская пчела",
    rarity: "rare",
    previewUrl: "🐝",
    imageUrl: bee,
    petName: "Бал ҡорто",
    description:
      "Бурзянская пчела — тёмная лесная пчела, связанная с традицией бортничества. Она напоминает о лесах, заботе о природе и башкирском мёде. Маленькая труженица составит компанию в языковом путешествии.",
  },
  {
    id: "prize_pet_eagle",
    type: "pet",
    title: "Беркут",
    rarity: "epic",
    previewUrl: "🦅",
    imageUrl: eagle,
    petName: "Бөркөт",
    description:
      "Беркут — крупная хищная птица с зорким взглядом и широкими крыльями. Образ гордой птицы связан с храбростью и свободой. В коллекции он напоминает о просторах и природе Южного Урала.",
  },
  {
    id: "prize_pet_bear",
    type: "pet",
    title: "Бурый медведь",
    rarity: "common",
    previewUrl: "🐻",
    imageUrl: bear,
    petName: "Айыу",
    description:
      "Бурый медведь — обитатель уральских лесов. Его башкирское название — «айыу». Этот спокойный лесной спутник поможет запомнить новое слово и напомнит о богатой природе Башкортостана.",
  },
  {
    id: "prize_pet_wolf",
    type: "pet",
    title: "Волк",
    rarity: "rare",
    previewUrl: "🐺",
    imageUrl: wolf,
    petName: "Бүре",
    description:
      "Волк, по-башкирски «бүре», встречается в сказках и преданиях. В коллекции он олицетворяет смелость и самостоятельность. Позови его в профиль и запомни ещё одно башкирское название животного.",
  },
  {
    id: "prize_item_tubetey",
    type: "item",
    title: "Тюбетей",
    rarity: "common",
    previewUrl: "🧢",
    imageUrl: tubetey,
    description:
      "Тюбетей, или түбәтәй, — традиционная шапочка с вышивкой. Бархат и узоры из золотистых или серебристых нитей превращали её в нарядную деталь костюма. Коллекционная миниатюра украсит фон твоего профиля.",
  },
  {
    id: "prize_item_kurai",
    type: "item",
    title: "Курай",
    rarity: "rare",
    previewUrl: "🎶",
    imageUrl: kurai,
    description:
      "Курай — открытая продольная флейта из стебля одноимённого растения. На нём играют песни и мелодии, сопровождающие сказания. Один из главных символов башкирской музыкальной культуры теперь есть и в твоей коллекции.",
  },
  {
    id: "prize_item_kubyz",
    type: "item",
    title: "Кубыз",
    rarity: "rare",
    previewUrl: "🎵",
    imageUrl: kubyz,
    description:
      "Кубыз — небольшой язычковый музыкальный инструмент, разновидность варгана. Исполнитель меняет его звучание с помощью дыхания и положения рта. В необычном тембре можно услышать отголоски ветра и птичьих голосов.",
  },
  {
    id: "prize_item_saba",
    type: "item",
    title: "Саба",
    rarity: "epic",
    previewUrl: "🏺",
    imageUrl: saba,
    description:
      "Саба — кожаный сосуд для приготовления кумыса. Кобылье молоко в нём сбраживали и взбивали специальной мутовкой. Этот предмет знакомит с традициями быта и гостеприимства башкир.",
  },
  {
    id: "prize_item_ichigi",
    type: "item",
    title: "Ичиги",
    rarity: "common",
    previewUrl: "👢",
    imageUrl: ichigi,
    description:
      "Ичиги — мягкие кожаные сапоги. Их украшали цветными вставками и узорами, носили в повседневной жизни и по праздникам. Такая обувь напоминает о мастерстве кожевников и традиционном костюме.",
  },
  {
    id: "prize_item_chak_chak",
    type: "item",
    title: "Чак-чак",
    rarity: "rare",
    previewUrl: "🍯",
    imageUrl: chakChak,
    description:
      "Чак-чак — праздничное угощение из обжаренного теста и мёда. Его готовят разные народы региона; в башкирской традиции он связан с местным мёдом, гостеприимством и общим праздничным столом.",
  },
];

export const culturalPrizes: Decoration[] = prizes.map((prize) => ({
  ...prize,
  price: 0,
  source: "wheel",
}));
