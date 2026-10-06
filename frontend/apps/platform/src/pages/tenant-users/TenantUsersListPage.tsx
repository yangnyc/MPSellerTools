import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import { FilterTabs, Identity, InlineAlert, KitDialog, PageHeader, Section, StateBlock, StatusPill, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { TenantUsersApi, TenantsApi } from "../../api/resources";
import type { TenantSummary, TenantUser } from "../../api/types";
import { LinkBox, UnavailableAlert } from "./shared";
import { ROLES, roleOf, useTenantUsers } from "./tenantUsers";

type StatusFilter = "all" | "active" | "blocked";

const ACTIVE = 1;

// 16 random characters with at least one of each kind the companies require.
// Look-alike characters (0/O, 1/l/I) are left out, as it is read out or retyped.
function generatePassword(): string {
  const sets = ["ABCDEFGHJKLMNPQRSTUVWXYZ", "abcdefghijkmnpqrstuvwxyz", "23456789"];
  const all = sets.join("");
  const random = (max: number) => {
    // Rejection sampling, so every character is equally likely.
    const limit = Math.floor(0x100000000 / max) * max;
    const buffer = new Uint32Array(1);
    do {
      crypto.getRandomValues(buffer);
    } while (buffer[0] >= limit);
    return buffer[0] % max;
  };
  const chars = sets.map((set) => set[random(set.length)]);
  while (chars.length < 16) {
    chars.push(all[random(all.length)]);
  }
  for (let i = chars.length - 1; i > 0; i--) {
    const j = random(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join("");
}

export default function TenantUsersListPage() {
  const { notify } = useSnackbar();
  const { users, unavailable, error, loading, reload } = useTenantUsers();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  // Only a running company can be asked to add a user.
  const [companies, setCompanies] = useState<TenantSummary[]>([]);
  useEffect(() => {
    TenantsApi.list()
      .then((tenants) => setCompanies(tenants.filter((t) => t.status === ACTIVE)))
      .catch(() => setCompanies([]));
  }, []);

  const [addOpen, setAddOpen] = useState(false);
  const [addCompany, setAddCompany] = useState("");
  const [addEmail, setAddEmail] = useState("");
  const [addRole, setAddRole] = useState("Employee");
  const [addError, setAddError] = useState<string | null>(null);
  const [addLink, setAddLink] = useState<string | null>(null);
  // Only used by Create; left empty, one is generated.
  const [addPassword, setAddPassword] = useState("");
  // Once created, the dialog shows the sign-in details for the admin to pass on.
  const [addCreated, setAddCreated] = useState<{ email: string; password: string } | null>(null);

  const [editTarget, setEditTarget] = useState<TenantUser | null>(null);
  const [editName, setEditName] = useState("");
  const [editEmail, setEditEmail] = useState("");
  const [editRole, setEditRole] = useState("Employee");
  const [editError, setEditError] = useState<string | null>(null);

  const [deleteTarget, setDeleteTarget] = useState<TenantUser | null>(null);
  const [saving, setSaving] = useState(false);

  const [passwordTarget, setPasswordTarget] = useState<TenantUser | null>(null);
  const [password, setPassword] = useState("");
  const [passwordShown, setPasswordShown] = useState(false);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  // Once saved, the dialog shows the sign-in details for the admin to pass on.
  const [passwordSaved, setPasswordSaved] = useState(false);

  const openPassword = useCallback((user: TenantUser) => {
    setPassword("");
    setPasswordShown(false);
    setPasswordError(null);
    setPasswordSaved(false);
    setPasswordTarget(user);
  }, []);

  const submitPassword = async () => {
    if (!passwordTarget) return;
    if (!password) {
      setPasswordError("Enter a password, or generate one.");
      return;
    }
    setSaving(true);
    try {
      await TenantUsersApi.setPassword(passwordTarget, password);
      notify(`Password set for ${passwordTarget.displayName}.`, "success");
      setPasswordError(null);
      setPasswordSaved(true);
    } catch (err) {
      setPasswordError(err instanceof ApiError ? err.message : "Could not set the password.");
    } finally {
      setSaving(false);
    }
  };

  const copyCredentials = async () => {
    if (!passwordTarget) return;
    try {
      await navigator.clipboard.writeText(`Username: ${passwordTarget.email}\nPassword: ${password}`);
      notify("Sign-in details copied.", "success");
    } catch {
      notify("Could not copy. Select the text and copy manually.", "error");
    }
  };

  // The password exists only in this dialog; closing it forgets it.
  const closePassword = () => {
    setPasswordTarget(null);
    setPassword("");
  };

  const openAdd = () => {
    setAddCompany(companies[0]?.id ?? "");
    setAddEmail("");
    setAddRole("Employee");
    setAddError(null);
    setAddLink(null);
    setAddPassword("");
    setAddCreated(null);
    setAddOpen(true);
  };

  // The password exists only in this dialog; closing it forgets it.
  const closeAdd = () => {
    setAddOpen(false);
    setAddPassword("");
    setAddCreated(null);
  };

  const validateAdd = () => {
    if (!addCompany) {
      setAddError("Choose a company.");
      return false;
    }
    if (!addEmail.trim()) {
      setAddError("Email is required.");
      return false;
    }
    return true;
  };

  const submitCreate = async () => {
    if (!validateAdd()) return;
    const email = addEmail.trim();
    const password = addPassword || generatePassword();
    setSaving(true);
    try {
      await TenantUsersApi.create(addCompany, { email, role: addRole, password });
      notify(`${email} created.`, "success");
      setAddError(null);
      setAddCreated({ email, password });
      reload();
    } catch (err) {
      setAddError(err instanceof ApiError ? err.message : "Create failed.");
    } finally {
      setSaving(false);
    }
  };

  const copyCreated = async () => {
    if (!addCreated) return;
    try {
      await navigator.clipboard.writeText(`Username: ${addCreated.email}\nPassword: ${addCreated.password}`);
      notify("Sign-in details copied.", "success");
    } catch {
      notify("Could not copy. Select the text and copy manually.", "error");
    }
  };

  const submitAdd = async () => {
    if (!validateAdd()) return;
    setSaving(true);
    try {
      const { devAcceptUrl } = await TenantUsersApi.invite(addCompany, { email: addEmail.trim(), role: addRole });
      notify(`Invitation sent to ${addEmail.trim()}.`, "success");
      setAddError(null);
      setAddLink(devAcceptUrl);
      if (!devAcceptUrl) {
        setAddOpen(false);
      }
    } catch (err) {
      setAddError(err instanceof ApiError ? err.message : "Invite failed.");
    } finally {
      setSaving(false);
    }
  };

  const copyAddLink = async () => {
    if (!addLink) return;
    try {
      await navigator.clipboard.writeText(addLink);
      notify("Link copied.", "success");
    } catch {
      notify("Could not copy the link. Select it and copy manually.", "error");
    }
  };

  const openEdit = useCallback((user: TenantUser) => {
    setEditName(user.displayName);
    setEditEmail(user.email);
    setEditRole(roleOf(user));
    setEditError(null);
    setEditTarget(user);
  }, []);

  const submitEdit = async () => {
    if (!editTarget) return;
    const displayName = editName.trim();
    const email = editEmail.trim();
    if (!displayName || !email) {
      setEditError("Name and email are required.");
      return;
    }
    setSaving(true);
    try {
      if (displayName !== editTarget.displayName || email !== editTarget.email) {
        await TenantUsersApi.update(editTarget, { displayName, email });
      }
      if (editRole !== roleOf(editTarget)) {
        await TenantUsersApi.changeRole(editTarget, editRole);
      }
      notify(`${displayName} updated.`, "success");
      setEditTarget(null);
    } catch (err) {
      setEditError(err instanceof ApiError ? err.message : "Update failed.");
    } finally {
      setSaving(false);
      // Also after a failure: the details may have been saved before the role was refused.
      reload();
    }
  };

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    const user = deleteTarget;
    setDeleteTarget(null);
    try {
      await TenantUsersApi.remove(user);
      notify(`${user.displayName} deleted from ${user.tenantName}.`, "success");
      reload();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Delete failed.", "error");
    }
  };

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = { user: TenantUser; company: string; role: string; statusLabel: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (users ?? [])
      .filter((u) => statusFilter === "all" || (statusFilter === "blocked") === u.isBlocked)
      .map((u) => ({ user: u, company: u.tenantName, role: roleLabel(roleOf(u)), statusLabel: u.isBlocked ? "Blocked" : "Active" }));

    const columns = [
      {
        Header: "User",
        id: "user",
        // Name and email together, so searching and sorting cover both.
        accessor: (row: Row) => `${row.user.displayName} ${row.user.email}`,
        Cell: ({ row }: CellProps) => <Identity name={row.original.user.displayName} secondary={row.original.user.email} />,
      },
      {
        Header: "Company",
        accessor: "company",
        Cell: ({ row }: CellProps) => <Identity name={row.original.company} secondary={row.original.user.tenantSlug} square />,
      },
      { Header: "Role", accessor: "role" },
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
        accessor: "company",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => {
          const u = row.original.user;
          return (
            <Box sx={{ display: "inline-flex", flexWrap: "wrap", justifyContent: "flex-end", gap: 1 }}>
              <MDButton
                size="small"
                variant="outlined"
                color="info"
                aria-label={`Edit ${u.displayName} at ${u.tenantName}`}
                onClick={() => openEdit(u)}
              >
                Edit
              </MDButton>
              <MDButton
                size="small"
                variant="outlined"
                color="warning"
                aria-label={`Set password for ${u.displayName} at ${u.tenantName}`}
                onClick={() => openPassword(u)}
              >
                Set password
              </MDButton>
              <MDButton
                size="small"
                variant="outlined"
                color="error"
                aria-label={`Delete ${u.displayName} at ${u.tenantName}`}
                onClick={() => setDeleteTarget(u)}
              >
                Delete
              </MDButton>
            </Box>
          );
        },
      },
    ];
    return { columns, rows };
  }, [users, statusFilter, openEdit, openPassword]);

  const blockedCount = users?.filter((u) => u.isBlocked).length ?? 0;

  return (
    <PageShell>
      <PageHeader
        icon="groups"
        title="Tenant users"
        subtitle="The users of every company, read from each company's own workspace. Add, edit or delete them here."
        actions={
          <Box sx={{ display: "inline-flex", flexWrap: "wrap", gap: 1 }}>
            <MDButton variant="outlined" color="info" onClick={reload} startIcon={<Icon>refresh</Icon>}>
              Refresh
            </MDButton>
            <MDButton variant="gradient" color="info" onClick={openAdd} startIcon={<Icon>person_add</Icon>}>
              Add user
            </MDButton>
          </Box>
        }
      />

      <UnavailableAlert unavailable={unavailable} />

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading users" />}
        {error && <StateBlock kind="error" title="Users could not be loaded" message={error} />}
        {!loading && !error && users?.length === 0 && (
          <StateBlock icon="groups" title="No users to show" message="No running company has any users yet." />
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
                  { value: "active", label: "Active", count: (users?.length ?? 0) - blockedCount },
                  { value: "blocked", label: "Blocked", count: blockedCount },
                ]}
              />
            </Box>
            <DataTable table={table} canSearch />
          </>
        )}
      </Section>

      <KitDialog
        open={addOpen}
        onClose={closeAdd}
        onSubmit={addLink || addCreated ? undefined : submitAdd}
        icon={addCreated ? "check_circle" : addLink ? "mark_email_read" : "person_add"}
        tone={addLink || addCreated ? "success" : "info"}
        title={addCreated ? "User created" : addLink ? "Invitation sent" : "Add user"}
        subtitle={
          addCreated
            ? "Pass these sign-in details on now. The password is not stored anywhere readable and cannot be shown again."
            : addLink
              ? "Share this link with the new user so they can set a password. They appear in the list once they accept."
              : "Send invite emails a single-use link to set their own password. Create adds them now, with a password you pass on."
        }
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={closeAdd}>
              {addLink || addCreated ? "Close" : "Cancel"}
            </MDButton>
            {addCreated ? (
              <MDButton variant="gradient" color="info" onClick={copyCreated} startIcon={<Icon>content_copy</Icon>}>
                Copy sign-in details
              </MDButton>
            ) : addLink ? (
              <MDButton variant="gradient" color="info" onClick={copyAddLink} startIcon={<Icon>content_copy</Icon>}>
                Copy link
              </MDButton>
            ) : (
              <>
                <MDButton type="submit" variant="gradient" color="info" disabled={saving}>
                  Send invite
                </MDButton>
                <MDButton variant="gradient" color="success" disabled={saving} onClick={submitCreate}>
                  Create
                </MDButton>
              </>
            )}
          </>
        }
      >
        {addError && <InlineAlert sx={{ mb: 2.5 }}>{addError}</InlineAlert>}
        {addCreated ? (
          <Box sx={{ display: "grid", gap: 1.5 }}>
            <LinkBox link={`Username: ${addCreated.email}`} />
            <LinkBox link={`Password: ${addCreated.password}`} />
          </Box>
        ) : addLink ? (
          <>
            <InlineAlert tone="info" sx={{ mb: 2 }}>
              Email delivery is not configured in this environment, so the accept link is shown here instead.
            </InlineAlert>
            <LinkBox link={addLink} />
          </>
        ) : (
          <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
            <MDInput
              select
              label="Company"
              fullWidth
              SelectProps={{ native: true }}
              value={addCompany}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAddCompany(e.target.value)}
            >
              {companies.length === 0 && <option value="">No running company</option>}
              {companies.map((company) => (
                <option key={company.id} value={company.id}>
                  {company.name}
                </option>
              ))}
            </MDInput>
            <MDInput
              label="Email"
              type="email"
              fullWidth
              value={addEmail}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAddEmail(e.target.value)}
            />
            <MDInput
              select
              label="Role"
              fullWidth
              SelectProps={{ native: true }}
              value={addRole}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAddRole(e.target.value)}
            >
              {ROLES.map((role) => (
                <option key={role} value={role}>
                  {roleLabel(role)}
                </option>
              ))}
            </MDInput>
            <MDInput
              label="Password (Create only)"
              fullWidth
              autoComplete="new-password"
              value={addPassword}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAddPassword(e.target.value)}
              helperText="Leave empty to generate one. At least 12 characters, with an upper-case letter, a lower-case letter and a digit."
            />
          </Box>
        )}
      </KitDialog>

      <KitDialog
        open={!!editTarget}
        onClose={() => setEditTarget(null)}
        onSubmit={submitEdit}
        icon="edit"
        title="Edit user"
        subtitle={editTarget ? `${editTarget.tenantName} · the email is also what they sign in with.` : ""}
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setEditTarget(null)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info" disabled={saving}>
              Save changes
            </MDButton>
          </>
        }
      >
        {editError && <InlineAlert sx={{ mb: 2.5 }}>{editError}</InlineAlert>}
        <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
          <MDInput
            label="Display name"
            fullWidth
            value={editName}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEditName(e.target.value)}
          />
          <MDInput
            label="Email"
            type="email"
            fullWidth
            value={editEmail}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEditEmail(e.target.value)}
          />
          <MDInput
            select
            label="Role"
            fullWidth
            SelectProps={{ native: true }}
            value={editRole}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEditRole(e.target.value)}
          >
            {ROLES.map((role) => (
              <option key={role} value={role}>
                {roleLabel(role)}
              </option>
            ))}
          </MDInput>
        </Box>
      </KitDialog>

      <KitDialog
        open={!!passwordTarget}
        onClose={closePassword}
        onSubmit={passwordSaved ? undefined : submitPassword}
        icon={passwordSaved ? "check_circle" : "key"}
        tone={passwordSaved ? "success" : "warning"}
        title={passwordSaved ? "Password set" : "Set password"}
        subtitle={
          passwordSaved
            ? "Pass these sign-in details on now. The password is not stored anywhere readable and cannot be shown again."
            : passwordTarget
              ? `${passwordTarget.displayName} at ${passwordTarget.tenantName}. Their current password stops working and they are signed out.`
              : ""
        }
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={closePassword}>
              {passwordSaved ? "Close" : "Cancel"}
            </MDButton>
            {passwordSaved ? (
              <MDButton variant="gradient" color="info" onClick={copyCredentials} startIcon={<Icon>content_copy</Icon>}>
                Copy sign-in details
              </MDButton>
            ) : (
              <MDButton type="submit" variant="gradient" color="info" disabled={saving}>
                Set password
              </MDButton>
            )}
          </>
        }
      >
        {passwordError && <InlineAlert sx={{ mb: 2.5 }}>{passwordError}</InlineAlert>}
        {passwordTarget && passwordSaved && (
          <Box sx={{ display: "grid", gap: 1.5 }}>
            <LinkBox link={`Username: ${passwordTarget.email}`} />
            <LinkBox link={`Password: ${password}`} />
          </Box>
        )}
        {passwordTarget && !passwordSaved && (
          <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
            <MDInput label="Username" fullWidth value={passwordTarget.email} InputProps={{ readOnly: true }} />
            <MDInput
              label="New password"
              type={passwordShown ? "text" : "password"}
              fullWidth
              autoComplete="new-password"
              value={password}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPassword(e.target.value)}
              helperText="At least 12 characters, with an upper-case letter, a lower-case letter and a digit."
            />
            <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1 }}>
              <MDButton
                size="small"
                variant="outlined"
                color="info"
                onClick={() => {
                  setPassword(generatePassword());
                  setPasswordShown(true);
                }}
              >
                Generate
              </MDButton>
              <MDButton size="small" variant="outlined" color="secondary" onClick={() => setPasswordShown((shown) => !shown)}>
                {passwordShown ? "Hide password" : "Show password"}
              </MDButton>
            </Box>
          </Box>
        )}
      </KitDialog>

      <ConfirmDialog
        open={!!deleteTarget}
        title="Delete user"
        message={
          deleteTarget
            ? `Permanently delete ${deleteTarget.displayName} (${deleteTarget.email}) from ${deleteTarget.tenantName}? This cannot be undone. To only stop them signing in, block them instead.`
            : ""
        }
        confirmLabel="Delete"
        confirmColor="error"
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </PageShell>
  );
}
