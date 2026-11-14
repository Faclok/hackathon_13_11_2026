import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCases } from "../api";
import { useUserStore } from "../store";
import type { RewardCase } from "../types";
import {
  decorationCatalog,
  DUPLICATE_COMPENSATION,
  getCaseDrops,
  getCaseDropChance,
  KEY_PRICE,
  rarities,
} from "../mocks/customization";
import {
  DecorationArtwork,
  DecorationPreview,
} from "../components/Personalization";
import {
  EconomyBalance,
  keyCount,
  RarityBadge,
} from "../components/CustomizationUI";
import CaseOpeningModal from "../components/CaseOpeningModal";
import { Icon, Modal, PageTitle, useToast } from "../components/ui";
import "../customization.css";

export default function Cases() {
  const user = useUserStore((state) => state.user)!;
  const opening = useUserStore((state) => state.caseOpening);
  const history = useUserStore((state) => state.caseHistory);
  const buyKeys = useUserStore((state) => state.buyCaseKeys);
  const openCase = useUserStore((state) => state.openCase);
  const [cases, setCases] = useState<RewardCase[]>([]);
  const [quantity, setQuantity] = useState(1);
  const [contents, setContents] = useState<RewardCase | null>(null);
  const [animatedId, setAnimatedId] = useState<string | null>(null);
  const toast = useToast();
  useEffect(() => {
    getCases().then(setCases);
  }, []);
  return (
    <>
      <PageTitle subtitle="Ключ к маленькому открытию">
        Барабан · испытай удачу
      </PageTitle>
      <EconomyBalance />
      <section className="surface keys-card">
        <span className="keys-card-icon">
          <Icon name="key" size={29} />
        </span>
        <div className="keys-card-copy">
          <h2>Ключи к кейсам</h2>
          <p>
            Один ключ открывает любой кейс.
            <br />
            Украшение останется с тобой.
          </p>
        </div>
        <div className="key-quantities" aria-label="Количество ключей">
          {[1, 5, 10].map((value) => (
            <button
              key={value}
              aria-pressed={quantity === value}
              className={quantity === value ? "active" : ""}
              onClick={() => setQuantity(value)}
            >
              {keyCount(value)}
            </button>
          ))}
        </div>
        <button
          className="button button-primary full-width"
          disabled={user.coins < quantity * KEY_PRICE}
          onClick={() => {
            if (buyKeys(quantity))
              toast(`В твоём инвентаре +${keyCount(quantity)}`);
          }}
        >
          {user.coins < quantity * KEY_PRICE
            ? "Недостаточно монет"
            : `Купить ${keyCount(quantity)} · ${quantity * KEY_PRICE} монет`}
        </button>
        <p className="key-price-note">1 ключ — {KEY_PRICE} монет</p>
      </section>
      <div className="section-caption cases-caption">
        <h2>Выбери свой кейс</h2>
        <span>1 открытие = 1 ключ</span>
      </div>
      <div className="reward-cases-grid">
        {cases.map((caseItem) => (
          <article
            className="surface reward-case-card"
            key={caseItem.id}
            data-case-id={caseItem.id}
          >
            <div className="case-image-wrap">
              <img src={caseItem.imageUrl} alt={`Кейс «${caseItem.title}»`} />
            </div>
            <h2>{caseItem.title}</h2>
            <p>{caseItem.description}</p>
            <div
              className="case-rarity-dots"
              aria-label="Редкости в этом кейсе"
            >
              {Object.keys(rarities)
                .filter((rarity) =>
                  getCaseDrops(caseItem).some(
                    (item) => (item.rarity ?? "common") === rarity,
                  ),
                )
                .map((rarity) => (
                  <span
                    key={rarity}
                    className={`rarity-dot rarity-${rarity}`}
                  />
                ))}
            </div>
            <button
              className="case-contents-button"
              onClick={() => setContents(caseItem)}
            >
              Что внутри <Icon name="arrow" size={12} />
            </button>
            <button
              className="button button-primary full-width"
              disabled={user.caseKeys < 1 || !!opening}
              onClick={() => {
                const result = openCase(caseItem.id);
                if (result) setAnimatedId(result.id);
              }}
            >
              {user.caseKeys < 1 ? "Нужен ключ" : "Открыть кейс"}
            </button>
          </article>
        ))}
      </div>
      <p className="case-duplicate-note">
        Повторный предмет принесёт {DUPLICATE_COMPENSATION} монет.
      </p>
      <Link to="/inventory" className="button button-soft full-width">
        Мой инвентарь <Icon name="bag" size={18} />
      </Link>
      {history.length > 0 && (
        <section className="surface case-history">
          <h2>Последние находки</h2>
          {history.slice(0, 3).map((entry) => {
            const item = decorationCatalog.find(
              (item) => item.id === entry.decorationId,
            )!;
            return (
              <div key={entry.id}>
                <span className="history-item-icon">
                  <DecorationArtwork item={item} />
                </span>
                <span>
                  <strong>{item.title}</strong>
                  <small>
                    {entry.duplicate
                      ? `Повтор · +${entry.compensation} монет`
                      : "В твоей коллекции"}
                  </small>
                </span>
                <RarityBadge item={item} />
              </div>
            );
          })}
        </section>
      )}
      {contents && (
        <Modal title={contents.title} onClose={() => setContents(null)}>
          <p className="case-contents-intro">
            Один из этих предметов попадёт в твой инвентарь.
          </p>
          <div className="case-drop-list">
            {getCaseDrops(contents).map((item) => (
              <div key={item.id}>
                <DecorationPreview item={item} user={user} />
                <span>
                  <strong>{item.title}</strong>
                  <RarityBadge item={item} />
                </span>
                <b>
                  {getCaseDropChance(contents, item).toLocaleString("ru-RU", {
                    maximumFractionDigits: 2,
                  })}
                  %
                </b>
              </div>
            ))}
          </div>
        </Modal>
      )}
      {opening && (
        <CaseOpeningModal
          key={opening.id}
          opening={opening}
          animate={animatedId === opening.id}
        />
      )}
    </>
  );
}
