import { useEffect, type ReactNode } from "react";
import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import Sidenav from "examples/Sidenav";
import Configurator from "examples/Configurator";
import { useMaterialUIController } from "context";
import routes, { type AppRoute } from "./routes";
import LoginPage from "./pages/LoginPage";
import { useAuth } from "./auth/useAuth";

function RequireAuth({ children, allowedRoles }: { children: ReactNode; allowedRoles?: string[] }) {
  const { status, user } = useAuth();

  if (status === "loading") {
    return null;
  }
  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }
  // Menu items for restricted pages are already hidden for the wrong role
  // (brief §8), but a direct URL visit must also be denied client-side —
  // the actual enforcement is server-side (brief §5), this is just so the
  // Employee doesn't land on a page that only errors out.
  if (allowedRoles && !allowedRoles.some((role) => user?.roles.includes(role))) {
    return <Navigate to="/dashboard" replace />;
  }
  return <>{children}</>;
}

function renderProtectedRoutes(allRoutes: AppRoute[]) {
  return allRoutes
    .filter((route) => route.type === "collapse" && route.route)
    .map((route) => (
      <Route
        path={route.route}
        element={<RequireAuth allowedRoles={route.roles}>{route.component}</RequireAuth>}
        key={route.key}
      />
    ));
}

export default function App() {
  const [controller] = useMaterialUIController();
  const { layout, sidenavColor } = controller;
  const { pathname } = useLocation();
  const { status, user } = useAuth();

  useEffect(() => {
    document.documentElement.scrollTop = 0;
  }, [pathname]);

  const showChrome = layout === "dashboard" && pathname !== "/login" && status === "authenticated";
  const visibleRoutes = routes.filter((route) => !route.roles || route.roles.some((role) => user?.roles.includes(role)));

  return (
    <>
      {showChrome && (
        <>
          <Sidenav color={sidenavColor} brandName="MP Seller Tools" routes={visibleRoutes} />
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
