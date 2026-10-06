import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import {
  FilterTabs,
  Identity,
  InlineAlert,
  PageHeader,
  Section,
  SimpleTable,
  StatCard,
  StateBlock,
  StatusPill,
  roleLabel,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useAuth } from "../../auth/useAuth";
import { ApiError } from "../../lib/api";
import { UsersApi } from "../../api/resources";
import type { UserSummary } from "../../api/types";

type Level = "full" | "limited" | "none";
type Access = { level: Level; text: string };
type Area = { area: string; TenantAdmin: Access; Employee: Access };

const ROLES = ["TenantAdmin", "Employee"] as const;
const roleOf = (user: UserSummary) => user.roles[0] ?? "Employee";
const none: Access = { level: "none", text: "No access" };

// What each role may do. The server enforces these rules on every request;
// this table only describes them, so keep it in step with the [Authorize]
// policies on the TenantHost controllers.
const AREAS: Area[] = [
  {
    area: "Dashboard",
    TenantAdmin: { level: "full", text: "Figures for the whole company" },
    Employee: { level: "limited", text: "Their own orders and tasks" },
  },
  {
    area: "Products",
    TenantAdmin: { level: "full", text: "Create, edit and archive" },
    Employee: { level: "limited", text: "View only" },
  },
  {
    area: "Orders",
    TenantAdmin: { level: "full", text: "Create, edit, assign and change status" },
    Employee: { level: "limited", text: "View orders assigned to them" },
  },
  {
    area: "Tasks",
    TenantAdmin: { level: "full", text: "Create, edit and assign" },
    Employee: { level: "limited", text: "Change the status of their own tasks" },
  },
  { area: "Users and invitations", TenantAdmin: { level: "full", text: "Invite, change roles, block" }, Employee: none },
  { area: "Company settings", TenantAdmin: { level: "full", text: "Edit" }, Employee: none },
  { area: "Audit log", TenantAdmin: { level: "full", text: "View" }, Employee: none },
  {
    area: "Profile and password",
    TenantAdmin: { level: "full", text: "Their own" },
    Employee: { level: "full", text: "Their own" },
  },
];

const LEVEL_TONE: Record<Level, "success" | "warning" | "neutral"> = { full: "success", limited: "warning", none: "neutral" };

export default function UserRolesPage() {
  const { user: currentUser } = useAuth();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [role, setRole] = useState<string>("TenantAdmin");

  useEffect(() => {
    UsersApi.list()
      .then(setUsers)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, []);

  const active = (users ?? []).filter((u) => !u.isBlocked);
  const activeIn = (name: string) => active.filter((u) => roleOf(u) === name).length;
  const members = (users ?? []).filter((u) => roleOf(u) === role);
  const accessColumn = (name: (typeof ROLES)[number]) => ({
    key: name,
    header: roleLabel(name),
    render: (row: Area) => <StatusPill tone={LEVEL_TONE[row[name].level]} label={row[name].text} />,
  });

  return (
    <PageShell>
      <PageHeader
        icon="admin_panel_settings"
        title="Roles & access"
        subtitle="Who holds each role, and what that role lets them do in this workspace."
        actions={
          <MDButton component={Link} to="/users/bulk" variant="outlined" color="info" startIcon={<Icon>checklist_rtl</Icon>}>
            Change roles
          </MDButton>
        }
      />

      {loading && (
        <Section>
          <StateBlock kind="loading" title="Loading" />
        </Section>
      )}
      {error && (
        <Section>
          <StateBlock kind="error" title="Roles could not be loaded" message={error} />
        </Section>
      )}

      {!loading && !error && (
        <Box sx={{ display: "grid", gap: 3 }}>
          {activeIn("TenantAdmin") === 1 && (
            <InlineAlert tone="warning" title="Only one active company admin">
              They cannot be blocked or made an employee until someone else is a company admin too.
            </InlineAlert>
          )}

          <Box sx={{ display: "grid", gap: 3, gridTemplateColumns: { xs: "1fr", md: "repeat(3, 1fr)" } }}>
            <StatCard icon="admin_panel_settings" tone="primary" label="Active company admins" value={activeIn("TenantAdmin")} />
            <StatCard icon="badge" tone="info" label="Active employees" value={activeIn("Employee")} />
            <StatCard icon="block" tone="error" label="Blocked users" value={(users?.length ?? 0) - active.length} hint="Blocked users have no access, whatever their role." />
          </Box>

          <Section flush icon="rule" title="What each role can do" subtitle="These rules are fixed. To change what someone can do, change their role.">
            <SimpleTable
              rows={AREAS}
              getRowId={(row: Area) => row.area}
              columns={[{ key: "area", header: "Area", render: (row: Area) => row.area }, ...ROLES.map(accessColumn)]}
            />
          </Section>

          <Section flush icon="group" title="Members">
            <Box sx={{ px: 3, py: 2.5 }}>
              <FilterTabs
                label="Filter by role"
                value={role}
                onChange={setRole}
                options={ROLES.map((name) => ({
                  value: name,
                  label: roleLabel(name),
                  count: (users ?? []).filter((u) => roleOf(u) === name).length,
                }))}
              />
            </Box>
            <SimpleTable
              rows={members}
              getRowId={(u: UserSummary) => u.id}
              emptyMessage="Nobody has this role."
              columns={[
                {
                  key: "user",
                  header: "User",
                  render: (u: UserSummary) => (
                    <Identity name={u.displayName} secondary={u.id === currentUser?.id ? `${u.email} · you` : u.email} />
                  ),
                },
                {
                  key: "status",
                  header: "Status",
                  render: (u: UserSummary) => (
                    <StatusPill tone={u.isBlocked ? "error" : "success"} label={u.isBlocked ? "Blocked" : "Active"} />
                  ),
                },
              ]}
            />
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
