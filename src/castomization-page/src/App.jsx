import { useEffect, useState } from "react";
import { CASES, DEFAULT_EQUIPPED, ITEMS, RARITIES, WARDROBE_TABS } from "./data.js";

const STORAGE_KEY = "profile-customization-v2";

function createDefaultState() {
  return {
    coins: 100,
    unlocked: ITEMS.filter((item) => item.isDefault).map((item) => item.id),
    equipped: { ...DEFAULT_EQUIPPED },
  };
}

function loadState() {
  const defaults = createDefaultState();

  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (!stored) {
      const legacyCoins = localStorage.getItem("profile-customization-coins");
      const legacyBalance = Number(legacyCoins);
      if (legacyCoins !== null && Number.isInteger(legacyBalance) && legacyBalance >= 0) {
        return { ...defaults, coins: legacyBalance };
      }
      return defaults;
    }

    const parsed = JSON.parse(stored);
    const validIds = new Set(ITEMS.map((item) => item.id));
    const unlocked = Array.isArray(parsed.unlocked)
      ? [...new Set(parsed.unlocked.filter((id) => validIds.has(id)))]
      : defaults.unlocked;

    defaults.unlocked.forEach((id) => {
      if (!unlocked.includes(id)) unlocked.push(id);
    });

    const equipped = { ...DEFAULT_EQUIPPED };
    Object.keys(DEFAULT_EQUIPPED).forEach((type) => {
      const selected = ITEMS.find(
        (item) => item.id === parsed.equipped?.[type] && item.type === type,
      );
      if (selected && unlocked.includes(selected.id)) equipped[type] = selected.id;
    });

    return {
      coins: Number.isInteger(parsed.coins) && parsed.coins >= 0 ? parsed.coins : defaults.coins,
      unlocked,
      equipped,
    };
  } catch (error) {
    console.error("Не удалось загрузить сохранение кастомизации.", error);
    return defaults;
  }
}

function getItem(id) {
  return ITEMS.find((item) => item.id === id);
}

function getDrops(type) {
  return ITEMS.filter((item) => item.type === type && !item.isDefault);
}

function rollItem(type) {
  const pool = getDrops(type);
  const totalWeight = pool.reduce((sum, item) => sum + RARITIES[item.rarity].weight, 0);
  let randomWeight = Math.random() * totalWeight;

  for (const item of pool) {
    randomWeight -= RARITIES[item.rarity].weight;
    if (randomWeight < 0) return item;
  }

  return pool[pool.length - 1];
}

function AppleAvatar() {
  return (
    <svg viewBox="0 0 64 64" aria-hidden="true">
      <path d="M32 16c0-5 3-8 6-9" fill="none" stroke="#5d4037" strokeWidth="3.5" strokeLinecap="round" />
      <path d="M32 15c-6-5-13-3-14 2 5 2 11 1 14-2Z" fill="#2e7d32" />
      <path d="M32 19c-8-3-18 2-18 14s8 21 18 21 18-9 18-21-10-17-18-14Z" fill="#8bc34a" />
      <path d="M21 25c-2 3-2 8-1 11" fill="none" stroke="#dcedc8" strokeWidth="3" strokeLinecap="round" />
      <circle cx="28" cy="34" r="1.8" fill="#1b5e20" />
      <circle cx="36" cy="34" r="1.8" fill="#1b5e20" />
      <path d="M30 38q2 2 4 0" fill="none" stroke="#1b5e20" strokeWidth="1.6" strokeLinecap="round" />
    </svg>
  );
}

