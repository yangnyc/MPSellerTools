import { useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import { PageHeader, Section, StateBlock, StatusPill, formatDateTime, timeAgo, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import { ApiError } from "../lib/api";
import { AuditApi } from "../api/resources";
import type { AuditEntry } from "../api/types";

export default function AuditPage() {
  const { c } = useKit();
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionFilter, setActionFilter] = useState("");

  useEffect(() => {
    AuditApi.list()
      .then(setEntries)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load audit log."))
      .finally(() => setLoading(false));
  }, []);

  const actions = useMemo(() => [...new Set((entries ?? []).map((e) => e.action))].sort(), [entries]);

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
        Header: "Details",
        accessor: (entry: AuditEntry) => entry.details ?? "",
        id: "details",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ maxWidth: 480, whiteSpace: "normal", overflowWrap: "anywhere" }}>{value || "—"}</Box>
        ),
      },
    ];
    const rows = (entries ?? []).filter((e) => !actionFilter || e.action === actionFilter);
    return { columns, rows };
  }, [entries, actionFilter, c]);

  return (
    <PageShell>
      <PageHeader
        icon="fact_check"
        title="Audit log"
        subtitle="A record of who changed what in your company's workspace."
      />

      <Section
        title="Company audit log"
        subtitle={entries ? `${entries.length} recorded ${entries.length === 1 ? "event" : "events"}` : undefined}
        actions={
          actions.length > 1 && (
            <MDInput
              select
              size="small"
              SelectProps={{ native: true }}
              inputProps={{ "aria-label": "Filter by action" }}
              value={actionFilter}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setActionFilter(e.target.value)}
            >
              <option value="">All actions</option>
              {actions.map((action) => (
                <option key={action} value={action}>
                  {action}
                </option>
              ))}
            </MDInput>
          )
        }
        flush
      >
        {loading && <StateBlock kind="loading" title="Loading audit log" />}
        {error && <StateBlock kind="error" title="The audit log could not be loaded" message={error} />}
        {!loading && !error && entries?.length === 0 && (
          <StateBlock icon="history" title="No activity recorded yet" message="Changes made in this workspace will be listed here." />
        )}
        {!loading && !error && (entries?.length ?? 0) > 0 && <DataTable table={table} canSearch />}
      </Section>
    </PageShell>
  );
}
