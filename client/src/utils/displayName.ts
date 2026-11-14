import type { User } from "../types";

export function displayName(user: Pick<User, "nick" | "firstName" | "lastName">): string {
  return user.firstName?.trim() && user.lastName?.trim()
    ? `${user.firstName.trim()} ${user.lastName.trim()}`
    : user.nick;
}
