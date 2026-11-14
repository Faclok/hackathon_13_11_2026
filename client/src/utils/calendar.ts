export const schoolDay = () =>
  new Date().toLocaleDateString("en-CA", { timeZone: "Europe/Moscow" });

export const dayNumber = (date: string) =>
  Math.floor(Date.parse(`${date}T00:00:00Z`) / 86_400_000);
