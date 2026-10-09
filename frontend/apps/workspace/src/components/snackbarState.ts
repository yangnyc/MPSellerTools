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
  // What `notify` has shown to the signed-in user since the page loaded,
  // newest first — the top bar's notifications view.
  notifications: NotificationEntry[];
  unreadCount: number;
  markNotificationsRead: () => void;
  clearNotifications: () => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

