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
  // `toastOnly` shows the message without keeping it in the list: for something a standing alert already says.
  notify: (message: string, severity?: Severity, options?: { toastOnly?: boolean }) => void;
  // Everything `notify` has shown to the signed-in user in this browser,
  // newest first — the top bar's notifications view, which pages through it.
  notifications: NotificationEntry[];
  unreadCount: number;
  markNotificationsRead: () => void;
  clearNotifications: () => void;
  // Takes one out of the list for good.
  dismissNotification: (id: number) => void;
  // The standing problems, shown above the list in red until each is resolved.
  alerts: StickyAlert[];
  // Puts one out of sight in this browser. It comes back if the problem changes (more of it, say), and
  // a problem that was resolved and happens again is shown again.
  dismissAlert: (key: string) => void;
  // Asks again now, for a page that has just put one of them right.
  refreshAlerts: () => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

