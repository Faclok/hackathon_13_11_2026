import { useId } from "react";

export function Ornament({ className = "" }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 100 100"
      fill="none"
      className={className}
      aria-hidden="true"
    >
      <path
        d="m50 5 15 15-10 10-5-5-5 5-10-10L50 5Zm-30 30 15 15-15 15L5 50l15-15Zm60 0 15 15-15 15-15-15 15-15ZM50 65l15 15-15 15-15-15 15-15Z"
        stroke="currentColor"
        strokeWidth="7"
      />
      <path
        d="m50 32 18 18-18 18-18-18 18-18Z"
        stroke="currentColor"
        strokeWidth="6"
      />
      <path d="m50 43 7 7-7 7-7-7 7-7Z" fill="currentColor" />
    </svg>
  );
}

export function SchoolLogo() {
  return (
    <span className="school-logo">
      <svg viewBox="0 0 56 58" aria-hidden="true">
        <g
          fill="none"
          stroke="currentColor"
          strokeWidth="2.8"
          strokeLinejoin="miter"
        >
          <path d="m28 3 8 8-5 5-4-4 4-4M18 15l8 8-8 8-8-8 8-8Zm20 0 8 8-8 8-8-8 8-8ZM8 25l8 8-8 8-8-8 8-8Zm20 0 8 8-8 8-8-8 8-8Zm20 0 8 8-8 8-8-8 8-8ZM18 35l8 8-8 8-8-8 8-8Zm20 0 8 8-8 8-8-8 8-8Z" />
          <path d="m16 54 12-5 12 5M28 49v8" />
        </g>
        <path
          d="m18 21 2 2-2 2-2-2m20-2 2 2-2 2-2-2m-8 8 2 2-2 2-2-2"
          fill="currentColor"
        />
      </svg>
      <span>
        Башкирская
        <br />
        языковая
        <br />
        школа
      </span>
    </span>
  );
}

export function Mascot({ className = "" }: { className?: string }) {
  const id = useId();
  return (
    <svg viewBox="0 0 56 56" className={className} aria-hidden="true">
      <defs>
        <radialGradient id={id} cx="35%" cy="25%">
          <stop stopColor="#c9ee79" />
          <stop offset="1" stopColor="#83c73b" />
        </radialGradient>
      </defs>
      <path d="M30 14C28 6 32 2 38 2c-1 7-4 11-8 12" fill="#34874b" />
      <path d="M27 15C18 15 14 9 16 4c8 1 12 5 11 11" fill="#efad44" />
      <path
        d="M12 23c3-8 11-10 17-7 8-4 19 2 21 12 3 14-9 23-21 23S5 43 8 32c0-3 2-7 4-9"
        fill={`url(#${id})`}
      />
      <ellipse cx="18" cy="27" rx="5" ry="3" fill="#e0f6a8" opacity=".5" />
      <circle cx="23" cy="32" r="1.6" fill="#285b32" />
      <circle cx="38" cy="30" r="1.6" fill="#285b32" />
      <path
        d="m28 38 5 1"
        stroke="#427638"
        strokeWidth="1.6"
        strokeLinecap="round"
      />
      <ellipse cx="18" cy="36" rx="3" ry="1.8" fill="#a1c854" />
      <ellipse cx="43" cy="34" rx="3" ry="1.8" fill="#a1c854" />
    </svg>
  );
}

