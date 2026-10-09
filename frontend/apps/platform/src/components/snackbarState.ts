import { createContext } from "react";

export type Severity = "success" | "error" | "warning" | "info";

export type NotificationEntry = {
  id: number;
  message: string;
  severity: Severity;
  // When it was shown, as an ISO timestamp.
  at: string;
};

export type SnackbarState = {
  notify: (message: string, severity?: Severity) => void;
  // Everything `notify` has shown to the signed-in user in this browser,
  // newest first — the top bar's notifications view, which pages through it.
  notifications: NotificationEntry[];
  unreadCount: number;
  markNotificationsRead: () => void;
  clearNotifications: () => void;
  // Takes one out of the list for good.
  dismissNotification: (id: number) => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

