import { useCallback, useEffect, useState } from "react";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogTitle from "@mui/material/DialogTitle";
import DialogContent from "@mui/material/DialogContent";
import DialogActions from "@mui/material/DialogActions";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { UsersApi } from "../api/resources";
import type { UserSummary } from "../api/types";

export default function UsersPage() {
  const { logout } = useAuth();
  const { notify } = useSnackbar();

  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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

  const load = () => {
    setLoading(true);
    setError(null);
    fetchData();
  };

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
      notify("Invitation sent.", "success");
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setInviteError(err instanceof ApiError ? err.message : "Invite failed.");
    }
  };

  const changeRole = async (targetUser: UserSummary, role: string) => {
    try {
      await UsersApi.changeRole(targetUser.id, role);
      notify(`${targetUser.displayName}'s role changed to ${role}.`, "success");
      load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Role change failed.", "error");
    }
  };

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

  const columns = [
    { Header: "Email", accessor: "email" },
    { Header: "Name", accessor: "name" },
    { Header: "Role", accessor: "role" },
    { Header: "Status", accessor: "status" },
    { Header: "Actions", accessor: "actions", align: "right" as const },
  ];

  const rows =
    users?.map((u) => ({
      email: u.email,
      name: u.displayName,
      role: (
        <MDInput
          select
          SelectProps={{ native: true }}
          size="small"
          value={u.roles[0] ?? "Employee"}
          onChange={(e: React.ChangeEvent<HTMLInputElement>) => changeRole(u, e.target.value)}
        >
          <option value="TenantAdmin">TenantAdmin</option>
          <option value="Employee">Employee</option>
        </MDInput>
      ),
      status: <Chip size="small" color={u.isBlocked ? "error" : "success"} label={u.isBlocked ? "Blocked" : "Active"} />,
      actions: (
        <MDButton size="small" variant="outlined" color={u.isBlocked ? "success" : "error"} onClick={() => setBlockTarget(u)}>
          {u.isBlocked ? "Unblock" : "Block"}
        </MDButton>
      ),
    })) ?? [];

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox display="flex" justifyContent="space-between" alignItems="center" p={3}>
            <MDTypography variant="h5">Users</MDTypography>
            <MDButton variant="gradient" color="info" onClick={openInvite}>
              Invite user
            </MDButton>
          </MDBox>
          {loading && (
            <MDBox p={3}>
              <MDTypography variant="body2">Loading users…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (users?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />

      <Dialog open={inviteOpen} onClose={() => setInviteOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Invite user</DialogTitle>
        <DialogContent>
          {inviteError && (
            <MDBox mb={2}>
              <MDTypography variant="caption" color="error">
                {inviteError}
              </MDTypography>
            </MDBox>
          )}
          {inviteLink ? (
            <MDBox>
              <MDTypography variant="body2" color="text">
                Invitation sent. Development accept link (email delivery is not configured in this
                environment):
              </MDTypography>
              <MDBox mt={1} p={1.5} sx={{ backgroundColor: "grey.100", borderRadius: 1, wordBreak: "break-all" }}>
                <MDTypography variant="caption">{inviteLink}</MDTypography>
              </MDBox>
            </MDBox>
          ) : (
            <>
              <MDBox mb={2} mt={1}>
                <MDInput
                  label="Email"
                  type="email"
                  fullWidth
                  value={inviteEmail}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setInviteEmail(e.target.value)}
                />
              </MDBox>
              <MDBox mb={1}>
                <MDInput
                  select
                  label="Role"
                  fullWidth
                  SelectProps={{ native: true }}
                  value={inviteRole}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setInviteRole(e.target.value)}
                >
                  <option value="Employee">Employee</option>
                  <option value="TenantAdmin">TenantAdmin</option>
                </MDInput>
              </MDBox>
            </>
          )}
        </DialogContent>
        <DialogActions>
          <MDButton variant="text" color="secondary" onClick={() => setInviteOpen(false)}>
            {inviteLink ? "Close" : "Cancel"}
          </MDButton>
          {!inviteLink && (
            <MDButton variant="gradient" color="info" onClick={submitInvite}>
              Send invite
            </MDButton>
          )}
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={!!blockTarget}
        title={blockTarget?.isBlocked ? "Unblock user" : "Block user"}
        message={`${blockTarget?.isBlocked ? "Restore" : "Revoke"} access for ${blockTarget?.displayName}?`}
        confirmLabel={blockTarget?.isBlocked ? "Unblock" : "Block"}
        confirmColor={blockTarget?.isBlocked ? "success" : "error"}
        onConfirm={confirmBlock}
        onCancel={() => setBlockTarget(null)}
      />
    </DashboardLayout>
  );
}