export function BookArt() {
  const id = useId();
  return (
    <svg viewBox="0 0 172 115" className="book-art" aria-hidden="true">
      <defs>
        <linearGradient id={`${id}cover`} x2="0" y2="1">
          <stop stopColor="#80ca58" />
          <stop offset="1" stopColor="#248435" />
        </linearGradient>
        <linearGradient id={`${id}page`} x2="1" y2="0">
          <stop stopColor="#e2e3c5" />
          <stop offset=".28" stopColor="#fffef0" />
          <stop offset=".8" stopColor="#fffef5" />
          <stop offset="1" stopColor="#e8e8cf" />
        </linearGradient>
      </defs>
      <ellipse cx="88" cy="95" rx="76" ry="10" fill="#165a28" opacity=".3" />
      <path
        d="M20 33Q85 19 151 33l10 49q2 10-10 13-67 11-133-2-9-2-7-12Z"
        fill={`url(#${id}cover)`}
      />
      <path d="M22 39q64-13 127-1l6 43q-67 14-139 0Z" fill="#236a2a" />
      <path
        d="M28 29q31-8 59 2 28-11 58-4l5 50q-32-2-63 6-29-8-63-5Z"
        fill="#dadfc1"
      />
      <path
        d="M30 25q28-6 57 4 30-10 57-4l2 49q-27-5-59 7-31-12-60-7Z"
        fill={`url(#${id}page)`}
      />
      <path d="M87 30v50" stroke="#d6d7b9" strokeWidth="1.5" />
      {[0, 1, 2, 3, 4, 5, 6].map((i) => (
        <g key={i} fill="none" stroke="#cecfb1" strokeWidth=".7" opacity=".65">
          <path d={`M36 ${34 + i * 5}q22-3 44 3`} />
          <path d={`M95 ${37 + i * 5}q21-5 42-3`} />
        </g>
      ))}
      <path d="m94 79 4-1v8l-2-2-2 2Z" fill="#df5f43" />
      <path
        d="M20 84q66 12 133 0"
        fill="none"
        stroke="#a0d376"
        strokeWidth="2"
        opacity=".7"
      />
    </svg>
  );
}

export function GamepadArt() {
  const id = useId();
  return (
    <svg viewBox="0 0 76 76" className="gamepad-art" aria-hidden="true">
      <defs>
        <linearGradient id={id} x1=".2" y1="0" x2=".7" y2="1">
          <stop stopColor="#8bc0ff" />
          <stop offset=".45" stopColor="#5087f5" />
          <stop offset="1" stopColor="#2452d3" />
        </linearGradient>
      </defs>
      <circle cx="39" cy="34" r="27" fill="#e6efff" />
      <path
        d="m25 24 24 4c8 1 11 8 12 15l3 17c1 7-6 11-11 6L43 55 29 52 19 57C9 61 5 53 10 46l9-16c1-3 3-5 6-6Z"
        fill={`url(#${id})`}
      />
      <path
        d="m24 33-2 6-5-1-2 5 6 2-2 6 5 1 2-6 5 2 2-5-6-2 2-6Z"
        fill="#eff7ff"
      />
      <g fill="#e4f0ff">
        <circle cx="47" cy="38" r="2.7" />
        <circle cx="54" cy="44" r="2.7" />
        <circle cx="40" cy="44" r="2.7" />
        <circle cx="47" cy="50" r="2.7" />
      </g>
      <path
        d="m33 17 2 5m8-6-1 5m9-1-4 4"
        stroke="#4682e8"
        strokeWidth="2.5"
        strokeLinecap="round"
      />
    </svg>
  );
}

export function FlameArt() {
  const id = useId();
  return (
    <svg viewBox="0 0 36 46" className="flame-art" aria-hidden="true">
      <defs>
        <linearGradient id={id} x2="0" y2="1">
          <stop stopColor="#ffbd23" />
          <stop offset="1" stopColor="#ff9300" />
        </linearGradient>
      </defs>
      <path
        d="M18 1c4 9 2 12 5 16 2-2 4-6 4-9 13 16 11 34-9 36C0 44-3 29 7 16c0 8 2 10 4 11C9 16 13 10 18 1Z"
        fill={`url(#${id})`}
      />
      <path
        d="M19 23c1 7 7 7 7 13 0 9-16 10-16 0 0-4 3-8 6-11-1 5 0 7 2 8 2-3 2-6 1-10Z"
        fill="#ffe15b"
      />
      <path d="M19 34c6 7 0 11-4 8-2-2 0-5 4-8Z" fill="#fff1a0" />
    </svg>
  );
}
