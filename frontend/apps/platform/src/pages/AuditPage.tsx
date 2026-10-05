import { useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import { PageHeader, Section, StateBlock, StatusPill, formatDateTime, timeAgo, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import { ApiError } from "../lib/api";
import { AuditApi, TenantsApi } from "../api/resources";
import type { AuditEntry } from "../api/types";

export default function AuditPage() {
  const { c } = useKit();
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Audit entries carry only a tenant id; the names come from the registry.
  const [tenantNames, setTenantNames] = useState<Record<string, string>>({});
  const [actionFilter, setActionFilter] = useState("");
  const [tenantFilter, setTenantFilter] = useState("");

  useEffect(() => {
    AuditApi.list()
      .then(setEntries)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load audit log."));
    TenantsApi.list()
      .then((tenants) => setTenantNames(Object.fromEntries(tenants.map((t) => [t.id, t.name]))))
      .catch(() => setTenantNames({}));
  }, []);

  const actions = useMemo(() => [...new Set((entries ?? []).map((e) => e.action))].sort(), [entries]);
  const tenantIds = useMemo(
    () => [...new Set((entries ?? []).flatMap((e) => (e.tenantId ? [e.tenantId] : [])))],
    [entries]
  );

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    const columns = [
      {
        Header: "When",
        accessor: "occurredAtUtc",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{timeAgo(value)}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{formatDateTime(value)}</Box>
          </Box>
        ),
      },
      { Header: "Actor", accessor: "actorEmail" },
      {
        Header: "Action",
        accessor: "action",
        Cell: ({ value }: { value: string }) => <StatusPill tone="primary" label={value} />,
      },
      {
        Header: "Company",
        id: "company",
        accessor: (entry: AuditEntry) => (entry.tenantId ? (tenantNames[entry.tenantId] ?? entry.tenantId) : ""),
        Cell: ({ value }: { value: string }) => value || "—",
      },
      {
        Header: "Details",
        id: "details",
        accessor: (entry: AuditEntry) => entry.details ?? "",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ maxWidth: 420, whiteSpace: "normal", overflowWrap: "anywhere" }}>{value || "—"}</Box>
        ),
      },
    ];
    const rows = (entries ?? []).filter(
      (e) => (!actionFilter || e.action === actionFilter) && (!tenantFilter || e.tenantId === tenantFilter)
    );
    return { columns, rows };
  }, [entries, tenantNames, actionFilter, tenantFilter, c]);

  const filterSelect = (
    label: string,
    value: string,
    onChange: (value: string) => void,
    allLabel: string,
    options: { value: string; label: string }[]
  ) => (
    <MDInput
      select
      size="small"
      SelectProps={{ native: true }}
      inputProps={{ "aria-label": label }}
      value={value}
      onChange={(e: React.ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
    >
      <option value="">{allLabel}</option>
      {options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </MDInput>
  );

  return (
    <PageShell>
      <PageHeader
        icon="fact_check"
        title="Audit log"
        subtitle="A record of every administrative action taken in this console."
      />

      <Section
        title="Platform audit log"
        subtitle={entries ? `${entries.length} recorded ${entries.length === 1 ? "event" : "events"}` : undefined}
        actions={
          (entries?.length ?? 0) > 0 && (
            <>
              {filterSelect(
                "Filter by company",
                tenantFilter,
                setTenantFilter,
                "All tenants",
                tenantIds.map((id) => ({ value: id, label: tenantNames[id] ?? id }))
              )}
              {filterSelect(
                "Filter by operation",
                actionFilter,
                setActionFilter,
                "All operations",
                actions.map((action) => ({ value: action, label: action }))
              )}
            </>
          )
        }
        flush
      >
        {error && <StateBlock kind="error" title="The audit log could not be loaded" message={error} />}
        {!error && !entries && <StateBlock kind="loading" title="Loading audit log" />}
        {!error && entries?.length === 0 && (
          <StateBlock icon="history" title="No activity recorded yet" message="Actions taken in this console will be listed here." />
        )}
        {!error && (entries?.length ?? 0) > 0 && <DataTable table={table} canSearch />}
      </Section>
    </PageShell>
  );
}
