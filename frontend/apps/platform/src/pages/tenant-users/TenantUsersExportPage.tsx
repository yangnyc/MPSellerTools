import { useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, downloadCsv, roleLabel } from "examples/Kit";
import PageShell from "../../components/PageShell";
import type { TenantUser } from "../../api/types";
import { CompanySelect, UnavailableAlert } from "./shared";
import { ROLES, roleOf, rowId, useTenantUsers } from "./tenantUsers";

type ChangeEvent = React.ChangeEvent<HTMLInputElement>;

export default function TenantUsersExportPage() {
  const { users, unavailable, error, loading } = useTenantUsers();
  const [search, setSearch] = useState("");
  const [company, setCompany] = useState("all");
  const [role, setRole] = useState("all");
  const [status, setStatus] = useState("all");

  const term = search.trim().toLowerCase();
  const rows = (users ?? []).filter(
    (u) =>
      (company === "all" || u.tenantId === company) &&
      (role === "all" || roleOf(u) === role) &&
      (status === "all" || (status === "blocked") === u.isBlocked) &&
      (!term || u.displayName.toLowerCase().includes(term) || u.email.toLowerCase().includes(term))
  );

  const filtered = term !== "" || company !== "all" || role !== "all" || status !== "all";
  const reset = () => {
    setSearch("");
    setCompany("all");
    setRole("all");
    setStatus("all");
  };

  const exportCsv = () =>
    downloadCsv(
      "tenant-users.csv",
      ["Company", "Slug", "Name", "Email", "Role", "Status"],
      rows.map((u) => [u.tenantName, u.tenantSlug, u.displayName, u.email, roleLabel(roleOf(u)), u.isBlocked ? "Blocked" : "Active"])
    );

  return (
    <PageShell>
      <PageHeader
        icon="file_download"
        title="Filters & export"
        subtitle="Narrow the list of users across companies, then download what is left as a CSV file."
        actions={
          <MDButton variant="gradient" color="info" disabled={rows.length === 0} onClick={exportCsv} startIcon={<Icon>file_download</Icon>}>
            Export CSV ({rows.length})
          </MDButton>
        }
      />

      <UnavailableAlert unavailable={unavailable} />

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
            <Box sx={{ display: "grid", gap: 2, px: 3, py: 2.5, gridTemplateColumns: { xs: "1fr", md: "2fr 1fr 1fr 1fr" } }}>
              <MDInput label="Name or email" size="small" value={search} onChange={(e: ChangeEvent) => setSearch(e.target.value)} />
              <CompanySelect users={users ?? []} value={company} onChange={setCompany} />
              <MDInput select label="Role" size="small" SelectProps={{ native: true }} value={role} onChange={(e: ChangeEvent) => setRole(e.target.value)}>
                <option value="all">Any role</option>
                {ROLES.map((value) => (
                  <option key={value} value={value}>
                    {roleLabel(value)}
                  </option>
                ))}
              </MDInput>
              <MDInput select label="Status" size="small" SelectProps={{ native: true }} value={status} onChange={(e: ChangeEvent) => setStatus(e.target.value)}>
                <option value="all">Any status</option>
                <option value="active">Active</option>
                <option value="blocked">Blocked</option>
              </MDInput>
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={rowId}
              emptyMessage="No users match these filters."
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
    </PageShell>
  );
}
