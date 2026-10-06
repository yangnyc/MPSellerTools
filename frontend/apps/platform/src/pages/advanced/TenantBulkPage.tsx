import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { FilterTabs, Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, timeAgo } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import BulkResultAlert from "../../components/BulkResultAlert";
import { ApiError } from "../../lib/api";
import { runBulk, type BulkResult } from "../../lib/bulk";
import { TENANT_STATUS_TONE } from "../../lib/status";
import { TenantsApi } from "../../api/resources";
import { TENANT_STATUS_LABELS, type TenantStatus, type TenantSummary } from "../../api/types";

type StatusFilter = "all" | TenantStatus;
type ActionKey = "suspend" | "resume" | "restart";

// Each action only applies to companies in one status; the rest of the
// selection is left alone rather than sent and refused.
const ACTIONS: Record<
  ActionKey,
  { label: string; appliesTo: TenantStatus; color: "warning" | "success" | "info"; run: (id: string) => Promise<void>; done: string; effect: string }
> = {
  suspend: {
    label: "Suspend",
    appliesTo: 1,
    color: "warning",
    run: TenantsApi.suspend,
    done: "queued to suspend",
    effect: "Their workspaces go offline until resumed.",
  },
  resume: {
    label: "Resume",
    appliesTo: 2,
    color: "success",
    run: TenantsApi.resume,
    done: "queued to resume",
    effect: "Their workspaces come back online.",
  },
  restart: {
    label: "Restart",
    appliesTo: 1,
    color: "info",
    run: TenantsApi.restart,
    done: "queued to restart",
    effect: "Their workspaces are briefly unavailable while they restart.",
  },
};

export default function TenantBulkPage() {
  const [tenants, setTenants] = useState<TenantSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [pending, setPending] = useState<ActionKey | null>(null);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<{ outcome: BulkResult; done: string } | null>(null);

  const fetchData = useCallback(() => {
    TenantsApi.list()
      .then(setTenants)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load companies."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  const chosen = (tenants ?? []).filter((t) => selected.has(t.id));
  const eligible = (key: ActionKey) => chosen.filter((t) => t.status === ACTIONS[key].appliesTo);

  const confirm = async () => {
    if (!pending) return;
    const action = ACTIONS[pending];
    const targets = eligible(pending);
    setPending(null);
    setRunning(true);
    const outcome = await runBulk(targets, (t) => t.name, (t) => action.run(t.id));
    setResult({ outcome, done: `${outcome.succeeded === 1 ? "company" : "companies"} ${action.done}` });
    setSelected(new Set());
    setRunning(false);
    fetchData();
  };

  const rows = (tenants ?? []).filter((t) => statusFilter === "all" || t.status === statusFilter);
  const pendingCount = pending ? eligible(pending).length : 0;

  return (
    <PageShell>
      <PageHeader
        icon="checklist_rtl"
        title="Bulk actions"
        subtitle="Suspend, resume or restart several companies in one step."
        actions={
          <MDButton variant="outlined" color="info" onClick={fetchData} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <BulkResultAlert result={result?.outcome ?? null} done={result?.done ?? ""} />

      <Section
        flush
        title={`${chosen.length} selected`}
        subtitle="Each button counts the selected companies it applies to."
        actions={(Object.keys(ACTIONS) as ActionKey[]).map((key) => (
          <MDButton
            key={key}
            size="small"
            variant="gradient"
            color={ACTIONS[key].color}
            disabled={running || eligible(key).length === 0}
            onClick={() => setPending(key)}
          >
            {ACTIONS[key].label} ({eligible(key).length})
          </MDButton>
        ))}
      >
        {loading && <StateBlock kind="loading" title="Loading" />}
        {error && <StateBlock kind="error" title="The list could not be loaded" message={error} />}
        {!loading && !error && (
          <>
            <Box sx={{ px: 3, py: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: tenants?.length },
                  ...([1, 0, 2, 3] as TenantStatus[]).map((status) => ({
                    value: status,
                    label: TENANT_STATUS_LABELS[status],
                    count: tenants?.filter((t) => t.status === status).length,
                  })),
                ]}
              />
            </Box>
            <SimpleTable
              rows={rows}
              getRowId={(t: TenantSummary) => t.id}
              getRowLabel={(t: TenantSummary) => t.name}
              selection={{ selected, onChange: setSelected }}
              emptyMessage="No companies match this filter."
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
                    <StatusPill tone={TENANT_STATUS_TONE[t.status]} label={TENANT_STATUS_LABELS[t.status]} pulse={t.status === 0} />
                  ),
                },
                { key: "created", header: "Created", render: (t: TenantSummary) => timeAgo(t.createdAtUtc) },
              ]}
            />
          </>
        )}
      </Section>

      <ConfirmDialog
        open={!!pending}
        title={pending ? `${ACTIONS[pending].label} companies` : ""}
        message={
          pending
            ? `${ACTIONS[pending].label} ${pendingCount} ${pendingCount === 1 ? "company" : "companies"}? ${ACTIONS[pending].effect}`
            : ""
        }
        confirmLabel={pending ? ACTIONS[pending].label : "Confirm"}
        confirmColor={pending ? ACTIONS[pending].color : "info"}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
    </PageShell>
  );
}