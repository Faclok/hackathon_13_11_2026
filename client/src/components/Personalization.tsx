import type { CSSProperties } from "react";
import type { Decoration, User } from "../types";
import { displayName } from "../utils/displayName";
import { decorationCatalog } from "../mocks/customization";
import { Mascot, Ornament } from "./Illustrations";
import { HatOverlay } from "./HatOverlay";
import "../personalization.css";

export function DecorationArtwork({ item }: { item: Decoration }) {
  return item.imageUrl ? (
    <img
      className="decoration-art"
      src={item.imageUrl}
      alt=""
      draggable={false}
    />
  ) : (
    <>{item.previewUrl}</>
  );
}

export function Avatar({
  user,
  className = "",
  framed = true,
}: {
  user: User;
  className?: string;
  framed?: boolean;
}) {
  return (
    <span
      className={`player-avatar ${framed && user.frameId ? `frame-${user.frameId}` : ""} ${className}`}
      data-hat-id={user.hatId}
    >
      {user.avatarUrl ? (
        <img
          src={user.avatarUrl}
          alt={`Аватар: ${displayName(user)}`}
          draggable={false}
        />
      ) : (
        <Mascot />
      )}
      {user.hatId && user.hatId !== "hat_none" && (
        <span className="avatar-hat">
          <HatOverlay id={user.hatId} />
        </span>
      )}
    </span>
  );
}

export function ProfileBackground({ backgroundId }: { backgroundId?: string }) {
  const background = decorationCatalog.find(
    (item) => item.id === backgroundId && item.type === "background",
  );
  const ornament =
    backgroundId?.startsWith("ornament") || !!background?.background;
  const landscape = [
    "steppe",
    "yurt",
    "mountain",
    "bg_ural_mountains",
  ].includes(backgroundId ?? "");
  return (
    <div
      className={`personal-background scenery-${backgroundId ?? "mint"} ${background?.imageUrl ? "artwork-background" : background?.background ? "custom-background" : ""}`}
      style={
        background?.background
          ? { background: background.background }
          : undefined
      }
      aria-hidden="true"
    >
      {background?.imageUrl && (
        <img
          className="background-art"
          src={background.imageUrl}
          alt=""
          draggable={false}
        />
      )}
      {ornament && (
        <div className="personal-ornaments">
          {Array.from({ length: 6 }, (_, i) => (
            <Ornament key={i} />
          ))}
        </div>
      )}
      {landscape && (
        <svg
          className="landscape-art"
          viewBox="0 0 320 240"
          preserveAspectRatio="xMidYMax slice"
        >
          <circle cx="258" cy="48" r="24" fill="#f2d17c" opacity=".8" />
          {backgroundId === "mountain" ||
          backgroundId === "bg_ural_mountains" ? (
            <>
              <path d="m-30 210 95-137 70 87 62-130 140 180Z" fill="#aec8c0" />
              <path
                d="m149 132 48-102 45 76-25-10-16 14-16-30-20 57Z"
                fill="#f2f7ed"
              />
              <path d="m27 128 38-55 35 44-25-7-12-16-12 26Z" fill="#ecf3e6" />
            </>
          ) : (
            <path d="M0 135Q80 83 177 137t150-1v104H0Z" fill="#d7cf96" />
          )}
          <path
            d="M-10 188Q78 132 183 179t157-21v82H-10Z"
            fill={
              backgroundId === "mountain" ||
              backgroundId === "bg_ural_mountains"
                ? "#97b69c"
                : "#b4c589"
            }
          />
          <path d="M-10 213Q115 180 330 204v36H-10Z" fill="#87aa79" />
          {backgroundId === "yurt" && (
            <g transform="translate(172 134)">
              <path d="M0 29Q47-22 94 29v45H0Z" fill="#f7e7c8" />
              <path
                d="M-5 29Q47-25 99 29"
                fill="none"
                stroke="#b38e70"
                strokeWidth="4"
              />
              <path d="M40 74V41h19v33" fill="#b78366" />
              <path
                d="M0 43h94M12 27v47m70-47v47"
                stroke="#d4b28b"
                strokeWidth="3"
              />
            </g>
          )}
          {backgroundId === "steppe" &&
            [23, 42, 65, 281, 301].map((x, i) => (
              <g key={x} stroke="#758b53" strokeWidth="2" fill="none">
                <path
                  d={`M${x} 240v-${32 + (i % 2) * 10}m0 18-7-10m7 0 7-10m-7 0-6-8`}
                />
              </g>
            ))}
        </svg>
      )}
    </div>
  );
}

