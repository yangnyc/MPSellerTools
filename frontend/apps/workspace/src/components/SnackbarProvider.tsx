import { useCallback, useRef, useState, type ReactNode } from "react";
import MDSnackbar from "components/MDSnackbar";

import { useAuth } from "../auth/useAuth";
import { SnackbarContext, type NotificationEntry, type Severity } from "./snackbarState";

// The notifications view keeps this many; older ones drop off the end.
const maxNotifications = 50;

export function SnackbarProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [severity, setSeverity] = useState<Severity>("info");
  // Counts notifications, so each one gets its own five seconds even when it
  // replaces one that is still showing.
  const [shownCount, setShownCount] = useState(0);
  const [notifications, setNotifications] = useState<NotificationEntry[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const nextId = useRef(1);

  // The list belongs to whoever is signed in: signing out, or in as someone
  // else, starts it empty.
  const userId = useAuth().user?.id ?? null;
  const [ownerId, setOwnerId] = useState(userId);
  if (ownerId !== userId) {
    setOwnerId(userId);
    setNotifications([]);
    setUnreadCount(0);
  }

  const notify = useCallback((msg: string, sev: Severity = "info") => {
    const entry = { id: nextId.current++, message: msg, severity: sev, at: new Date().toISOString() };
    setMessage(msg);
    setSeverity(sev);
    setShownCount((count) => count + 1);
    setNotifications((list) => [entry, ...list].slice(0, maxNotifications));
    setUnreadCount((count) => Math.min(count + 1, maxNotifications));
    setOpen(true);
  }, []);

  const markNotificationsRead = useCallback(() => setUnreadCount(0), []);

  const clearNotifications = useCallback(() => {
    setNotifications([]);
    setUnreadCount(0);
  }, []);

  return (
    <SnackbarContext.Provider
      value={{ notify, notifications, unreadCount, markNotificationsRead, clearNotifications }}
    >
      {children}
      <MDSnackbar
        key={shownCount}
        color={severity}
        icon={severity === "error" ? "warning" : "notifications"}
        title="MP Seller Tools"
        dateTime=""
        content={message}
        open={open}
        close={() => setOpen(false)}
        // Disappears by itself after five seconds; a click elsewhere on the
        // page does not dismiss it early.
        autoHideDuration={5000}
        onClose={(_event: unknown, reason: string) => {
          if (reason !== "clickaway") {
            setOpen(false);
          }
        }}
      />
    </SnackbarContext.Provider>
  );
}
