import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { FilterTabs, Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import BulkResultAlert from "../../components/BulkResultAlert";
import { useAuth } from "../../auth/useAuth";
import { ApiError } from "../../lib/api";
import { runBulk, type BulkResult } from "../../lib/bulk";
import { UsersApi } from "../../api/resources";
import type { UserSummary } from "../../api/types";

type StatusFilter = "all" | "active" | "blocked";
type ActionKey = "block" | "unblock" | "role";

const ROLES = ["TenantAdmin", "Employee"];
const roleOf = (user: UserSummary) => user.roles[0] ?? "Employee";

export default function UserBulkPage() {
  const { user: currentUser } = useAuth();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [newRole, setNewRole] = useState("Employee");
  const [pending, setPending] = useState<ActionKey | null>(null);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<{ outcome: BulkResult; done: string } | null>(null);

  const fetchData = useCallback(() => {
    UsersApi.list()
      .then(setUsers)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  const chosen = (users ?? []).filter((u) => selected.has(u.id));
  // Each action skips the selected users it would not change.
  const targets: Record<ActionKey, UserSummary[]> = {
    block: chosen.filter((u) => !u.isBlocked),
    unblock: chosen.filter((u) => u.isBlocked),
    role: chosen.filter((u) => roleOf(u) !== newRole),
  };
  const actions: Record<ActionKey, { label: string; color: "error" | "success" | "info"; run: (u: UserSummary) => Promise<void>; done: string; effect: string }> = {
    block: { label: "Block", color: "error", run: (u) => UsersApi.block(u.id), done: "blocked", effect: "They are signed out and cannot sign in." },
    unblock: { label: "Unblock", color: "success", run: (u) => UsersApi.unblock(u.id), done: "unblocked", effect: "They can sign in again." },
    role: {
      label: `Make ${roleLabel(newRole)}`,
      color: "info",
      run: (u) => UsersApi.changeRole(u.id, newRole),
      done: `changed to ${roleLabel(newRole)}`,
      effect: "They are signed out and get the new role when they sign back in.",
    },
  };

  const confirm = async () => {
    if (!pending) return;
    const action = actions[pending];
    const list = targets[pending];
    setPending(null);
    setRunning(true);
    const outcome = await runBulk(list, (u) => u.displayName, action.run);
    setResult({ outcome, done: `${outcome.succeeded === 1 ? "user" : "users"} ${action.done}` });
    setSelected(new Set());
    setRunning(false);
    fetchData();
  };

  const rows = (users ?? []).filter((u) => statusFilter === "all" || (statusFilter === "blocked") === u.isBlocked);
  const blockedCount = users?.filter((u) => u.isBlocked).length ?? 0;
  const pendingCount = pending ? targets[pending].length : 0;

  return (
    <PageShell>
      <PageHeader
        icon="checklist_rtl"
        title="Bulk actions"
        subtitle="Block, unblock or change the role of several users in one step."
        actions={
          <MDButton variant="outlined" color="info" onClick={fetchData} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <BulkResultAlert result={result?.outcome ?? null} done={result?.done ?? ""} />

      <Section
        flush
        title={`${chosen.length} selected`}
        subtitle="Each button counts the selected users it would change. You cannot select yourself."
        actions={
          <>
            <MDButton size="small" variant="gradient" color="error" disabled={running || targets.block.length === 0} onClick={() => setPending("block")}>
              Block ({targets.block.length})
            </MDButton>
            <MDButton size="small" variant="gradient" color="success" disabled={running || targets.unblock.length === 0} onClick={() => setPending("unblock")}>
              Unblock ({targets.unblock.length})
            </MDButton>
            <MDInput
              select
              size="small"
              SelectProps={{ native: true }}
              inputProps={{ "aria-label": "Role to set" }}
              value={newRole}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNewRole(e.target.value)}
            >
              {ROLES.map((role) => (
                <option key={role} value={role}>
                  {roleLabel(role)}
                </option>
              ))}
            </MDInput>
            <MDButton size="small" variant="gradient" color="info" disabled={running || targets.role.length === 0} onClick={() => setPending("role")}>
              Set role ({targets.role.length})
            </MDButton>
          </>
        }
      >
        {loading && <StateBlock kind="loading" title="Loading users" />}
        {error && <StateBlock kind="error" title="Users could not be loaded" message={error} />}
        {!loading && !error && (
          <>
            <Box sx={{ px: 3, py: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: users?.length },
                  { value: "active", label: "Active", count: (users?.length ?? 0) - blockedCount },
                  { value: "blocked", label: "Blocked", count: blockedCount },
                ]}
              />
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={(u: UserSummary) => u.id}
              getRowLabel={(u: UserSummary) => u.displayName}
              selection={{ selected, onChange: setSelected, isSelectable: (u: UserSummary) => u.id !== currentUser?.id }}
              emptyMessage="No users match this filter."
              columns={[
                {
                  key: "user",
                  header: "User",
                  render: (u: UserSummary) => (
                    <Identity name={u.displayName} secondary={u.id === currentUser?.id ? `${u.email} · you` : u.email} />
                  ),
                },
                { key: "role", header: "Role", render: (u: UserSummary) => roleLabel(roleOf(u)) },
                {
                  key: "status",
                  header: "Status",
                  render: (u: UserSummary) => (
                    <StatusPill tone={u.isBlocked ? "error" : "success"} label={u.isBlocked ? "Blocked" : "Active"} />
                  ),
                },
              ]}
            />
          </>
        )}
      </Section>

      <ConfirmDialog
        open={!!pending}
        title={pending ? `${actions[pending].label}: ${pendingCount} ${pendingCount === 1 ? "user" : "users"}` : ""}
        message={pending ? `${actions[pending].effect} Continue?` : ""}
        confirmLabel={pending ? actions[pending].label : "Confirm"}
        confirmColor={pending ? actions[pending].color : "info"}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
    </PageShell>
  );
}