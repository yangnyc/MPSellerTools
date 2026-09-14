import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";
import TenantsListPage from "./pages/TenantsListPage";
import TenantNewPage from "./pages/TenantNewPage";
import TenantDetailPage from "./pages/TenantDetailPage";
import JobsPage from "./pages/JobsPage";
import AuditPage from "./pages/AuditPage";
import ProfilePage from "./pages/ProfilePage";

// Sidenav + router entries for the PlatformAdmin console (brief §8).
export type AppRoute = {
  type: "collapse" | "title" | "divider";
  name?: string;
  key: string;
  icon?: React.ReactNode;
  route?: string;
  component?: React.ReactNode;
  // When true, this route is registered for direct navigation (e.g.
  // /tenants/new, /tenants/:id) but is not shown as its own sidenav entry —
  // it's reached via a button/link from another page instead.
  hideFromSidenav?: boolean;
};

const routes: AppRoute[] = [
  {
    type: "collapse",
    name: "Dashboard",
    key: "dashboard",
    icon: <Icon fontSize="small">dashboard</Icon>,
    route: "/dashboard",
    component: <DashboardPage />,
  },
  {
    type: "collapse",
    name: "Tenants",
    key: "tenants",
    icon: <Icon fontSize="small">apartment</Icon>,
    route: "/tenants",
    component: <TenantsListPage />,
  },
  {
    type: "collapse",
    key: "tenants-new",
    route: "/tenants/new",
    component: <TenantNewPage />,
    hideFromSidenav: true,
  },
  {
    type: "collapse",
    key: "tenants-detail",
    route: "/tenants/:id",
    component: <TenantDetailPage />,
    hideFromSidenav: true,
  },
  {
    type: "collapse",
    name: "Jobs",
    key: "jobs",
    icon: <Icon fontSize="small">work_history</Icon>,
    route: "/jobs",
    component: <JobsPage />,
  },
  {
    type: "collapse",
    name: "Audit",
    key: "audit",
    icon: <Icon fontSize="small">fact_check</Icon>,
    route: "/audit",
    component: <AuditPage />,
  },
  {
    type: "collapse",
    name: "Profile",
    key: "profile",
    icon: <Icon fontSize="small">person</Icon>,
    route: "/profile",
    component: <ProfilePage />,
  },
];

export default routes;
