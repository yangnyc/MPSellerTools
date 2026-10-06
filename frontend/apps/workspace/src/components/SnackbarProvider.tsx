import { useCallback, useState, type ReactNode } from "react";
import MDSnackbar from "components/MDSnackbar";

import { SnackbarContext, type Severity } from "./snackbarState";

export function SnackbarProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [severity, setSeverity] = useState<Severity>("info");
  // Counts notifications, so each one gets its own five seconds even when it
  // replaces one that is still showing.
  const [shownCount, setShownCount] = useState(0);

  const notify = useCallback((msg: string, sev: Severity = "info") => {
    setMessage(msg);
    setSeverity(sev);
    setShownCount((count) => count + 1);
    setOpen(true);
  }, []);

  return (
    <SnackbarContext.Provider value={{ notify }}>
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
