import { useEffect, type ReactNode } from "react";
import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import Sidenav from "examples/Sidenav";
import Configurator from "examples/Configurator";
import { useMaterialUIController } from "context";
import routes, { type AppRoute } from "./routes";
import LoginPage from "./pages/LoginPage";
import ProductViewPage from "./pages/ProductViewPage";
import { useAuth } from "./auth/useAuth";
import { PageLoader } from "./components/LoadingBar";

function RequireAuth({ children, allowedRoles }: { children: ReactNode; allowedRoles?: string[] }) {
  const { status, user } = useAuth();

  if (status === "loading") {
    return <PageLoader />;
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

// A sidenav group has no page of its own; its sub-items do.
const withSubItems = (allRoutes: AppRoute[]): AppRoute[] =>
  allRoutes.flatMap((route) => (route.collapse ? route.collapse : [route]));

function renderProtectedRoutes(allRoutes: AppRoute[]) {
  return withSubItems(allRoutes)
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
  const { status, user, savePinnedMenus } = useAuth();

  useEffect(() => {
    document.documentElement.scrollTop = 0;
  }, [pathname]);

  const showChrome = layout === "dashboard" && pathname !== "/login" && status === "authenticated";
  // A group open to everyone can still hold a page that is not, so its sub-items are checked too.
  const allowed = (route: AppRoute) => !route.roles || route.roles.some((role) => user?.roles.includes(role));
  const visibleRoutes = routes.filter(allowed).map((route) => (route.collapse ? { ...route, collapse: route.collapse.filter(allowed) } : route));

  return (
    <>
      {showChrome && (
        <>
          <Sidenav
            color={sidenavColor}
            brandName="MP Seller Tools"
            routes={visibleRoutes}
            pinnedGroups={user?.pinnedMenus ?? []}
            // A failed save puts the checkbox back by itself.
            onPinnedGroupsChange={(menus: string[]) => void savePinnedMenus(menus).catch(() => undefined)}
          />
          <Configurator />
        </>
      )}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        {renderProtectedRoutes(routes)}
        {/* One product's own page: reached from the tables, so it has no sidenav entry. */}
        <Route path="/products/:id" element={<RequireAuth><ProductViewPage /></RequireAuth>} />
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </>
  );
}
