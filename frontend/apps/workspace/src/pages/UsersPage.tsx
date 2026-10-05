import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  FilterTabs,
  Identity,
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  StateBlock,
  StatusPill,
  roleLabel,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { UsersApi } from "../api/resources";
import type { UserSummary } from "../api/types";

type StatusFilter = "all" | "active" | "blocked";

const ROLES = ["TenantAdmin", "Employee"];

export default function UsersPage() {
  const { user: currentUser, logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();

  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  const [inviteOpen, setInviteOpen] = useState(false);
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState("Employee");
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [inviteLink, setInviteLink] = useState<string | null>(null);

  const [blockTarget, setBlockTarget] = useState<UserSummary | null>(null);

  const fetchData = useCallback(() => {
    UsersApi.list()
      .then(setUsers)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    fetchData();
  }, [fetchData]);

  const openInvite = () => {
    setInviteEmail("");
    setInviteRole("Employee");
    setInviteError(null);
    setInviteLink(null);
    setInviteOpen(true);
  };

  const submitInvite = async () => {
    if (!inviteEmail.trim()) {
      setInviteError("Email is required.");
      return;
    }
    try {
      const result = await UsersApi.invite({ email: inviteEmail, role: inviteRole });
      setInviteLink(result.devAcceptUrl);
      setInviteError(null);
      notify("Invitation sent.", "success");
      if (!result.devAcceptUrl) {
        setInviteOpen(false);
      }
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setInviteError(err instanceof ApiError ? err.message : "Invite failed.");
    }
  };

  const copyInviteLink = async () => {
    if (!inviteLink) return;
    try {
      await navigator.clipboard.writeText(inviteLink);
      notify("Link copied.", "success");
    } catch {
      notify("Could not copy the link. Select it and copy manually.", "error");
    }
  };

  const changeRole = useCallback(
    async (targetUser: UserSummary, role: string) => {
      try {
        await UsersApi.changeRole(targetUser.id, role);
        notify(`${targetUser.displayName}'s role changed to ${roleLabel(role)}.`, "success");
        load();
      } catch (err) {
        notify(err instanceof ApiError ? err.message : "Role change failed.", "error");
      }
    },
    [notify, load]
  );

  const confirmBlock = async () => {
    if (!blockTarget) return;
    try {
      if (blockTarget.isBlocked) {
        await UsersApi.unblock(blockTarget.id);
        notify(`${blockTarget.displayName} unblocked.`, "success");
      } else {
        await UsersApi.block(blockTarget.id);
        notify(`${blockTarget.displayName} blocked.`, "success");
      }
      setBlockTarget(null);
      load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Action failed.", "error");
      setBlockTarget(null);
    }
  };

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = { user: UserSummary; name: string; email: string; role: string; statusLabel: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (users ?? [])
      .filter((u) => statusFilter === "all" || (statusFilter === "blocked") === u.isBlocked)
      .map((u) => ({
        user: u,
        name: u.displayName,
        email: u.email,
        role: u.roles[0] ?? "Employee",
        statusLabel: u.isBlocked ? "Blocked" : "Active",
      }));

    const columns = [
      {
        Header: "User",
        id: "user",
        // Name and email together, so searching and sorting cover both.
        accessor: (row: Row) => `${row.name} ${row.email}`,
        Cell: ({ row }: CellProps) => (
          <Identity
            name={row.original.name}
            secondary={row.original.user.id === currentUser?.id ? `${row.original.email} · you` : row.original.email}
          />
        ),
      },
      {
        Header: "Role",
        accessor: "role",
        Cell: ({ row }: CellProps) => (
          <MDInput
            select
            SelectProps={{ native: true }}
            size="small"
            inputProps={{ "aria-label": `Role for ${row.original.name}` }}
            value={row.original.role}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => changeRole(row.original.user, e.target.value)}
          >
            {ROLES.map((role) => (
              <option key={role} value={role}>
                {roleLabel(role)}
              </option>
            ))}
          </MDInput>
        ),
      },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill tone={row.original.user.isBlocked ? "error" : "success"} label={row.original.statusLabel} />
        ),
      },
      {
        Header: "Actions",
        id: "actions",
        accessor: "name",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <MDButton
            size="small"
            variant="outlined"
            color={row.original.user.isBlocked ? "success" : "error"}
            onClick={() => setBlockTarget(row.original.user)}
          >
            {row.original.user.isBlocked ? "Unblock" : "Block"}
          </MDButton>
        ),
      },
    ];
    return { columns, rows };
  }, [users, statusFilter, currentUser?.id, changeRole]);

  const blockedCount = users?.filter((u) => u.isBlocked).length;

  return (
    <PageShell>
      <PageHeader
        icon="group"
        title="Users"
        subtitle="Invite teammates, set their role, and block access when someone leaves."
        actions={
          <MDButton variant="gradient" color="info" onClick={openInvite} startIcon={<Icon>person_add</Icon>}>
            Invite user
          </MDButton>
        }
      />

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading users" />}
        {error && (
          <StateBlock
            kind="error"
            title="Users could not be loaded"
            message={error}
            action={
              <MDButton variant="outlined" color="info" size="small" onClick={load}>
                Try again
              </MDButton>
            }
          />
        )}
        {!loading && !error && (users?.length ?? 0) > 0 && (
          <>
            <Box sx={{ px: 3, pt: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: users?.length },
                  { value: "active", label: "Active", count: (users?.length ?? 0) - (blockedCount ?? 0) },
                  { value: "blocked", label: "Blocked", count: blockedCount },
                ]}
              />
            </Box>
            <DataTable table={table} canSearch />
          </>
        )}
      </Section>

      <KitDialog
        open={inviteOpen}
        onClose={() => setInviteOpen(false)}
        onSubmit={inviteLink ? undefined : submitInvite}
        icon={inviteLink ? "mark_email_read" : "person_add"}
        tone={inviteLink ? "success" : "info"}
        title={inviteLink ? "Invitation sent" : "Invite user"}
        subtitle={
          inviteLink
            ? "Share this link with the new user so they can set a password."
            : "They receive a single-use link to set their own password."
        }
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setInviteOpen(false)}>
              {inviteLink ? "Close" : "Cancel"}
            </MDButton>
            {inviteLink ? (
              <MDButton variant="gradient" color="info" onClick={copyInviteLink} startIcon={<Icon>content_copy</Icon>}>
                Copy link
              </MDButton>
            ) : (
              <MDButton type="submit" variant="gradient" color="info">
                Send invite
              </MDButton>
            )}
          </>
        }
      >
        {inviteError && <InlineAlert sx={{ mb: 2.5 }}>{inviteError}</InlineAlert>}
        {inviteLink ? (
          <>
            <InlineAlert tone="info" sx={{ mb: 2 }}>
              Email delivery is not configured in this environment, so the accept link is shown here instead.
            </InlineAlert>
            <Box
              sx={{
                p: 1.5,
                borderRadius: "10px",
                fontFamily: "monospace",
                fontSize: "0.8125rem",
                wordBreak: "break-all",
                color: c.text,
                backgroundColor: c.surfaceAlt,
                border: `1px solid ${c.border}`,
              }}
            >
              {inviteLink}
            </Box>
          </>
        ) : (
          <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
            <MDInput
              label="Email"
              type="email"
              fullWidth
              value={inviteEmail}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setInviteEmail(e.target.value)}
            />
            <MDInput
              select
              label="Role"
              fullWidth
              SelectProps={{ native: true }}
              value={inviteRole}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setInviteRole(e.target.value)}
            >
              <option value="Employee">{roleLabel("Employee")}</option>
              <option value="TenantAdmin">{roleLabel("TenantAdmin")}</option>
            </MDInput>
          </Box>
        )}
      </KitDialog>

      <ConfirmDialog
        open={!!blockTarget}
        title={blockTarget?.isBlocked ? "Unblock user" : "Block user"}
        message={`${blockTarget?.isBlocked ? "Restore" : "Revoke"} access for ${blockTarget?.displayName}?`}
        confirmLabel={blockTarget?.isBlocked ? "Unblock" : "Block"}
        confirmColor={blockTarget?.isBlocked ? "success" : "error"}
        onConfirm={confirmBlock}
        onCancel={() => setBlockTarget(null)}
      />
    </PageShell>
  );
}