function HatOverlay({ id }) {
  const hats = {
    hat_tubeteika: (
      <svg viewBox="0 0 80 42" aria-hidden="true">
        <path d="M14 34C14 16 25 8 40 8s26 8 26 26H14Z" fill="#1b5e20" stroke="#fbbf24" strokeWidth="2.5" />
        <path d="M14 31h52" stroke="#fbbf24" strokeWidth="3" strokeDasharray="4 3" />
        <path d="m40 13 5 7-5 7-5-7 5-7Z" fill="#fbbf24" />
      </svg>
    ),
    hat_cap: (
      <svg viewBox="0 0 90 48" aria-hidden="true">
        <path d="m45 4 37 14-37 12L8 18 45 4Z" fill="#1e293b" stroke="#334155" strokeWidth="1.5" />
        <path d="M24 23v11c0 4 42 4 42 0V23" fill="#0f172a" />
        <path d="M74 20v15" stroke="#fbbf24" strokeWidth="2.5" strokeLinecap="round" />
        <circle cx="74" cy="36" r="2.5" fill="#fbbf24" />
      </svg>
    ),
    hat_burek: (
      <svg viewBox="0 0 90 48" aria-hidden="true">
        <path d="M20 27C20 12 30 6 45 6s25 6 25 21H20Z" fill="#991b1b" />
        <rect x="10" y="22" width="70" height="18" rx="9" fill="#78350f" stroke="#451a03" strokeWidth="2" />
        <path d="M18 28c10-2 44-2 54 0" stroke="#92400e" strokeWidth="2" strokeLinecap="round" />
      </svg>
    ),
    hat_crown: (
      <svg viewBox="0 0 84 46" aria-hidden="true">
        <path d="m12 36-4-22 19 11L42 8l15 17 19-11-4 22H12Z" fill="#fbbf24" stroke="#b45309" strokeWidth="2.2" strokeLinejoin="round" />
        <circle cx="42" cy="24" r="3" fill="#ef4444" />
        <circle cx="24" cy="28" r="2.2" fill="#10b981" />
        <circle cx="60" cy="28" r="2.2" fill="#3b82f6" />
      </svg>
    ),
  };
  return hats[id] || null;
}

function BackgroundOrnament() {
  return (
    <svg className="page-ornament" viewBox="0 0 400 400" aria-hidden="true">
      <g transform="translate(220,-40) rotate(45)">
        <rect x="0" y="0" width="140" height="140" />
        <rect x="45" y="45" width="50" height="50" />
        <rect x="160" y="0" width="140" height="140" />
        <rect x="205" y="45" width="50" height="50" />
        <rect x="0" y="160" width="140" height="140" />
        <rect x="45" y="205" width="50" height="50" />
        <rect x="160" y="160" width="140" height="140" />
      </g>
    </svg>
  );
}

function ItemAppearance({ item, size = "wardrobe" }) {
  const className = `item-appearance is-${size}`;

  if (item.type === "hat") {
    return (
      <span className={`${className} appearance-hat`} aria-hidden="true">
        <span className="appearance-avatar">
          <span className="apple-avatar"><AppleAvatar /></span>
          {item.id !== "hat_none" && (
            <span className="appearance-hat-overlay"><HatOverlay id={item.id} /></span>
          )}
        </span>
      </span>
    );
  }

  if (item.type === "frame") {
    return (
      <span className={`${className} appearance-frame`} aria-hidden="true">
        <span className={`appearance-frame-ring ${item.id}`}>
          <span className="apple-avatar"><AppleAvatar /></span>
        </span>
      </span>
    );
  }

  if (item.type === "bg") {
    return (
      <span className={`${className} appearance-background`} aria-hidden="true">
        <span className="appearance-background-art" style={{ background: item.background }}>
          <span className="appearance-background-ornament" />
        </span>
      </span>
    );
  }

  return (
    <span className={`${className} appearance-pet`} aria-hidden="true">
      <span className="pet-bubble">{item.emoji}</span>
      {item.petName && <span className="pet-name">{item.petName}</span>}
    </span>
  );
}

function CaseCard({ caseItem, coins, onOpen }) {
  const drops = getDrops(caseItem.type);
  const canAfford = coins >= 10;

  return (
    <article className="case-card">
      <div>
        <div className="case-topline">
          <div className={`case-icon ${caseItem.type} case-icon-image`} aria-hidden="true">
            <img
              className="case-image"
              src={{
                hat: "/cases/hats-case.png",
                frame: "/cases/frames-case.png",
                bg: "/cases/backgrounds-case.png",
                pet: "/cases/pets-case.png",
              }[caseItem.type]}
              alt=""
            />
          </div>
        </div>
        <div className="case-intro">
          <h3>{caseItem.title}</h3>
          <p className="case-subtitle">{caseItem.subtitle}</p>
        </div>
        <p className="drop-heading">Возможный дроп</p>
        <div className="drop-list">
          {drops.map((item) => (
            <div className="drop-row" key={item.id}>
              <ItemAppearance item={item} size="inline" />
              <span className="drop-name" title={item.name}>{item.name}</span>
              <span className={`rarity-badge rarity-${item.rarity}`}>{RARITIES[item.rarity].label}</span>
            </div>
          ))}
        </div>
      </div>
      <button
        className="open-case-button"
        type="button"
        disabled={!canAfford}
        onClick={() => onOpen(caseItem)}
      >
        {canAfford ? "Открыть за 10 🪙　→" : "Недостаточно монет"}
      </button>
    </article>
  );
}

