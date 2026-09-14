import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { MaterialUIControllerProvider } from "context";
import { AuthProvider } from "./auth/AuthContext";
import "./index.css";
import App from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <MaterialUIControllerProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MaterialUIControllerProvider>
    </BrowserRouter>
  </StrictMode>
);
