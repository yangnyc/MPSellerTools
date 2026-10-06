import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";
import TenantsListPage from "./pages/TenantsListPage";
import TenantNewPage from "./pages/TenantNewPage";
import TenantDetailPage from "./pages/TenantDetailPage";
import JobsPage from "./pages/JobsPage";
import AuditPage from "./pages/AuditPage";
import ProfilePage from "./pages/ProfilePage";
import TenantBulkPage from "./pages/advanced/TenantBulkPage";
import TenantExportPage from "./pages/advanced/TenantExportPage";
import TenantControlsPage from "./pages/advanced/TenantControlsPage";
import TenantUsersListPage from "./pages/tenant-users/TenantUsersListPage";
import TenantUsersBulkPage from "./pages/tenant-users/TenantUsersBulkPage";
import TenantUsersExportPage from "./pages/tenant-users/TenantUsersExportPage";
import TenantUsersControlsPage from "./pages/tenant-users/TenantUsersControlsPage";

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
  // Sub-items: makes this an expandable sidenav group with no page of its own.
  collapse?: AppRoute[];
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
    name: "Tenants",
    key: "tenants",
    icon: <Icon fontSize="small">apartment</Icon>,
    collapse: [
      {
        type: "collapse",
        name: "All tenants",
        key: "tenants-list",
        icon: <Icon fontSize="small">view_list</Icon>,
        route: "/tenants",
        component: <TenantsListPage />,
      },
      {
        type: "collapse",
        name: "Bulk actions",
        key: "tenants-bulk",
        icon: <Icon fontSize="small">checklist_rtl</Icon>,
        route: "/tenants/bulk",
        component: <TenantBulkPage />,
      },
      {
        type: "collapse",
        name: "Filters & export",
        key: "tenants-export",
        icon: <Icon fontSize="small">file_download</Icon>,
        route: "/tenants/export",
        component: <TenantExportPage />,
      },
      {
        type: "collapse",
        name: "Tenant controls",
        key: "tenants-controls",
        icon: <Icon fontSize="small">settings_power</Icon>,
        route: "/tenants/controls",
        component: <TenantControlsPage />,
      },
    ],
  },
  {
    type: "collapse",
    name: "Tenant users",
    // Letters only: the server saves a pinned group by this key and accepts nothing else.
    key: "tenantusers",
    icon: <Icon fontSize="small">groups</Icon>,
    collapse: [
      {
        type: "collapse",
        name: "All users",
        key: "tenant-users-list",
        icon: <Icon fontSize="small">view_list</Icon>,
        route: "/tenant-users",
        component: <TenantUsersListPage />,
      },
      {
        type: "collapse",
        name: "Bulk actions",
        key: "tenant-users-bulk",
        icon: <Icon fontSize="small">checklist_rtl</Icon>,
        route: "/tenant-users/bulk",
        component: <TenantUsersBulkPage />,
      },
      {
        type: "collapse",
        name: "Filters & export",
        key: "tenant-users-export",
        icon: <Icon fontSize="small">file_download</Icon>,
        route: "/tenant-users/export",
        component: <TenantUsersExportPage />,
      },
      {
        type: "collapse",
        name: "Account controls",
        key: "tenant-users-controls",
        icon: <Icon fontSize="small">manage_accounts</Icon>,
        route: "/tenant-users/controls",
        component: <TenantUsersControlsPage />,
      },
    ],
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
