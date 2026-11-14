export function pluralWord(count: number, one: string, few: string, many: string) {
  const mod100 = Math.abs(count) % 100;
  const mod10 = mod100 % 10;
  return mod100 >= 11 && mod100 <= 14 ? many
    : mod10 === 1 ? one
      : mod10 >= 2 && mod10 <= 4 ? few : many;
}
export const plural = (count: number, one: string, few: string, many: string) =>
  `${count} ${pluralWord(count, one, few, many)}`;