export function FloatingItems({
  user,
  onSelect,
}: {
  user: User;
  onSelect?: (item: Decoration) => void;
}) {
  const items = (user.equippedItemIds ?? user.ownedDecorationIds)
    .map((id) => decorationCatalog.find((d) => d.id === id))
    .filter((item): item is Decoration => item?.type === "item");
  const pet = decorationCatalog.find(
    (item) => item.id === user.petId && item.type === "pet" && !item.isDefault,
  );
  const itemStyle = (index: number): CSSProperties | undefined => {
    if (items.length <= 4) return undefined;
    const rows = Math.ceil(items.length / 2);
    return {
      "--item-top": `${6 + (Math.floor(index / 2) * 60) / (rows - 1)}%`,
      "--item-left": index % 2 === 0 ? "4%" : "auto",
      "--item-right": index % 2 === 1 ? "4%" : "auto",
    } as CSSProperties;
  };
  if (!items.length && !pet) return null;
  return (
    <div className="floating-items" aria-label="Предметы на фоне">
      {items.map((item, index) =>
        onSelect ? (
          <button
            className={`floating-item ${items.length > 4 ? "distributed-item" : ""}`}
            style={itemStyle(index)}
            key={item.id}
            aria-label={`О предмете: ${item.title}`}
            title={item.title}
            onClick={() => onSelect(item)}
          >
            <DecorationArtwork item={item} />
          </button>
        ) : (
          <span
            className={`floating-item ${items.length > 4 ? "distributed-item" : ""}`}
            style={itemStyle(index)}
            key={item.id}
            role="img"
            aria-label={item.title}
            title={item.title}
          >
            <DecorationArtwork item={item} />
          </span>
        ),
      )}
      {pet &&
        (onSelect ? (
          <button
            className="equipped-pet"
            aria-label={`О питомце: ${pet.title}`}
            title={pet.title}
            onClick={() => onSelect(pet)}
          >
            <span>
              <DecorationArtwork item={pet} />
            </span>
            <small>{pet.petName}</small>
          </button>
        ) : (
          <span className="equipped-pet" role="img" aria-label={pet.title}>
            <span>
              <DecorationArtwork item={pet} />
            </span>
            <small>{pet.petName}</small>
          </span>
        ))}
    </div>
  );
}

export function DecorationPreview({
  item,
  user,
}: {
  item: Decoration;
  user: User;
}) {
  return (
    <div
      className={`decoration-preview personal-card preview-${item.type}`}
      aria-label={`Превью: ${item.title}`}
    >
      {item.type === "background" ? (
        <>
          <ProfileBackground backgroundId={item.id} />
          <Avatar
            user={{ ...user, hatId: undefined }}
            className="preview-avatar"
            framed={false}
          />
        </>
      ) : item.type === "frame" ? (
        <Avatar
          user={{
            ...user,
            frameId: item.isDefault ? undefined : item.id,
            hatId: undefined,
          }}
          className="preview-avatar"
        />
      ) : item.type === "hat" ? (
        <Avatar
          user={{ ...user, hatId: item.isDefault ? undefined : item.id }}
          className="preview-avatar"
          framed={false}
        />
      ) : (
        <span className="item-preview-symbol" aria-hidden="true">
          <DecorationArtwork item={item} />
          {item.type === "pet" && item.petName && !item.imageUrl && (
            <small className="pet-preview-name">{item.petName}</small>
          )}
        </span>
      )}
    </div>
  );
}
