import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  decorationCatalog,
  getCaseDrops,
  isDecorationEquipped,
  rewardCases,
} from "../mocks/customization";
import { useUserStore } from "../store";
import type { CaseOpening } from "../types";
import { DecorationPreview } from "./Personalization";
import { RarityBadge } from "./CustomizationUI";
import { Modal, useToast } from "./ui";

export default function CaseOpeningModal({
  opening,
  animate,
}: {
  opening: CaseOpening;
  animate: boolean;
}) {
  const user = useUserStore((state) => state.user)!;
  const dismiss = useUserStore((state) => state.dismissCaseOpening);
  const equip = useUserStore((state) => state.equipDecoration);
  const toast = useToast();
  const winner = decorationCatalog.find(
    (item) => item.id === opening.decorationId,
  )!;
  const caseItem = rewardCases.find((item) => item.id === opening.caseId)!;
  const [spinning, setSpinning] = useState(
    animate && !window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );
  const [started, setStarted] = useState(false);
  const [tape] = useState(() => {
    const pool = getCaseDrops(caseItem);
    return Array.from({ length: 36 }, (_, index) =>
      index === 30 ? winner : pool[Math.floor(Math.random() * pool.length)],
    );
  });
  useEffect(() => {
    if (!spinning) return;
    const start = window.setTimeout(() => setStarted(true), 60);
    const finish = window.setTimeout(() => setSpinning(false), 3650);
    return () => {
      window.clearTimeout(start);
      window.clearTimeout(finish);
    };
  }, [spinning]);
  const equipped = isDecorationEquipped(user, winner);
  return (
    <Modal
      title={spinning ? "Открываем кейс…" : "Твоя находка!"}
      onClose={dismiss}
      dismissible={!spinning}
      className="case-opening-modal"
    >
      <p className="opening-case-name">{caseItem.title}</p>
      {spinning ? (
        <>
          <div className="case-reel" aria-label="Кейс открывается">
            <div className="case-reel-pointer" />
            <div
              className="case-reel-track"
              style={{
                transform: `translateX(${started ? -(30 * 108 + 50) : -50}px)`,
              }}
            >
              {tape.map((item, index) => (
                <div
                  key={index}
                  className={`case-reel-card rarity-border-${item.rarity}`}
                >
                  <DecorationPreview item={item} user={user} />
                  <strong>{item.title}</strong>
                  <RarityBadge item={item} />
                </div>
              ))}
            </div>
          </div>
          <p className="opening-hint" role="status">
            Сейчас узнаем, что внутри
          </p>
        </>
      ) : (
        <div className="case-reward" data-decoration-id={winner.id}>
          <RarityBadge item={winner} />
          <DecorationPreview item={winner} user={user} />
          <h3>{winner.title}</h3>
          <p>{winner.description}</p>
          {opening.duplicate ? (
            <div className="duplicate-notice">
              Уже есть в коллекции · +{opening.compensation} монет
            </div>
          ) : (
            <div className="new-item-notice">Добавлено в твой инвентарь</div>
          )}
          <button
            className="button button-primary full-width"
            disabled={equipped}
            onClick={() => {
              equip(winner.id);
              dismiss();
              toast(`«${winner.title}» — теперь в твоём образе`);
            }}
          >
            {equipped
              ? "Уже надето ✓"
              : winner.type === "background"
                ? "Применить фон"
                : winner.type === "pet"
                  ? "Позвать питомца"
                  : "Надеть"}
          </button>
          <Link
            to="/inventory"
            className="button button-soft full-width"
            onClick={dismiss}
          >
            В инвентарь
          </Link>
        </div>
      )}
    </Modal>
  );
}
