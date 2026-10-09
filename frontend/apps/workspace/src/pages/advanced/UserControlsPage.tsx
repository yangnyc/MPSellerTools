import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import {
  Identity,
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  SimpleTable,
  StateBlock,
  StatusPill,
  formatDateTime,
  roleLabel,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useAuth } from "../../auth/useAuth";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { UsersApi } from "../../api/resources";
import type { PendingInvitation, UserSummary } from "../../api/types";

type Pending =
  | { kind: "signOut"; user: UserSummary }
  | { kind: "reset"; user: UserSummary }
  | { kind: "revoke"; invitation: PendingInvitation };

export default function UserControlsPage() {
  const { c } = useKit();
  const { user: currentUser } = useAuth();
  const invitationsOn = !!currentUser?.invitationsEnabled;
  const { notify } = useSnackbar();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [invitations, setInvitations] = useState<PendingInvitation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [pending, setPending] = useState<Pending | null>(null);
  const [resetLink, setResetLink] = useState<string | null>(null);

  const fetchData = useCallback(() => {
    // With invitations off there are none to wait for, and none are asked for.
    Promise.all([UsersApi.list(), invitationsOn ? UsersApi.pendingInvitations() : Promise.resolve([])])
      .then(([userList, invitationList]) => {
        setUsers(userList);
        setInvitations(invitationList);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, [invitationsOn]);

  useEffect(fetchData, [fetchData]);

  const confirm = async () => {
    if (!pending) return;
    const action = pending;
    setPending(null);
    try {
      if (action.kind === "signOut") {
        await UsersApi.signOut(action.user.id);
        notify(`${action.user.displayName} signed out everywhere.`, "success");
      } else if (action.kind === "reset") {
        const { devResetUrl } = await UsersApi.forcePasswordReset(action.user.id);
        notify(`Password reset sent to ${action.user.email}.`, "success");
        setResetLink(devResetUrl);
      } else {
        await UsersApi.revokeInvitation(action.invitation.id);
        notify(`Invitation for ${action.invitation.email} revoked.`, "success");
      }
      fetchData();
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

  const confirmText: Record<Pending["kind"], { title: string; label: string; color: "error" | "warning" }> = {
    signOut: { title: "Sign out everywhere", label: "Sign out", color: "warning" },
    reset: { title: "Force password reset", label: "Reset password", color: "error" },
    revoke: { title: "Revoke invitation", label: "Revoke", color: "error" },
  };
  const confirmMessage = !pending
    ? ""
    : pending.kind === "signOut"
      ? `End every session ${pending.user.displayName} has open? They can sign in again straight away.`
      : pending.kind === "reset"
        ? `Remove ${pending.user.displayName}'s password and sign them out? They can only get back in through the reset link sent to ${pending.user.email}.`
        : `Revoke the invitation for ${pending.invitation.email}? Its link stops working.`;

  return (
    <PageShell>
      <PageHeader
        icon="manage_accounts"
        title="Account controls"
        subtitle={`Sign users out and force a password reset${invitationsOn ? ", and manage invitations that have not been accepted" : ""}.`}
        actions={
          <MDButton variant="outlined" color="info" onClick={fetchData} startIcon={<Icon>refresh</Icon>}>
            Refresh
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
          <StateBlock kind="error" title="Account controls could not be loaded" message={error} />
        </Section>
      )}

      {!loading && !error && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Section flush icon="group" title="Users" subtitle="Blocked users have no sessions or working password to act on.">
            <SimpleTable
              rows={users ?? []}
              getRowId={(u: UserSummary) => u.id}
              emptyMessage="No users yet."
              columns={[
                {
                  key: "user",
                  header: "User",
                  render: (u: UserSummary) => (
                    <Identity name={u.displayName} secondary={u.id === currentUser?.id ? `${u.email} · you` : u.email} />
                  ),
                },
                { key: "role", header: "Role", render: (u: UserSummary) => roleLabel(u.roles[0] ?? "Employee") },
                {
                  key: "status",
                  header: "Status",
                  render: (u: UserSummary) => (
                    <StatusPill tone={u.isBlocked ? "error" : "success"} label={u.isBlocked ? "Blocked" : "Active"} />
                  ),
                },
                {
                  key: "actions",
                  header: "",
                  align: "right",
                  render: (u: UserSummary) => (
                    <Box sx={{ display: "inline-flex", flexWrap: "wrap", justifyContent: "flex-end", gap: 1 }}>
                      <MDButton
                        size="small"
                        variant="outlined"
                        color="warning"
                        disabled={u.isBlocked}
                        aria-label={`Sign out ${u.displayName}`}
                        onClick={() => setPending({ kind: "signOut", user: u })}
                      >
                        Sign out
                      </MDButton>
                      <MDButton
                        size="small"
                        variant="outlined"
                        color="error"
                        disabled={u.isBlocked || u.id === currentUser?.id}
                        aria-label={`Reset password for ${u.displayName}`}
                        onClick={() => setPending({ kind: "reset", user: u })}
                      >
                        Reset password
                      </MDButton>
                    </Box>
                  ),
                },
              ]}
            />
          </Section>

          {invitationsOn && (
          <Section flush icon="mail" title="Pending invitations" subtitle="Sent, but not accepted yet.">
            <SimpleTable
              rows={invitations ?? []}
              getRowId={(i: PendingInvitation) => i.id}
              emptyMessage="No invitations are waiting to be accepted."
              columns={[
                { key: "email", header: "Email", render: (i: PendingInvitation) => i.email },
                { key: "role", header: "Role", render: (i: PendingInvitation) => roleLabel(i.role) },
                {
                  key: "sent",
                  header: "Sent",
                  render: (i: PendingInvitation) => <span title={formatDateTime(i.createdAtUtc)}>{timeAgo(i.createdAtUtc)}</span>,
                },
                {
                  key: "expires",
                  header: "Expires",
                  render: (i: PendingInvitation) =>
                    i.isExpired ? (
                      <StatusPill tone="neutral" label="Expired" />
                    ) : (
                      <span title={formatDateTime(i.expiresAtUtc)}>{timeAgo(i.expiresAtUtc)}</span>
                    ),
                },
                {
                  key: "actions",
                  header: "",
                  align: "right",
                  render: (i: PendingInvitation) => (
                    <MDButton
                      size="small"
                      variant="outlined"
                      color="error"
                      aria-label={`Revoke invitation for ${i.email}`}
                      onClick={() => setPending({ kind: "revoke", invitation: i })}
                    >
                      Revoke
                    </MDButton>
                  ),
                },
              ]}
            />
          </Section>
          )}
        </Box>
      )}

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
          {resetLink}
        </Box>
      </KitDialog>
    </PageShell>
  );
}