function WardrobeItem({ item, isUnlocked, isEquipped, onEquip }) {
  return (
    <button
      className={`wardrobe-card ${isUnlocked ? "is-unlocked" : "is-locked"} ${isEquipped ? "is-equipped" : ""}`}
      type="button"
      disabled={!isUnlocked}
      onClick={() => onEquip(item)}
      aria-label={`${item.name}${isEquipped ? ", надето" : isUnlocked ? ", надеть" : ", закрыто"}`}
    >
      <span className="wardrobe-card-top">
        <span className={`rarity-badge rarity-${item.rarity}`}>{RARITIES[item.rarity].label}</span>
        {isEquipped && <span className="wardrobe-status">✓ Надето</span>}
        {!isUnlocked && <span className="wardrobe-lock" aria-label="Закрыто">🔒</span>}
      </span>
      <span className="item-art"><ItemAppearance item={item} size="wardrobe" /></span>
      <span className="wardrobe-item-name">{item.name}</span>
      <span className="wardrobe-description">{isUnlocked ? item.description : "Выпадает из кейса"}</span>
    </button>
  );
}

function RouletteCard({ item }) {
  return (
    <div className={`roulette-card rarity-${item.rarity}`}>
      <ItemAppearance item={item} size="roulette" />
      <span className="roulette-title">{item.name}</span>
      <span className={`rarity-badge rarity-${item.rarity}`}>{RARITIES[item.rarity].label}</span>
    </div>
  );
}

function RewardVisual({ item }) {
  return <ItemAppearance item={item} size="reward" />;
}

