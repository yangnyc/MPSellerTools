import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import {
  FilterTabs,
  InlineAlert,
  PageHeader,
  Section,
  SimpleTable,
  StateBlock,
  StatusPill,
  downloadCsv,
  formatDateTime,
  roleLabel,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import BulkResultAlert from "../../components/BulkResultAlert";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { runBulk, type BulkResult } from "../../lib/bulk";
import { UsersApi } from "../../api/resources";
import type { PendingInvitation, UserSummary } from "../../api/types";

type ChangeEvent = React.ChangeEvent<HTMLInputElement>;
type ExpiryFilter = "all" | "waiting" | "expired";
type ActionKey = "resend" | "revoke";
type SentLink = { email: string; url: string };

const ROLES = ["Employee", "TenantAdmin"];
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function UserInvitationsPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [invitations, setInvitations] = useState<PendingInvitation[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [emailText, setEmailText] = useState("");
  const [role, setRole] = useState("Employee");
  const [expiryFilter, setExpiryFilter] = useState<ExpiryFilter>("all");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [pending, setPending] = useState<ActionKey | null>(null);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<{ outcome: BulkResult; done: string } | null>(null);
  const [links, setLinks] = useState<SentLink[]>([]);

  const fetchData = useCallback(() => {
    Promise.all([UsersApi.list(), UsersApi.pendingInvitations()])
      .then(([userList, invitationList]) => {
        setUsers(userList);
        setInvitations(invitationList);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load invitations."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  // Sort what was typed into the addresses worth inviting and the ones to skip.
  const typed = [...new Set(emailText.split(/[\s,;]+/).map((value) => value.trim().toLowerCase()).filter(Boolean))];
  const userEmails = new Set((users ?? []).map((u) => u.email.toLowerCase()));
  const waitingEmails = new Set((invitations ?? []).filter((i) => !i.isExpired).map((i) => i.email.toLowerCase()));
  const invalid = typed.filter((email) => !EMAIL.test(email));
  const alreadyUsers = typed.filter((email) => userEmails.has(email));
  const alreadyInvited = typed.filter((email) => !userEmails.has(email) && waitingEmails.has(email));
  const ready = typed.filter((email) => EMAIL.test(email) && !userEmails.has(email) && !waitingEmails.has(email));
  const skipped = [
    { label: "already a user", emails: alreadyUsers },
    { label: "already invited", emails: alreadyInvited },
    { label: "not an email address", emails: invalid },
  ].filter((group) => group.emails.length > 0);

  const invite = async (email: string, inviteRole: string, sent: SentLink[]) => {
    const { devAcceptUrl } = await UsersApi.invite({ email, role: inviteRole });
    if (devAcceptUrl) {
      sent.push({ email, url: new URL(devAcceptUrl, window.location.origin).toString() });
    }
  };

  const finish = (outcome: BulkResult, done: string, sent: SentLink[]) => {
    setResult({ outcome, done: `${outcome.succeeded === 1 ? "invitation" : "invitations"} ${done}` });
    setLinks(sent);
    setSelected(new Set());
    setRunning(false);
    fetchData();
  };

  const sendInvitations = async () => {
    setRunning(true);
    const sent: SentLink[] = [];
    const outcome = await runBulk(ready, (email) => email, (email) => invite(email, role, sent));
    if (outcome.failures.length === 0) {
      setEmailText("");
    }
    finish(outcome, "sent", sent);
  };

  const chosen = (invitations ?? []).filter((i) => selected.has(i.id));
  const actions: Record<ActionKey, { label: string; color: "info" | "error"; effect: string }> = {
    resend: { label: "Resend", color: "info", effect: "Each gets a new link that lasts seven days, and the old link stops working." },
    revoke: { label: "Revoke", color: "error", effect: "Their links stop working." },
  };

  const confirm = async () => {
    if (!pending) return;
    const action = pending;
    setPending(null);
    setRunning(true);
    const sent: SentLink[] = [];
    const outcome = await runBulk(
      chosen,
      (i) => i.email,
      async (i) => {
        // The new invitation first, so a failure never leaves the person with none.
        if (action === "resend") {
          await invite(i.email, i.role, sent);
        }
        await UsersApi.revokeInvitation(i.id);
      }
    );
    finish(outcome, action === "resend" ? "resent" : "revoked", sent);
  };

  const copyLinks = async () => {
    try {
      await navigator.clipboard.writeText(links.map((link) => `${link.email}\t${link.url}`).join("\n"));
      notify(links.length === 1 ? "Link copied." : `${links.length} links copied.`, "success");
    } catch {
      notify("Could not copy the links. Select them and copy manually.", "error");
    }
  };

  const expiredCount = invitations?.filter((i) => i.isExpired).length ?? 0;
  const rows = (invitations ?? []).filter((i) => expiryFilter === "all" || (expiryFilter === "expired") === i.isExpired);
  const exportCsv = () =>
    downloadCsv(
      "invitations.csv",
      ["Email", "Role", "Sent", "Expires", "Status"],
      rows.map((i) => [i.email, roleLabel(i.role), i.createdAtUtc, i.expiresAtUtc, i.isExpired ? "Expired" : "Waiting"])
    );

  return (
    <PageShell>
      <PageHeader
        icon="mail"
        title="Invitations"
        subtitle="Invite several people at once, and resend or revoke invitations nobody has accepted yet."
        actions={
          <MDButton variant="outlined" color="info" onClick={fetchData} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <BulkResultAlert result={result?.outcome ?? null} done={result?.done ?? ""} />

      {loading && (
        <Section>
          <StateBlock kind="loading" title="Loading" />
        </Section>
      )}
      {error && (
        <Section>
          <StateBlock kind="error" title="Invitations could not be loaded" message={error} />
        </Section>
      )}

      {!loading && !error && (
        <Box sx={{ display: "grid", gap: 3 }}>
          {links.length > 0 && (
            <Section
              flush
              icon="link"
              tone="success"
              title="Invitation links"
              subtitle="Email delivery is not configured in this environment, so the links are shown here instead. They are not shown again."
              actions={
                <MDButton size="small" variant="gradient" color="info" onClick={copyLinks} startIcon={<Icon>content_copy</Icon>}>
                  Copy all
                </MDButton>
              }
            >
              <SimpleTable
                rows={links}
                getRowId={(link: SentLink) => link.url}
                columns={[
                  { key: "email", header: "Email", render: (link: SentLink) => link.email },
                  {
                    key: "link",
                    header: "Link",
                    render: (link: SentLink) => (
                      <Box sx={{ fontFamily: "monospace", fontSize: "0.8125rem", wordBreak: "break-all", color: c.text }}>{link.url}</Box>
                    ),
                  },
                ]}
              />
            </Section>
          )}

          <Section
            icon="group_add"
            title="Invite people"
            subtitle="Each person receives a single-use link to set their own password."
          >
            <Box sx={{ display: "grid", gap: 2, alignItems: "start", gridTemplateColumns: { xs: "1fr", md: "3fr 1fr" } }}>
              <MDInput
                label="Email addresses"
                multiline
                rows={4}
                fullWidth
                placeholder="One per line, or separated by commas"
                value={emailText}
                onChange={(e: ChangeEvent) => setEmailText(e.target.value)}
              />
              <Box sx={{ display: "grid", gap: 2 }}>
                <MDInput
                  select
                  label="Role"
                  fullWidth
                  SelectProps={{ native: true }}
                  value={role}
                  onChange={(e: ChangeEvent) => setRole(e.target.value)}
                >
                  {ROLES.map((value) => (
                    <option key={value} value={value}>
                      {roleLabel(value)}
                    </option>
                  ))}
                </MDInput>
                <MDButton
                  variant="gradient"
                  color="info"
                  disabled={running || ready.length === 0}
                  onClick={sendInvitations}
                  startIcon={<Icon>send</Icon>}
                >
                  Send invitations ({ready.length})
                </MDButton>
              </Box>
            </Box>
            {skipped.length > 0 && (
              <InlineAlert tone="warning" title="Some addresses will be skipped" sx={{ mt: 2 }}>
                <ul style={{ margin: "0.25rem 0 0", paddingLeft: "1.25rem" }}>
                  {skipped.map((group) => (
                    <li key={group.label}>
                      {group.emails.join(", ")}: {group.label}
                    </li>
                  ))}
                </ul>
              </InlineAlert>
            )}
          </Section>

          <Section
            flush
            icon="schedule_send"
            title={`Pending invitations: ${chosen.length} selected`}
            subtitle="Sent, but not accepted yet."
            actions={
              <>
                <MDButton size="small" variant="gradient" color="info" disabled={running || chosen.length === 0} onClick={() => setPending("resend")}>
                  Resend ({chosen.length})
                </MDButton>
                <MDButton size="small" variant="gradient" color="error" disabled={running || chosen.length === 0} onClick={() => setPending("revoke")}>
                  Revoke ({chosen.length})
                </MDButton>
                <MDButton size="small" variant="outlined" color="info" disabled={rows.length === 0} onClick={exportCsv} startIcon={<Icon>file_download</Icon>}>
                  Export CSV
                </MDButton>
              </>
            }
          >
            <Box sx={{ px: 3, py: 2.5 }}>
              <FilterTabs
                label="Filter by expiry"
                value={expiryFilter}
                onChange={setExpiryFilter}
                options={[
                  { value: "all", label: "All", count: invitations?.length },
                  { value: "waiting", label: "Waiting", count: (invitations?.length ?? 0) - expiredCount },
                  { value: "expired", label: "Expired", count: expiredCount },
                ]}
              />
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={(i: PendingInvitation) => i.id}
              getRowLabel={(i: PendingInvitation) => i.email}
              selection={{ selected, onChange: setSelected }}
              emptyMessage={invitations?.length ? "No invitations match this filter." : "No invitations are waiting to be accepted."}
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
              ]}
            />
          </Section>
        </Box>
      )}

      <ConfirmDialog
        open={!!pending}
        title={pending ? `${actions[pending].label}: ${chosen.length} ${chosen.length === 1 ? "invitation" : "invitations"}` : ""}
        message={pending ? `${actions[pending].effect} Continue?` : ""}
        confirmLabel={pending ? actions[pending].label : "Confirm"}
        confirmColor={pending ? actions[pending].color : "info"}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
    </PageShell>
  );
}
