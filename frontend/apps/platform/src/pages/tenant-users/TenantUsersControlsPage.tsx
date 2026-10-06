import { useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { Identity, InlineAlert, KitDialog, PageHeader, Section, SimpleTable, StateBlock, StatusPill, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { TenantUsersApi } from "../../api/resources";
import type { TenantUser } from "../../api/types";
import { CompanySelect, LinkBox, UnavailableAlert } from "./shared";
import { roleOf, rowId, useTenantUsers } from "./tenantUsers";

type Pending = { kind: "signOut" | "reset"; user: TenantUser };

export default function TenantUsersControlsPage() {
  const { notify } = useSnackbar();
  const { users, unavailable, error, loading, reload } = useTenantUsers();
  const [company, setCompany] = useState("all");
  const [pending, setPending] = useState<Pending | null>(null);
  const [resetLink, setResetLink] = useState<string | null>(null);

  const confirm = async () => {
    if (!pending) return;
    const { kind, user } = pending;
    setPending(null);
    try {
      if (kind === "signOut") {
        await TenantUsersApi.signOut(user);
        notify(`${user.displayName} signed out everywhere.`, "success");
      } else {
        const { devResetUrl } = await TenantUsersApi.forcePasswordReset(user);
        notify(`Password reset sent to ${user.email}.`, "success");
        setResetLink(devResetUrl);
      }
      reload();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Action failed.", "error");
    }
  };

  const copyResetLink = async () => {
    if (!resetLink) return;
    try {
      await navigator.clipboard.writeText(resetLink);
      notify("Link copied.", "success");
    } catch {
      notify("Could not copy the link. Select it and copy manually.", "error");
    }
  };

  const confirmText = {
    signOut: { title: "Sign out everywhere", label: "Sign out", color: "warning" as const },
    reset: { title: "Force password reset", label: "Reset password", color: "error" as const },
  };
  const confirmMessage = !pending
    ? ""
    : pending.kind === "signOut"
      ? `End every session ${pending.user.displayName} has open at ${pending.user.tenantName}? They can sign in again straight away.`
      : `Remove ${pending.user.displayName}'s password at ${pending.user.tenantName} and sign them out? They can only get back in through the reset link sent to ${pending.user.email}.`;

  const rows = (users ?? []).filter((u) => company === "all" || u.tenantId === company);

  return (
    <PageShell>
      <PageHeader
        icon="manage_accounts"
        title="Account controls"
        subtitle="Sign a company's user out everywhere, or force them to set a new password."
        actions={
          <MDButton variant="outlined" color="info" onClick={reload} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <UnavailableAlert unavailable={unavailable} />

      <Section flush subtitle="Blocked users have no sessions or working password to act on.">
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
                {
                  key: "actions",
                  header: "",
                  align: "right",
                  render: (u: TenantUser) => (
                    <Box sx={{ display: "inline-flex", flexWrap: "wrap", justifyContent: "flex-end", gap: 1 }}>
                      <MDButton
                        size="small"
                        variant="outlined"
                        color="warning"
                        disabled={u.isBlocked}
                        aria-label={`Sign out ${u.displayName} at ${u.tenantName}`}
                        onClick={() => setPending({ kind: "signOut", user: u })}
                      >
                        Sign out
                      </MDButton>
                      <MDButton
                        size="small"
                        variant="outlined"
                        color="error"
                        disabled={u.isBlocked}
                        aria-label={`Reset password for ${u.displayName} at ${u.tenantName}`}
                        onClick={() => setPending({ kind: "reset", user: u })}
                      >
                        Reset password
                      </MDButton>
                    </Box>
                  ),
                },
              ]}
            />
          </>
        )}
      </Section>

      <ConfirmDialog
        open={!!pending}
        title={pending ? confirmText[pending.kind].title : ""}
        message={confirmMessage}
        confirmLabel={pending ? confirmText[pending.kind].label : "Confirm"}
        confirmColor={pending ? confirmText[pending.kind].color : "error"}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />

      <KitDialog
        open={!!resetLink}
        onClose={() => setResetLink(null)}
        icon="lock_reset"
        tone="success"
        title="Password reset sent"
        subtitle="Share this link with the user so they can set a new password."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setResetLink(null)}>
              Close
            </MDButton>
            <MDButton variant="gradient" color="info" onClick={copyResetLink} startIcon={<Icon>content_copy</Icon>}>
              Copy link
            </MDButton>
          </>
        }
      >
        <InlineAlert tone="info" sx={{ mb: 2 }}>
          Email delivery is not configured in this environment, so the reset link is shown here instead.
        </InlineAlert>
        {resetLink && <LinkBox link={resetLink} />}
      </KitDialog>
    </PageShell>
  );
}
