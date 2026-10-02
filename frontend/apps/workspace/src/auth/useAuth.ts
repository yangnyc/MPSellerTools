import { useContext } from "react";
import { AuthContext, type AuthState } from "./authState";

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used inside an AuthProvider");
  }
  return context;
}
