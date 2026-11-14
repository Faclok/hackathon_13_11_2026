import { Link } from "react-router-dom";
import { useUserStore } from "../store";
import { rarities } from "../mocks/customization";
import { Icon } from "./ui";
import type { Decoration } from "../types";

export function RarityBadge({ item }: { item: Decoration }) {
  const rarity = item.rarity ?? "common";
  return (
    <span className={`rarity-label rarity-${rarity}`}>
      {item.isDefault ? "Базовый" : rarities[rarity].label}
    </span>
  );
}
export function EconomyBalance() {
  const user = useUserStore((state) => state.user)!;
  return (
    <div className="economy-balance" aria-label="Баланс">
      <div>
        <span className="currency-symbol" aria-hidden="true">
          ●
        </span>
        <span>
          <small>Монеты</small>
          <strong data-testid="coin-balance">{user.coins}</strong>
        </span>
      </div>
      <Link to="/wheel" aria-label={`Ключи к кейсам: ${user.caseKeys}`}>
        <Icon name="key" size={23} />
        <span>
          <small>Ключи</small>
          <strong data-testid="key-balance">{user.caseKeys}</strong>
        </span>
      </Link>
    </div>
  );
}
export function keyCount(quantity: number) {
  const suffix =
    quantity % 100 >= 11 && quantity % 100 <= 14
      ? "ключей"
      : quantity % 10 === 1
        ? "ключ"
        : quantity % 10 >= 2 && quantity % 10 <= 4
          ? "ключа"
          : "ключей";
  return `${quantity} ${suffix}`;
}
