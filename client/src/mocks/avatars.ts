// Self-contained SVG portraits keep the demo independent of external image services.
const palettes = [
  ["#e6d6ff", "#f2c5a3", "#493529", "#627bd1"],
  ["#ffe5cc", "#e8b18e", "#332c29", "#b96b74"],
  ["#d9eee7", "#c98f6c", "#292c32", "#508677"],
  ["#f8dfec", "#f5cdb0", "#79553c", "#bb7298"],
  ["#dce9ff", "#e6ae85", "#453a35", "#6789bb"],
];

export function portraitAvatar(index: number) {
  const [background, skin, hair, shirt] = palettes[index % palettes.length];
  const longHair = index % 3 === 1;
  const glasses = index % 4 === 0;
  const fringe =
    index % 3 === 2
      ? "M18 27c-1-11 9-16 17-14 8-2 14 6 13 13l-7-3-2 4-6-4-4 4-4-5-7 5Z"
      : "M18 27c-1-11 9-16 17-14 8-2 14 6 13 13-8 1-14-3-17-7-3 4-7 7-13 8";
  const hairstyle = longHair
    ? `<path d="M17 37C14 15 25 9 35 10c15 1 18 14 15 30l-5 10H20Z" fill="${hair}"/>`
    : `<path d="M17 29c-2-13 9-21 20-18 12-2 18 10 13 22l-9-10-22 11Z" fill="${hair}"/>`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
    <rect width="64" height="64" rx="32" fill="${background}"/>
    <circle cx="51" cy="12" r="12" fill="white" opacity=".25"/>
    ${hairstyle}
    <path d="M9 64c1-14 9-21 23-21s22 7 23 21" fill="${shirt}"/>
    <path d="M26 41h12v9c-4 5-9 4-12 0" fill="${skin}"/>
    <ellipse cx="32" cy="31" rx="14" ry="17" fill="${skin}"/>
    <path d="${fringe}" fill="${hair}"/>
    <path d="m23 29 5-1m9 0 5 1" fill="none" stroke="${hair}" stroke-width="1.5" stroke-linecap="round"/>
    <circle cx="26" cy="33" r="1.3" fill="#34332f"/><circle cx="39" cy="33" r="1.3" fill="#34332f"/>
    <path d="m31 34-1 5h3" fill="none" stroke="#bd8a6a" stroke-width="1.1" stroke-linecap="round"/>
    <path d="M28 42q5 4 10-1" fill="none" stroke="#9e655b" stroke-width="1.4" stroke-linecap="round"/>
    ${glasses ? '<g fill="none" stroke="#4b4b63" stroke-width="1.3"><rect x="20" y="29" width="11" height="8" rx="3"/><rect x="34" y="29" width="11" height="8" rx="3"/><path d="M31 32h3"/></g>' : ""}
    ${longHair ? `<path d="M17 25q-4 17 4 25l4-3c-5-11-3-17-2-22m22-3c3 9 2 20-3 27l5 2c7-10 7-22 3-30" fill="${hair}"/>` : ""}
    <path d="m23 52 9 6 9-6" fill="none" stroke="white" stroke-width="2" opacity=".6"/>
  </svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
