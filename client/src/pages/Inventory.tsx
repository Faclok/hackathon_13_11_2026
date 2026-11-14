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
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [catalogError, setCatalogError] = useState("");
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
      setCatalogError(cause instanceof Error ? cause.message : "Не удалось загрузить инвентарь."),
    ).finally(() => setCatalogLoading(false));
  }, []);
  const owned = catalog.filter((item) =>
    user.ownedDecorationIds.includes(item.id),
  );
  const acquired = owned.filter((item) => !item.isDefault);
  const swords = user.swords ?? 0;
  const showSword = swords > 0 && (tab === "all" || tab === "item");
  const baseItems = owned.filter((item) => item.isDefault && (tab === "all" || item.type === tab));
  const items = acquired.filter((item) => tab === "all" || item.type === tab);
  const category = tabs.find(([value]) => value === tab)?.[1] ?? "Все";
  const renderCard = (item: Decoration) => {
    const equipped = isDecorationEquipped(user, item);
    const canRemove = equipped && !item.isDefault && item.type !== "background";
    const label = equipped
      ? canRemove ? item.type === "item" ? "Убрать с фона" : "Снять" : "Выбрано ✓"
      : item.type === "background" ? "Применить"
        : item.type === "pet" ? "Позвать"
          : item.type === "item" ? "На фон" : "Надеть";
    return <article className={`surface decoration-card inventory-card ${equipped ? "is-equipped" : ""}`} key={item.id} data-decoration-id={item.id}>
      <RarityBadge item={item} />
      <DecorationPreview item={item} user={user} />
      <h2>{item.title}</h2>
      <button className="item-info" onClick={() => setInfo(item)}>О предмете <span>↗</span></button>
      <button className={`button ${equipped ? "button-soft" : "button-primary"} full-width`}
        disabled={!!busyId || (equipped && !canRemove)}
        onClick={async () => {
          setBusyId(item.id);
          try {
            if (canRemove) { await unequip(item.id); toast("Предмет снят"); }
            else { await equip(item.id); toast(item.type === "item" ? "Предмет размещён на фоне" : "Образ обновлён"); }
          } catch (cause) {
            toast(cause instanceof Error ? cause.message : "Не удалось обновить образ.");
          } finally { setBusyId(null); }
        }}>{label}</button>
    </article>;
  };
  return (
    <>
      <PageTitle subtitle="Все твои находки — в одном месте">
        Инвентарь
      </PageTitle>
      <EconomyBalance />
      {error && <p role="alert">{error}</p>}
      {catalogError && <p role="alert">{catalogError}</p>}
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
      <Link className="inventory-cases-link" to="/cases">
        <span className="inventory-cases-icon">
          <Icon name="bag" size={23} />
        </span>
        <span>
          <strong>Хочется новых находок?</strong>
          <small>Открой кейс за ключ</small>
        </span>
        <Icon name="arrow" size={18} />
      </Link>
      {catalogLoading && <p className="loading-text" role="status">Загружаем инвентарь…</p>}
      {!catalogLoading && !catalogError && <><div className="inventory-tabs" aria-label="Тип предметов">
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
                ? acquired.length + (swords > 0 ? 1 : 0)
                : acquired.filter((item) => item.type === value).length + (value === "item" && swords > 0 ? 1 : 0)}
            </span>
          </button>
        ))}
      </div>
      <h2 className="inventory-section-title">Находки</h2>
      {items.length || showSword ? (
        <div className="shop-grid inventory-grid">
          {showSword && <article className="surface decoration-card inventory-card sword-card">
            <span className="rarity-badge">Награда за обучение</span>
            <div className="decoration-preview"><span className="sword-art" aria-hidden="true">⚔️</span><strong className="sword-stack">×{swords}</strong></div>
            <h2>Меч</h2>
            <p>Один Меч открывает одного Босса. Получи новый за каждые пять пройденных уровней в «Учить».</p>
            <Link className="button button-primary full-width" to="/boss">К Боссу</Link>
          </article>}
          {items.map(renderCard)}
        </div>
      ) : (
        <section className="surface inventory-empty">
          <Icon name="bag" size={42} />
          <h2>{tab === "all" ? "Находок пока нет" : `В категории «${category}» пока нет находок`}</h2>
          <p>Открывай кейсы, чтобы пополнить коллекцию.</p>
          <Link to="/cases" className="button button-primary">Открыть кейсы <Icon name="arrow" size={16} /></Link>
        </section>
      )}
      {baseItems.length > 0 && <section className="inventory-base-section">
        <h2 className="inventory-section-title">Базовое оформление</h2>
        <p>Доступно сразу и не считается находками.</p>
        <div className="shop-grid inventory-grid">{baseItems.map(renderCard)}</div>
      </section>}</>}
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
