import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import {
  Identity,
  PageHeader,
  Section,
  SimpleTable,
  StateBlock,
  StatusPill,
  downloadCsv,
  formatDateTime,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import { ApiError } from "../../lib/api";
import { TENANT_STATUS_TONE } from "../../lib/status";
import { TenantsApi } from "../../api/resources";
import { TENANT_STATUS_LABELS, type TenantStatus, type TenantSummary } from "../../api/types";

type ChangeEvent = React.ChangeEvent<HTMLInputElement>;

export default function TenantExportPage() {
  const [tenants, setTenants] = useState<TenantSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  useEffect(() => {
    TenantsApi.list()
      .then(setTenants)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load companies."))
      .finally(() => setLoading(false));
  }, []);

  // The date inputs are whole local days: "from" starts at midnight, "to" runs to the end of its day.
  const fromTime = from ? new Date(`${from}T00:00:00`).getTime() : null;
  const toTime = to ? new Date(`${to}T23:59:59.999`).getTime() : null;
  const term = search.trim().toLowerCase();

  const rows = (tenants ?? []).filter((t) => {
    const created = new Date(t.createdAtUtc).getTime();
    return (
      (status === "all" || t.status === Number(status)) &&
      (!term || t.name.toLowerCase().includes(term) || t.slug.toLowerCase().includes(term)) &&
      (fromTime === null || created >= fromTime) &&
      (toTime === null || created <= toTime)
    );
  });

  const filtered = term !== "" || status !== "all" || from !== "" || to !== "";
  const reset = () => {
    setSearch("");
    setStatus("all");
    setFrom("");
    setTo("");
  };

  const exportCsv = () =>
    downloadCsv(
      "companies.csv",
      ["Name", "Slug", "Status", "URL", "Created (UTC)"],
      rows.map((t) => [t.name, t.slug, TENANT_STATUS_LABELS[t.status], t.url, t.createdAtUtc])
    );

  return (
    <PageShell>
      <PageHeader
        icon="file_download"
        title="Filters & export"
        subtitle="Narrow the list of companies, then download what is left as a CSV file."
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
        title={`${rows.length} of ${tenants?.length ?? 0} companies`}
        actions={
          <MDButton size="small" variant="text" color="secondary" disabled={!filtered} onClick={reset}>
            Clear filters
          </MDButton>
        }
      >
        {loading && <StateBlock kind="loading" title="Loading" />}
        {error && <StateBlock kind="error" title="The list could not be loaded" message={error} />}
        {!loading && !error && (
          <>
            <Box
              sx={{ display: "grid", gap: 2, px: 3, py: 2.5, gridTemplateColumns: { xs: "1fr", md: "2fr 1fr 1fr 1fr" } }}
            >
              <MDInput label="Name or slug" size="small" value={search} onChange={(e: ChangeEvent) => setSearch(e.target.value)} />
              <MDInput
                select
                label="Status"
                size="small"
                SelectProps={{ native: true }}
                value={status}
                onChange={(e: ChangeEvent) => setStatus(e.target.value)}
              >
                <option value="all">Any status</option>
                {([1, 0, 2, 3] as TenantStatus[]).map((value) => (
                  <option key={value} value={value}>
                    {TENANT_STATUS_LABELS[value]}
                  </option>
                ))}
              </MDInput>
              <MDInput
                label="Created from"
                type="date"
                size="small"
                InputLabelProps={{ shrink: true }}
                value={from}
                onChange={(e: ChangeEvent) => setFrom(e.target.value)}
              />
              <MDInput
                label="Created to"
                type="date"
                size="small"
                InputLabelProps={{ shrink: true }}
                value={to}
                onChange={(e: ChangeEvent) => setTo(e.target.value)}
              />
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={(t: TenantSummary) => t.id}
              emptyMessage="No companies match these filters."
              columns={[
                {
                  key: "company",
                  header: "Company",
                  render: (t: TenantSummary) => <Identity name={t.name} secondary={t.slug} square />,
                },
                {
                  key: "status",
                  header: "Status",
                  render: (t: TenantSummary) => (
                    <StatusPill tone={TENANT_STATUS_TONE[t.status]} label={TENANT_STATUS_LABELS[t.status]} />
                  ),
                },
                { key: "url", header: "URL", render: (t: TenantSummary) => t.url ?? "—" },
                { key: "created", header: "Created", render: (t: TenantSummary) => formatDateTime(t.createdAtUtc) },
              ]}
            />
          </>
        )}
      </Section>
    </PageShell>
  );
}