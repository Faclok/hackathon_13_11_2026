import type { ReactNode } from "react";
export function HatOverlay({ id }: { id?: string }) {
  const hats: Record<string, ReactNode> = {
    hat_tubeteika: (
      <svg viewBox="0 0 80 42" aria-hidden="true">
        <path
          d="M14 34C14 16 25 8 40 8s26 8 26 26H14Z"
          fill="#1b5e20"
          stroke="#fbbf24"
          strokeWidth="2.5"
        />
        <path
          d="M14 31h52"
          stroke="#fbbf24"
          strokeWidth="3"
          strokeDasharray="4 3"
        />
        <path d="m40 13 5 7-5 7-5-7 5-7Z" fill="#fbbf24" />
      </svg>
    ),
    hat_cap: (
      <svg viewBox="0 0 90 48" aria-hidden="true">
        <path
          d="m45 4 37 14-37 12L8 18 45 4Z"
          fill="#1e293b"
          stroke="#334155"
          strokeWidth="1.5"
        />
        <path d="M24 23v11c0 4 42 4 42 0V23" fill="#0f172a" />
        <path
          d="M74 20v15"
          stroke="#fbbf24"
          strokeWidth="2.5"
          strokeLinecap="round"
        />
        <circle cx="74" cy="36" r="2.5" fill="#fbbf24" />
      </svg>
    ),
    hat_burek: (
      <svg viewBox="0 0 90 48" aria-hidden="true">
        <path d="M20 27C20 12 30 6 45 6s25 6 25 21H20Z" fill="#991b1b" />
        <rect
          x="10"
          y="22"
          width="70"
          height="18"
          rx="9"
          fill="#78350f"
          stroke="#451a03"
          strokeWidth="2"
        />
        <path
          d="M18 28c10-2 44-2 54 0"
          stroke="#92400e"
          strokeWidth="2"
          strokeLinecap="round"
        />
      </svg>
    ),
    hat_crown: (
      <svg viewBox="0 0 84 46" aria-hidden="true">
        <path
          d="m12 36-4-22 19 11L42 8l15 17 19-11-4 22H12Z"
          fill="#fbbf24"
          stroke="#b45309"
          strokeWidth="2.2"
          strokeLinejoin="round"
        />
        <circle cx="42" cy="24" r="3" fill="#ef4444" />
        <circle cx="24" cy="28" r="2.2" fill="#10b981" />
        <circle cx="60" cy="28" r="2.2" fill="#3b82f6" />
      </svg>
    ),
  };
  return id ? hats[id] || null : null;
}
