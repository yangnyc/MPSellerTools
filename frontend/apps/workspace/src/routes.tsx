import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";
import ProductsPage from "./pages/ProductsPage";
import ListingsPage from "./pages/ListingsPage";
import OrdersPage from "./pages/OrdersPage";
import TasksPage from "./pages/TasksPage";
import UsersPage from "./pages/UsersPage";
import SettingsPage from "./pages/SettingsPage";
import EbayPage from "./pages/EbayPage";
import MarketplaceListingsPage from "./pages/marketplace/MarketplaceListingsPage";
import MarketplaceAddProductPage from "./pages/marketplace/MarketplaceAddProductPage";
import MarketplaceSettingsPage from "./pages/marketplace/MarketplaceSettingsPage";
import SyncQueuePage from "./pages/marketplace/SyncQueuePage";
import JobsPage from "./pages/JobsPage";
import InventoryPage from "./pages/InventoryPage";
import { AMAZON, EBAY, MAGENTO, WALMART, settingsPath, type Marketplace } from "./api/channels";
import AuditPage from "./pages/AuditPage";
import ProfilePage from "./pages/ProfilePage";
import UserBulkPage from "./pages/advanced/UserBulkPage";
import UserExportPage from "./pages/advanced/UserExportPage";
import UserControlsPage from "./pages/advanced/UserControlsPage";
import UserInvitationsPage from "./pages/advanced/UserInvitationsPage";
import UserRolesPage from "./pages/advanced/UserRolesPage";
import UserActivityPage from "./pages/advanced/UserActivityPage";

// Sidenav + router entries shared by TenantAdmin and Employee (brief §8).
// Routes without a `roles` restriction are visible to both; TenantAdmin-only
// entries (Users, Inventory, the marketplaces, Sync queue, Settings, Audit) are filtered out of the Employee's sidenav
// and, independently, rejected by the backend even if a request reaches the
// API directly (brief §5/§8's "direct requests to restricted APIs must also
// be denied").
export type AppRoute = {
  type: "collapse" | "title" | "divider";
  name?: string;
  key: string;
  icon?: React.ReactNode;
  route?: string;
  component?: React.ReactNode;
  roles?: string[];
  // Sub-items: makes this an expandable sidenav group with no page of its own.
  collapse?: AppRoute[];
};

