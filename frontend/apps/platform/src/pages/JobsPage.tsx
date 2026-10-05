import { useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import DataTable from "examples/Tables/DataTable";
import {
  FilterTabs,
  Identity,
  PageHeader,
  Section,
  StateBlock,
  StatusPill,
  formatDateTime,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { ApiError } from "../lib/api";
import { JobsApi } from "../api/resources";
import { JOB_STATUS_LABELS, JOB_TYPE_LABELS, type ProvisioningJob, type ProvisioningJobStatus } from "../api/types";
import { JOB_STATUS_TONE } from "../lib/status";

type StatusFilter = "all" | ProvisioningJobStatus;

const JOB_STATUSES: ProvisioningJobStatus[] = [0, 1, 2, 3];
const REFRESH_MS = 5000;

export default function JobsPage() {
  const kit = useKit();
  const { c } = kit;
  const [jobs, setJobs] = useState<ProvisioningJob[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  useEffect(() => {
    const load = () =>
      JobsApi.list()
        // Keep the previous array when a refresh brings nothing new, so the
        // table below keeps its page and search between polls.
        .then((next) => setJobs((prev) => (JSON.stringify(prev) === JSON.stringify(next) ? prev : next)))
        .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load jobs."));
    load();
    const timer = setInterval(load, REFRESH_MS);
    return () => clearInterval(timer);
  }, []);

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = ProvisioningJob & { typeLabel: string; statusLabel: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (jobs ?? [])
      .filter((j) => statusFilter === "all" || j.status === statusFilter)
      .map((j) => ({ ...j, typeLabel: JOB_TYPE_LABELS[j.jobType], statusLabel: JOB_STATUS_LABELS[j.status] }));

    const columns = [
      {
        Header: "Company",
        accessor: "tenantName",
        Cell: ({ value }: { value: string }) => <Identity name={value} size={28} square />,
      },
      { Header: "Type", accessor: "typeLabel" },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill
            tone={JOB_STATUS_TONE[row.original.status]}
            label={row.original.statusLabel}
            pulse={row.original.status === 1}
          />
        ),
      },
      { Header: "Attempts", accessor: "attempts", align: "right" as const },
      {
        Header: "Updated",
        accessor: "updatedAtUtc",
        Cell: ({ value }: { value: string }) => <span title={formatDateTime(value)}>{timeAgo(value)}</span>,
      },
      {
        Header: "Error",
        id: "error",
        accessor: (row: Row) => row.lastError ?? "",
        Cell: ({ value }: { value: string }) =>
          value ? (
            <Box sx={{ maxWidth: 280, whiteSpace: "normal", overflowWrap: "anywhere", color: kit.tone("error").fg }}>
              {value}
            </Box>
          ) : (
            <Box component="span" sx={{ color: c.subtle }}>
              —
            </Box>
          ),
      },
    ];
    return { columns, rows };
  }, [jobs, statusFilter, kit, c]);

  return (
    <PageShell>
      <PageHeader
        icon="work_history"
        title="Provisioning jobs"
        subtitle="Every create, suspend, and resume the worker has picked up. Refreshes every 5 seconds."
      />

      <Section flush>
        {error && <StateBlock kind="error" title="Jobs could not be loaded" message={error} />}
        {!error && !jobs && <StateBlock kind="loading" title="Loading jobs" />}
        {!error && jobs?.length === 0 && (
          <StateBlock
            icon="work_history"
            title="No provisioning jobs yet"
            message="Jobs appear here as soon as a company is created, suspended, or resumed."
          />
        )}
        {!error && (jobs?.length ?? 0) > 0 && (
          <>
            <Box sx={{ px: 3, pt: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: jobs?.length },
                  ...JOB_STATUSES.map((status) => ({
                    value: status,
                    label: JOB_STATUS_LABELS[status],
                    count: jobs?.filter((j) => j.status === status).length,
                  })),
                ]}
              />
            </Box>
            <DataTable table={table} canSearch />
          </>
        )}
      </Section>
    </PageShell>
  );
}
