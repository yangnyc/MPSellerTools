import { createContext } from "react";

export type Severity = "success" | "error" | "warning" | "info";

export type NotificationEntry = {
  id: number;
  message: string;
  severity: Severity;
  // When it was shown, as an ISO timestamp.
  at: string;
};

// Something wrong that stays wrong until it is put right: worked out by the server from how things are,
// so it cannot be dismissed, and goes by itself once its cause does. `link` is the page where that is done.
export type StickyAlert = { key: string; title: string; message: string; action: string; link: string };

export type SnackbarState = {
  notify: (message: string, severity?: Severity) => void;
  // Everything `notify` has shown to the signed-in user in this browser,
  // newest first — the top bar's notifications view, which pages through it.
  notifications: NotificationEntry[];
  unreadCount: number;
  markNotificationsRead: () => void;
  clearNotifications: () => void;
  // The standing problems, shown above the list in red until each is resolved.
  alerts: StickyAlert[];
  // Asks again now, for a page that has just put one of them right.
  refreshAlerts: () => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

