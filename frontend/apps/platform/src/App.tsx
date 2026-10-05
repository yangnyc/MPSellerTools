import { useEffect, type ReactNode } from "react";
import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import Sidenav from "examples/Sidenav";
import Configurator from "examples/Configurator";
import { useMaterialUIController } from "context";
import routes, { type AppRoute } from "./routes";
import LoginPage from "./pages/LoginPage";
import { useAuth } from "./auth/useAuth";

function renderProtectedRoutes(allRoutes: AppRoute[]) {
  return allRoutes
    .filter((route) => route.type === "collapse" && route.route)
    .map((route) => (
      <Route
        path={route.route}
        element={<RequireAuth>{route.component}</RequireAuth>}
        key={route.key}
      />
    ));
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();

  if (status === "loading") {
    return null;
  }
  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

export default function App() {
  const [controller] = useMaterialUIController();
  const { layout, sidenavColor } = controller;
  const { pathname } = useLocation();
  const { status } = useAuth();

  useEffect(() => {
    document.documentElement.scrollTop = 0;
  }, [pathname]);

  const showChrome = layout === "dashboard" && pathname !== "/login" && status === "authenticated";
  const sidenavRoutes = routes.filter((route) => !route.hideFromSidenav);

  return (
    <>
      {showChrome && (
        <>
          <Sidenav color={sidenavColor} brandName="MP Seller Tools" routes={sidenavRoutes} />
          <Configurator />
        </>
      )}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        {renderProtectedRoutes(routes)}
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  );
}
