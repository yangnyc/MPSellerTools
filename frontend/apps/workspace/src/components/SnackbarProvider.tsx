import { useCallback, useState, type ReactNode } from "react";
import MDSnackbar from "components/MDSnackbar";

import { SnackbarContext, type Severity } from "./snackbarState";

export function SnackbarProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [severity, setSeverity] = useState<Severity>("info");

  const notify = useCallback((msg: string, sev: Severity = "info") => {
    setMessage(msg);
    setSeverity(sev);
    setOpen(true);
  }, []);

  return (
    <SnackbarContext.Provider value={{ notify }}>
      {children}
      <MDSnackbar
        color={severity}
        icon={severity === "error" ? "warning" : "notifications"}
        title="MP Seller Tools"
        dateTime=""
        content={message}
        open={open}
        close={() => setOpen(false)}
      />
    </SnackbarContext.Provider>
  );
}