// A marketplace's sidenav group: its products, the page that adds a product to
// it, what it reports as posted, and its account settings. `first` goes ahead of those, for a page only
// that marketplace has. Settings under a name of their own (Magento's "Connection") lead the group.
function marketplaceGroup(marketplace: Marketplace, first: AppRoute[] = []): AppRoute {
  const key = marketplace.name.toLowerCase();
  const settings: AppRoute = {
    type: "collapse",
    name: marketplace.settingsName ?? "Settings",
    key: `${key}-settings`,
    icon: <Icon fontSize="small">{marketplace.settingsName ? "link" : "tune"}</Icon>,
    route: settingsPath(marketplace),
    component: <MarketplaceSettingsPage key={key} marketplace={marketplace} />,
    roles: ["TenantAdmin"],
  };
  return {
    type: "collapse",
    name: marketplace.name,
    key,
    icon: <Icon fontSize="small">{marketplace.icon}</Icon>,
    roles: ["TenantAdmin"],
    collapse: [
      ...first,
      ...(marketplace.settingsName ? [settings] : []),
      {
        type: "collapse",
        name: `Products on ${marketplace.name}`,
        key: `${key}-listings`,
        icon: <Icon fontSize="small">view_list</Icon>,
        route: marketplace.path,
        component: <MarketplaceListingsPage key={key} marketplace={marketplace} />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Add product",
        key: `${key}-add-product`,
        icon: <Icon fontSize="small">add_shopping_cart</Icon>,
        route: `${marketplace.path}/add-product`,
        component: <MarketplaceAddProductPage key={key} marketplace={marketplace} />,
        roles: ["TenantAdmin"],
      },
      // What the marketplace itself reports as posted: the Listings page, narrowed to this one.
      {
        type: "collapse",
        name: "Listings",
        key: `${key}-posted`,
        icon: <Icon fontSize="small">sell</Icon>,
        route: `/${key}/listings`,
        component: <ListingsPage key={key} marketplace={marketplace} />,
        roles: ["TenantAdmin"],
      },
      ...(marketplace.settingsName ? [] : [settings]),
    ],
  };
}

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
    name: "Products",
    key: "products",
    icon: <Icon fontSize="small">inventory_2</Icon>,
    route: "/products",
    component: <ProductsPage />,
  },
  {
    type: "collapse",
    name: "Listings",
    key: "listings",
    icon: <Icon fontSize="small">sell</Icon>,
    route: "/listings",
    component: <ListingsPage />,
  },
  {
    type: "collapse",
    name: "Orders",
    key: "orders",
    icon: <Icon fontSize="small">receipt_long</Icon>,
    route: "/orders",
    component: <OrdersPage />,
  },
  {
    type: "collapse",
    name: "Inventory",
    key: "inventory",
    icon: <Icon fontSize="small">warehouse</Icon>,
    route: "/inventory",
    component: <InventoryPage />,
    roles: ["TenantAdmin"],
  },
  // eBay's keys and the seller's consent have a page of their own, where eBay sends the seller back to.
  marketplaceGroup(EBAY, [
    {
      type: "collapse",
      name: "Connection",
      key: "ebay-connection",
      icon: <Icon fontSize="small">link</Icon>,
      route: "/ebay",
      component: <EbayPage />,
      roles: ["TenantAdmin"],
    },
  ]),
  marketplaceGroup(AMAZON),
  marketplaceGroup(WALMART),
  marketplaceGroup(MAGENTO),
  {
    type: "collapse",
    name: "Sync queue",
    key: "sync",
    icon: <Icon fontSize="small">sync</Icon>,
    route: "/sync",
    component: <SyncQueuePage />,
    roles: ["TenantAdmin"],
  },
  // Large pieces of work asked for once and carried out in the background.
  {
    type: "collapse",
    name: "Jobs",
    key: "jobs",
    icon: <Icon fontSize="small">playlist_play</Icon>,
    route: "/jobs",
    component: <JobsPage />,
    roles: ["TenantAdmin"],
  },
  // Between the lines: following up on the work, apart from the selling above.
  { type: "divider", key: "divider-follow-up" },
  {
    type: "collapse",
    name: "Tasks",
    key: "tasks",
    icon: <Icon fontSize="small">checklist</Icon>,
    route: "/tasks",
    component: <TasksPage />,
  },
  {
    type: "collapse",
    name: "Audit",
    key: "audit",
    icon: <Icon fontSize="small">fact_check</Icon>,
    route: "/audit",
    component: <AuditPage />,
    roles: ["TenantAdmin"],
  },
  // Below the line: the company's own setup and people, apart from the daily work above it.
  { type: "divider", key: "divider-admin" },
  {
    type: "collapse",
    name: "Settings",
    key: "settings",
    icon: <Icon fontSize="small">settings</Icon>,
    route: "/settings",
    component: <SettingsPage />,
    roles: ["TenantAdmin"],
  },
  {
    type: "collapse",
    name: "Profile",
    key: "profile",
    icon: <Icon fontSize="small">person</Icon>,
    route: "/profile",
    component: <ProfilePage />,
  },
  {
    type: "collapse",
    name: "Users",
    key: "users",
    icon: <Icon fontSize="small">group</Icon>,
    roles: ["TenantAdmin"],
    collapse: [
      {
        type: "collapse",
        name: "All users",
        key: "users-list",
        icon: <Icon fontSize="small">view_list</Icon>,
        route: "/users",
        component: <UsersPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Bulk actions",
        key: "users-bulk",
        icon: <Icon fontSize="small">checklist_rtl</Icon>,
        route: "/users/bulk",
        component: <UserBulkPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Filters & export",
        key: "users-export",
        icon: <Icon fontSize="small">file_download</Icon>,
        route: "/users/export",
        component: <UserExportPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Account controls",
        key: "users-controls",
        icon: <Icon fontSize="small">manage_accounts</Icon>,
        route: "/users/controls",
        component: <UserControlsPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Invitations",
        key: "users-invitations",
        icon: <Icon fontSize="small">mail</Icon>,
        route: "/users/invitations",
        component: <UserInvitationsPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Roles & access",
        key: "users-roles",
        icon: <Icon fontSize="small">admin_panel_settings</Icon>,
        route: "/users/roles",
        component: <UserRolesPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "User activity",
        key: "users-activity",
        icon: <Icon fontSize="small">timeline</Icon>,
        route: "/users/activity",
        component: <UserActivityPage />,
        roles: ["TenantAdmin"],
      },
    ],
  },
];

export default routes;