export default function App() {
  const [state, setState] = useState(loadState);
  const [activeTab, setActiveTab] = useState("hat");
  const [toast, setToast] = useState(null);
  const [caseModal, setCaseModal] = useState(null);
  const [rouletteStarted, setRouletteStarted] = useState(false);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch (error) {
      console.error("Не удалось сохранить кастомизацию.", error);
    }
  }, [state]);

  useEffect(() => {
    if (!toast) return undefined;
    const timeout = window.setTimeout(() => setToast(null), 3000);
    return () => window.clearTimeout(timeout);
  }, [toast]);

  useEffect(() => {
    if (caseModal?.phase !== "spinning") return undefined;

    const frame = window.requestAnimationFrame(() => setRouletteStarted(true));
    const timeout = window.setTimeout(() => {
      setState((previous) => ({
        ...previous,
        coins: previous.coins + (caseModal.duplicate ? 5 : 0),
        unlocked: caseModal.duplicate || previous.unlocked.includes(caseModal.winner.id)
          ? previous.unlocked
          : [...previous.unlocked, caseModal.winner.id],
      }));
      setCaseModal((previous) => previous ? { ...previous, phase: "revealed" } : previous);
    }, 3650);

    return () => {
      window.cancelAnimationFrame(frame);
      window.clearTimeout(timeout);
    };
  }, [caseModal?.id, caseModal?.phase]);

  const currentHat = getItem(state.equipped.hat);
  const currentFrame = getItem(state.equipped.frame);
  const currentBackground = getItem(state.equipped.bg);
  const currentPet = getItem(state.equipped.pet);
  const filteredItems = ITEMS.filter(
    (item) => item.type === activeTab && state.unlocked.includes(item.id),
  );

  function showToast(message, type = "success") {
    setToast({ message, type, id: Date.now() });
  }

  function handleEquip(item) {
    setState((previous) => ({
      ...previous,
      equipped: {
        ...previous.equipped,
        [item.type]: previous.equipped[item.type] === item.id
          ? DEFAULT_EQUIPPED[item.type]
          : item.id,
      },
    }));
  }

  function handleOpenCase(caseItem) {
    if (state.coins < 10) {
      showToast("Недостаточно монет", "warning");
      return;
    }

    const winner = rollItem(caseItem.type);
    const duplicate = state.unlocked.includes(winner.id);
    const pool = getDrops(caseItem.type);
    const winnerIndex = 30;
    const tape = Array.from({ length: 36 }, (_, index) => (
      index === winnerIndex ? winner : pool[Math.floor(Math.random() * pool.length)]
    ));

    setState((previous) => ({ ...previous, coins: previous.coins - 10 }));
    setRouletteStarted(false);
    setCaseModal({ id: caseItem.id, caseItem, tape, winner, duplicate, phase: "spinning" });
  }

  function closeModal() {
    if (caseModal?.phase === "spinning") return;
    setCaseModal(null);
    setRouletteStarted(false);
  }

  function handleEquipReward() {
    if (!caseModal?.winner) return;
    setState((previous) => ({
      ...previous,
      equipped: { ...previous.equipped, [caseModal.winner.type]: caseModal.winner.id },
    }));
    const name = caseModal.winner.name;
    closeModal();
    showToast(`Надето: ${name}`);
  }

  function handleModalKeyDown(event) {
    if (event.key === "Escape" && caseModal?.phase === "revealed") closeModal();
  }

  const rouletteCardWidth = window.matchMedia("(max-width: 520px)").matches ? 107 : 120;
  const rouletteCardCenter = window.matchMedia("(max-width: 520px)").matches ? 50 : 56;
  const rouletteOffset = -(30 * rouletteCardWidth + rouletteCardCenter);

  return (
    <>
      <BackgroundOrnament />
      <div className="page-shell">
        <header className="topbar">
          <div className="topbar-left">
            <a className="brand" href="#" aria-label="Башкирская языковая школа — главная">
              <svg className="brand-mark" viewBox="0 0 48 50" fill="none" aria-hidden="true">
                <path d="M24 2 34 12 24 22 14 12 24 2Z" stroke="currentColor" strokeWidth="3" />
                <path d="m12 14 8 8-8 8-8-8 8-8Zm24 0 8 8-8 8-8-8 8-8Zm-12 8 7 7-7 7-7-7 7-7Z" stroke="currentColor" strokeWidth="3" />
                <path d="M8 36c6 0 12 2 16 6 4-4 10-6 16-6v5c-6 0-12 2-16 6-4-4-10-6-16-6v-5Z" fill="#65b741" />
              </svg>
              <span className="brand-name">Башкирская<br />языковая<br />школа</span>
            </a>
            <a className="home-link" href="#" onClick={(event) => { event.preventDefault(); showToast("Переход на главную страницу школы"); }}>
              <span aria-hidden="true">←</span> На главную
            </a>
          </div>
          <div className="topbar-right">
            <div className="coin-balance">
              <strong>{state.coins} 🪙</strong>
              <button
                className="add-coins-button"
                type="button"
                title="Добавить тестовые монеты"
                onClick={() => {
                  setState((previous) => ({ ...previous, coins: previous.coins + 50 }));
                  showToast("Начислено +50 тестовых монет!");
                }}
              >
                ＋ <span>50 🪙 (тест)</span>
              </button>
            </div>
            <div className={`mini-avatar ${currentFrame.id}`} aria-hidden="true"><div className="apple-avatar"><AppleAvatar /></div></div>
            <span className="profile-name">Name</span>
          </div>
        </header>

        <main>
          <section className="preview-section" aria-labelledby="page-title">
            <div className="section-title-row">
              <h1 id="page-title">Кастомизация профиля</h1>
            </div>
            <div className="profile-preview" style={{ background: currentBackground.background }}>
              <svg className="preview-ornament" viewBox="0 0 200 200" aria-hidden="true">
                <rect x="100" y="10" width="120" height="120" transform="rotate(45 100 10)" />
                <rect x="100" y="45" width="70" height="70" transform="rotate(45 100 45)" />
              </svg>
              <div className="preview-content">
                <div className="preview-person">
                  <div className="avatar-group">
                    <div className="avatar-stack">
                      <div className="hat-overlay"><HatOverlay id={currentHat.id} /></div>
                      <div className={`large-avatar-ring ${currentFrame.id}`}><div className="apple-avatar"><AppleAvatar /></div></div>
                    </div>
                    {currentPet.id !== "pet_none" && (
                      <div className="preview-pet">
                        <span className="preview-pet-character" aria-hidden="true">{currentPet.emoji}</span>
                        <span className="pet-name">{currentPet.petName}</span>
                      </div>
                    )}
                  </div>
                  <div className="preview-copy">
                    <span className="preview-label"><span aria-hidden="true">✦</span> Предпросмотр образа</span>
                    <h2>Привет, Name!</h2>
                    <p>Открывай тематические кейсы за монеты, собирай редкие украшения и создай свой уникальный стиль ученика.</p>
                    <div className="equipped-slots">
                      <span className="equipped-slot">Убор: <strong>{currentHat.name}</strong></span>
                      <span className="equipped-slot">Рамка: <strong>{currentFrame.name}</strong></span>
                      <span className="equipped-slot">Фон: <strong>{currentBackground.name}</strong></span>
                      <span className="equipped-slot">Питомец: <strong>{currentPet.name}</strong></span>
                    </div>
                  </div>
                </div>
                <button className="reset-button" type="button" onClick={() => {
                  setState((previous) => ({ ...previous, equipped: { ...DEFAULT_EQUIPPED } }));
                  showToast("Образ сброшен к стандартному");
                }}>
                  ↻ <span>Сбросить образ</span>
                </button>
              </div>
            </div>
          </section>

          <section className="cases-section" aria-labelledby="cases-heading">
            <div className="section-heading">
              <h2 id="cases-heading">Кейсы с наградами</h2>
              <p>Испытай удачу! Каждый кейс стоит 10 монет и содержит предметы разной редкости</p>
            </div>
            <div className="case-grid">
              {CASES.map((caseItem) => (
                <CaseCard key={caseItem.id} caseItem={caseItem} coins={state.coins} onOpen={handleOpenCase} />
              ))}
            </div>
          </section>

          <section className="wardrobe-section" aria-labelledby="wardrobe-heading">
            <div className="wardrobe-heading">
              <div>
                <h2 id="wardrobe-heading">Мой гардероб</h2>
                <p>Нажми на открытый предмет, чтобы надеть или снять его с профиля</p>
              </div>
              <div className="wardrobe-tabs" role="group" aria-label="Фильтр гардероба">
                {WARDROBE_TABS.map((tab) => (
                  <button
                    className={`wardrobe-tab ${activeTab === tab.id ? "is-active" : ""}`}
                    type="button"
                    key={tab.id}
                    aria-pressed={activeTab === tab.id}
                    onClick={() => setActiveTab(tab.id)}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            </div>
            <div className="wardrobe-grid">
              {filteredItems.map((item) => (
                <WardrobeItem
                  key={item.id}
                  item={item}
                  isUnlocked={state.unlocked.includes(item.id)}
                  isEquipped={state.equipped[item.type] === item.id}
                  onEquip={handleEquip}
                />
              ))}
            </div>
          </section>
        </main>
        <footer className="page-footer">Учись, играй и создавай свой стиль</footer>
      </div>

      {toast && (
        <div key={toast.id} className={`toast is-visible${toast.type === "warning" ? " is-warning" : ""}`} role="status" aria-live="polite">
          {toast.message}
        </div>
      )}

      {caseModal && (
        <div
          className="case-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="modal-heading"
          onKeyDown={handleModalKeyDown}
          onClick={(event) => {
            if (event.target === event.currentTarget && caseModal.phase === "revealed") closeModal();
          }}
        >
          <div className="modal-card">
            {caseModal.phase === "revealed" && (
              <button className="modal-close" type="button" onClick={closeModal} aria-label="Закрыть">×</button>
            )}
            <div className="modal-heading">
              <span className="modal-case-label">{caseModal.caseItem.title}</span>
              <h2 id="modal-heading">{caseModal.phase === "spinning" ? "Открываем кейс..." : "Поздравляем!"}</h2>
            </div>

            {caseModal.phase === "spinning" ? (
              <div className="roulette">
                <div className="roulette-pointer" aria-hidden="true" />
                <div
                  className={`roulette-track ${rouletteStarted ? "is-spinning" : ""}`}
                  style={{ transform: `translateX(${rouletteStarted ? rouletteOffset : 0}px)` }}
                >
                  {caseModal.tape.map((item, index) => <RouletteCard key={`${item.id}-${index}`} item={item} />)}
                </div>
              </div>
            ) : (
              <div className="reward-panel">
                <div className="reward-card">
                  <span className={`rarity-badge rarity-${caseModal.winner.rarity}`}>{RARITIES[caseModal.winner.rarity].label}</span>
                  <RewardVisual item={caseModal.winner} />
                  <h3>{caseModal.winner.name}</h3>
                  <p>{caseModal.winner.description}</p>
                  {caseModal.duplicate && <span className="duplicate-notice">🪙 Дубликат! Возвращено +5 монет</span>}
                </div>
                <div className="reward-actions">
                  <button className="reward-action reward-action-equip" type="button" onClick={handleEquipReward}>
                    ✓ Надеть сразу
                  </button>
                  <button className="reward-action reward-action-close" type="button" onClick={closeModal}>В инвентарь</button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
}
