import { useEffect, useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Link from "@mui/material/Link";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
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
import { TenantsApi } from "../api/resources";
import { TENANT_STATUS_LABELS, type TenantStatus, type TenantSummary } from "../api/types";
import { TENANT_STATUS_TONE } from "../lib/status";

type StatusFilter = "all" | TenantStatus;

const TENANT_STATUSES: TenantStatus[] = [1, 0, 2, 3];

export default function TenantsListPage() {
  const { c } = useKit();
  const [tenants, setTenants] = useState<TenantSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  useEffect(() => {
    TenantsApi.list()
      .then(setTenants)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load companies."))
      .finally(() => setLoading(false));
  }, []);

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = TenantSummary & { statusLabel: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (tenants ?? [])
      .filter((t) => statusFilter === "all" || t.status === statusFilter)
      .map((t) => ({ ...t, statusLabel: TENANT_STATUS_LABELS[t.status] }));

    const columns = [
      {
        Header: "Company",
        id: "company",
        // Name and slug together, so searching and sorting cover both.
        accessor: (row: Row) => `${row.name} ${row.slug}`,
        Cell: ({ row }: CellProps) => (
          <Box component={RouterLink} to={`/tenants/${row.original.id}`} sx={{ display: "block" }}>
            <Identity name={row.original.name} secondary={row.original.slug} square />
          </Box>
        ),
      },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill
            tone={TENANT_STATUS_TONE[row.original.status]}
            label={row.original.statusLabel}
            pulse={row.original.status === 0}
          />
        ),
      },
      {
        Header: "URL",
        id: "url",
        accessor: (row: Row) => row.url ?? "",
        Cell: ({ value }: { value: string }) =>
          value ? (
            <Link href={value} target="_blank" rel="noreferrer" sx={{ color: c.accent }}>
              {value}
            </Link>
          ) : (
            <Box component="span" sx={{ color: c.subtle }}>
              —
            </Box>
          ),
      },
      {
        Header: "Created",
        accessor: "createdAtUtc",
        Cell: ({ value }: { value: string }) => <span title={formatDateTime(value)}>{timeAgo(value)}</span>,
      },
      {
        Header: "",
        id: "open",
        accessor: "id",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <Tooltip title="Open">
            <IconButton
              component={RouterLink}
              to={`/tenants/${row.original.id}`}
              size="small"
              aria-label={`Open ${row.original.name}`}
              sx={{ color: c.muted }}
            >
              <Icon fontSize="small">arrow_forward</Icon>
            </IconButton>
          </Tooltip>
        ),
      },
    ];
    return { columns, rows };
  }, [tenants, statusFilter, c]);

  const createButton = (size: "small" | "medium") => (
    <MDButton
      component={RouterLink}
      to="/tenants/new"
      variant="gradient"
      color="info"
      size={size}
      startIcon={<Icon>add</Icon>}
    >
      Create company
    </MDButton>
  );

  return (
    <PageShell>
      <PageHeader
        icon="apartment"
        title="Companies"
        subtitle="Every workspace on the platform, with its status and login URL."
        actions={createButton("medium")}
      />

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading" />}
        {error && <StateBlock kind="error" title="The list could not be loaded" message={error} />}
        {!loading && !error && tenants?.length === 0 && (
          <StateBlock
            icon="apartment"
            title="No companies yet"
            message="Create one and the platform provisions its database and workspace for you."
            action={createButton("small")}
          />
        )}
        {!loading && !error && (tenants?.length ?? 0) > 0 && (
          <>
            <Box sx={{ px: 3, pt: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: tenants?.length },
                  ...TENANT_STATUSES.map((status) => ({
                    value: status,
                    label: TENANT_STATUS_LABELS[status],
                    count: tenants?.filter((t) => t.status === status).length,
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
