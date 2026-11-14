import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getDecorations } from "../api";
import { useUserStore } from "../store";
import { displayName } from "../utils/displayName";
import type { Decoration } from "../types";
import { isDecorationEquipped } from "../mocks/customization";
import {
  Avatar,
  DecorationPreview,
  FloatingItems,
  ProfileBackground,
} from "../components/Personalization";
import { EconomyBalance, RarityBadge } from "../components/CustomizationUI";
import { Icon, Modal, PageTitle, useToast } from "../components/ui";
import "../customization.css";

const tabs = [
  ["all", "Все"],
  ["background", "Фоны"],
  ["frame", "Рамки"],
  ["hat", "Уборы"],
  ["pet", "Питомцы"],
  ["item", "Предметы"],
] as const;
export default function Inventory() {
  const user = useUserStore((state) => state.user)!;
  const equip = useUserStore((state) => state.equipDecoration);
  const unequip = useUserStore((state) => state.unequipDecoration);
  const [catalog, setCatalog] = useState<Decoration[]>([]);
  const [tab, setTab] = useState<string>("all");
  const [info, setInfo] = useState<Decoration | null>(null);
  const [error, setError] = useState("");
  const [busyId, setBusyId] = useState<string | null>(null);
  const toast = useToast();
  useEffect(() => {
    void useUserStore.getState().refreshProfile().catch((cause) =>
      setError(cause instanceof Error ? cause.message : "Не удалось обновить профиль."),
    );
    getDecorations().then(setCatalog).catch((cause) =>
      setError(cause instanceof Error ? cause.message : "Не удалось загрузить инвентарь."),
    );
  }, []);
  const owned = catalog.filter((item) =>
    user.ownedDecorationIds.includes(item.id),
  );
  const items = owned.filter((item) => tab === "all" || item.type === tab);
  return (
    <>
      <PageTitle subtitle="Все твои находки — в одном месте">
        Инвентарь
      </PageTitle>
      <EconomyBalance />
      {error && <p role="alert">{error}</p>}
      <section
        className="shop-preview personal-card inventory-preview"
        aria-label="Текущее оформление"
        data-background={user.backgroundId}
      >
        <ProfileBackground backgroundId={user.backgroundId} />
        <FloatingItems user={user} />
        <Avatar user={user} />
        <div className="personal-content">
          <strong>{displayName(user)}</strong>
          <p>Твой образ</p>
          <Link to="/leaderboard">
            Посмотреть в рейтинге <Icon name="arrow" size={12} />
          </Link>
        </div>
      </section>
      <Link className="inventory-cases-link" to="/wheel">
        <span className="inventory-cases-icon">
          <Icon name="wheel" size={23} />
        </span>
        <span>
          <strong>Хочется новых находок?</strong>
          <small>Открой кейс за ключ</small>
        </span>
        <Icon name="arrow" size={18} />
      </Link>
      <div className="inventory-tabs" aria-label="Тип предметов">
        {tabs.map(([value, label]) => (
          <button
            key={value}
            className={tab === value ? "active" : ""}
            aria-pressed={tab === value}
            onClick={() => setTab(value)}
          >
            {label}
            <span>
              {value === "all"
                ? owned.length
                : owned.filter((item) => item.type === value).length}
            </span>
          </button>
        ))}
      </div>
      {items.length ? (
        <div className="shop-grid inventory-grid">
          {items.map((item) => {
            const equipped = isDecorationEquipped(user, item);
            const canRemove =
              equipped && !item.isDefault && item.type !== "background";
            const label = equipped
              ? canRemove
                ? item.type === "item"
                  ? "Убрать с фона"
                  : "Снять"
                : "Выбрано ✓"
              : item.type === "background"
                ? "Применить"
                : item.type === "pet"
                  ? "Позвать"
                  : item.type === "item"
                    ? "На фон"
                    : "Надеть";
            return (
              <article
                className={`surface decoration-card inventory-card ${equipped ? "is-equipped" : ""}`}
                key={item.id}
                data-decoration-id={item.id}
              >
                <RarityBadge item={item} />
                <DecorationPreview item={item} user={user} />
                <h2>{item.title}</h2>
                <button className="item-info" onClick={() => setInfo(item)}>
                  О предмете <span>↗</span>
                </button>
                <button
                  className={`button ${equipped ? "button-soft" : "button-primary"} full-width`}
                  disabled={!!busyId || (equipped && !canRemove)}
                  onClick={async () => {
                    setBusyId(item.id);
                    try {
                      if (canRemove) {
                        await unequip(item.id);
                        toast("Предмет снят");
                      } else {
                        await equip(item.id);
                        toast(item.type === "item" ? "Предмет размещён на фоне" : "Образ обновлён");
                      }
                    } catch (cause) {
                      toast(cause instanceof Error ? cause.message : "Не удалось обновить образ.");
                    } finally { setBusyId(null); }
                  }}
                >
                  {label}
                </button>
              </article>
            );
          })}
        </div>
      ) : (
        <section className="surface inventory-empty">
          <Icon name="bag" size={42} />
          <h2>Здесь пока пусто</h2>
          <p>Открывай кейсы, чтобы пополнить коллекцию.</p>
          <Link to="/wheel" className="button button-primary">
            Испытать удачу <Icon name="arrow" size={16} />
          </Link>
        </section>
      )}
      {info && (
        <Modal title={info.title} onClose={() => setInfo(null)}>
          <div className="item-detail-preview">
            <DecorationPreview item={info} user={user} />
          </div>
          <RarityBadge item={info} />
          <p className="inventory-item-description">{info.description}</p>
          <button
            className="button button-soft full-width"
            onClick={() => setInfo(null)}
          >
            Понятно
          </button>
        </Modal>
      )}
    </>
  );
}
