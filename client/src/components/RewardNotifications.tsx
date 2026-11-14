import { useState } from "react";
import { useUserStore } from "../store";
import { decorationCatalog } from "../mocks/customization";

const signed = (amount: number) => `${amount >= 0 ? "+" : ""}${amount}`;

export default function RewardNotifications() {
  const notices = useUserStore((state) => state.rewardNotices);
  const levels = useUserStore((state) => state.levelRewards);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!notices.length && !levels.length) return null;
  const coins = notices.reduce((sum, notice) => sum + notice.coins, 0);
  const xp = notices.reduce((sum, notice) => sum + notice.xp, 0);
  return <section className="surface reward-notifications" aria-label="Полученные награды">
    <div role="status" aria-live="polite">
      {notices.length > 0 && <p><strong>{notices.some((notice) => notice.coins < 0) ? "Результаты проверки переводов" : "Твой перевод подтверждён!"}</strong><br />{signed(coins)} монет · {signed(xp)} XP</p>}
      {levels.map((reward) => <p key={reward.level}><strong>Новый уровень {reward.level}!</strong><br />{reward.decorationId
        ? `Получен скин «${decorationCatalog.find((item) => item.id === reward.decorationId)?.title ?? reward.decorationId}»`
        : `+${reward.compensation} монет`}</p>)}
    </div>
    <button className="button button-soft" disabled={busy} onClick={async () => {
      setBusy(true); setError("");
      try { await useUserStore.getState().dismissRewards(); }
      catch { setError("Не удалось закрыть уведомление. Попробуй ещё раз."); }
      finally { setBusy(false); }
    }}>Понятно</button>
    {error && <p role="alert">{error}</p>}
  </section>;
}
