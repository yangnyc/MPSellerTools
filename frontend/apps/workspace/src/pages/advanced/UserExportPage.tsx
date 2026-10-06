import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, downloadCsv, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { ApiError } from "../../lib/api";
import { UsersApi } from "../../api/resources";
import type { UserSummary } from "../../api/types";

type ChangeEvent = React.ChangeEvent<HTMLInputElement>;

const ROLES = ["TenantAdmin", "Employee"];
const roleOf = (user: UserSummary) => user.roles[0] ?? "Employee";

export default function UserExportPage() {
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [role, setRole] = useState("all");
  const [status, setStatus] = useState("all");

  useEffect(() => {
    UsersApi.list()
      .then(setUsers)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, []);

  const term = search.trim().toLowerCase();
  const rows = (users ?? []).filter(
    (u) =>
      (role === "all" || roleOf(u) === role) &&
      (status === "all" || (status === "blocked") === u.isBlocked) &&
      (!term || u.displayName.toLowerCase().includes(term) || u.email.toLowerCase().includes(term))
  );

  const filtered = term !== "" || role !== "all" || status !== "all";
  const reset = () => {
    setSearch("");
    setRole("all");
    setStatus("all");
  };

  const exportCsv = () =>
    downloadCsv(
      "users.csv",
      ["Name", "Email", "Role", "Status"],
      rows.map((u) => [u.displayName, u.email, roleLabel(roleOf(u)), u.isBlocked ? "Blocked" : "Active"])
    );

  return (
    <PageShell>
      <PageHeader
        icon="file_download"
        title="Filters & export"
        subtitle="Narrow the list of users, then download what is left as a CSV file."
        actions={
          <MDButton
            variant="gradient"
            color="info"
            disabled={rows.length === 0}
            onClick={exportCsv}
            startIcon={<Icon>file_download</Icon>}
          >
            Export CSV ({rows.length})
          </MDButton>
        }
      />

      <Section
        flush
        title={`${rows.length} of ${users?.length ?? 0} users`}
        actions={
          <MDButton size="small" variant="text" color="secondary" disabled={!filtered} onClick={reset}>
            Clear filters
          </MDButton>
        }
      >
        {loading && <StateBlock kind="loading" title="Loading users" />}
        {error && <StateBlock kind="error" title="Users could not be loaded" message={error} />}
        {!loading && !error && (
          <>
            <Box sx={{ display: "grid", gap: 2, px: 3, py: 2.5, gridTemplateColumns: { xs: "1fr", md: "2fr 1fr 1fr" } }}>
              <MDInput label="Name or email" size="small" value={search} onChange={(e: ChangeEvent) => setSearch(e.target.value)} />
              <MDInput
                select
                label="Role"
                size="small"
                SelectProps={{ native: true }}
                value={role}
                onChange={(e: ChangeEvent) => setRole(e.target.value)}
              >
                <option value="all">Any role</option>
                {ROLES.map((value) => (
                  <option key={value} value={value}>
                    {roleLabel(value)}
                  </option>
                ))}
              </MDInput>
              <MDInput
                select
                label="Status"
                size="small"
                SelectProps={{ native: true }}
                value={status}
                onChange={(e: ChangeEvent) => setStatus(e.target.value)}
              >
                <option value="all">Any status</option>
                <option value="active">Active</option>
                <option value="blocked">Blocked</option>
              </MDInput>
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={(u: UserSummary) => u.id}
              emptyMessage="No users match these filters."
              columns={[
                { key: "user", header: "User", render: (u: UserSummary) => <Identity name={u.displayName} secondary={u.email} /> },
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
    </PageShell>
  );
}