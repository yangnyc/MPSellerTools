import { useContext } from "react";
import { SnackbarContext, type SnackbarState } from "./SnackbarProvider";

export function useSnackbar(): SnackbarState {
  const context = useContext(SnackbarContext);
  if (!context) {
    throw new Error("useSnackbar must be used inside a SnackbarProvider");
  }
  return context;
}
