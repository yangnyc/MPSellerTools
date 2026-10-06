import { useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import BulkResultAlert from "../../components/BulkResultAlert";
import { runBulk, type BulkResult } from "../../lib/bulk";
import { TenantUsersApi } from "../../api/resources";
import type { TenantUser } from "../../api/types";
import { CompanySelect, UnavailableAlert } from "./shared";
import { ROLES, roleOf, rowId, useTenantUsers } from "./tenantUsers";

type ActionKey = "block" | "unblock" | "role";

export default function TenantUsersBulkPage() {
  const { users, unavailable, error, loading, reload } = useTenantUsers();
  const [company, setCompany] = useState("all");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [newRole, setNewRole] = useState("Employee");
  const [pending, setPending] = useState<ActionKey | null>(null);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<{ outcome: BulkResult; done: string } | null>(null);

  const chosen = (users ?? []).filter((u) => selected.has(rowId(u)));
  // Each action skips the selected users it would not change.
  const targets: Record<ActionKey, TenantUser[]> = {
    block: chosen.filter((u) => !u.isBlocked),
    unblock: chosen.filter((u) => u.isBlocked),
    role: chosen.filter((u) => roleOf(u) !== newRole),
  };
  const actions: Record<ActionKey, { label: string; color: "error" | "success" | "info"; run: (u: TenantUser) => Promise<void>; done: string; effect: string }> = {
    block: { label: "Block", color: "error", run: TenantUsersApi.block, done: "blocked", effect: "They are signed out and cannot sign in." },
    unblock: { label: "Unblock", color: "success", run: TenantUsersApi.unblock, done: "unblocked", effect: "They can sign in again." },
    role: {
      label: `Make ${roleLabel(newRole)}`,
      color: "info",
      run: (u) => TenantUsersApi.changeRole(u, newRole),
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
    const outcome = await runBulk(list, (u) => `${u.displayName} (${u.tenantName})`, action.run);
    setResult({ outcome, done: `${outcome.succeeded === 1 ? "user" : "users"} ${action.done}` });
    setSelected(new Set());
    setRunning(false);
    reload();
  };

  const rows = (users ?? []).filter((u) => company === "all" || u.tenantId === company);
  const pendingCount = pending ? targets[pending].length : 0;

  return (
    <PageShell>
      <PageHeader
        icon="checklist_rtl"
        title="Bulk actions"
        subtitle="Block, unblock or change the role of several users, across companies, in one step."
        actions={
          <MDButton variant="outlined" color="info" onClick={reload} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <UnavailableAlert unavailable={unavailable} />
      <BulkResultAlert result={result?.outcome ?? null} done={result?.done ?? ""} />

      <Section
        flush
        title={`${chosen.length} selected`}
        subtitle="Each button counts the selected users it would change. A company refuses to lose its last active admin."
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
            <Box sx={{ px: 3, py: 2.5, maxWidth: 360 }}>
              <CompanySelect users={users ?? []} value={company} onChange={setCompany} />
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={rowId}
              getRowLabel={(u: TenantUser) => `${u.displayName} at ${u.tenantName}`}
              selection={{ selected, onChange: setSelected }}
              emptyMessage="No users to show."
              columns={[
                { key: "user", header: "User", render: (u: TenantUser) => <Identity name={u.displayName} secondary={u.email} /> },
                { key: "company", header: "Company", render: (u: TenantUser) => u.tenantName },
                { key: "role", header: "Role", render: (u: TenantUser) => roleLabel(roleOf(u)) },
                {
                  key: "status",
                  header: "Status",
                  render: (u: TenantUser) => (
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
