import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import DataTable from "examples/Tables/DataTable";
import {
  DetailList,
  FilterTabs,
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  formatDateTime,
  timeAgo,
  useKit,
  type KitTone,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import {
  ChannelListingsApi,
  ChannelsApi,
  SYNC_OPERATION_LABELS,
  SyncApi,
  type ChannelAccount,
  type OrderLineIssue,
  type OrderLineIssueReason,
  type SyncErrorClass,
  type SyncHealth,
  type SyncJob,
  type SyncJobStatus,
} from "../../api/channels";

const STATUS: Record<SyncJobStatus, { label: string; tone: KitTone; pulse?: boolean }> = {
  0: { label: "Waiting", tone: "neutral" },
  1: { label: "Running", tone: "info", pulse: true },
  2: { label: "Awaiting marketplace", tone: "info", pulse: true },
  3: { label: "Succeeded", tone: "success" },
  4: { label: "Failed", tone: "error" },
  5: { label: "Needs correction", tone: "warning" },
  6: { label: "Dry run", tone: "neutral" },
  7: { label: "Cancelled", tone: "neutral" },
};

const ERROR_CLASS: Record<SyncErrorClass, string> = {
  0: "—",
  1: "Temporary: it is tried again by itself",
  2: "Authorization: check the account's credentials",
  3: "The listing's data has to be corrected",
  4: "Refused for good by the marketplace",
};

const ISSUE_REASON: Record<OrderLineIssueReason, { label: string; tone: KitTone; help: string }> = {
  0: { label: "Unknown SKU", tone: "error", help: "The marketplace's SKU matches nothing in your catalog." },
  1: { label: "Ambiguous SKU", tone: "warning", help: "The marketplace's SKU matches more than one variant." },
  2: { label: "Stock shortfall", tone: "error", help: "More was sold than could be reserved: an oversell to sort out by hand." },
};

type Filter = "attention" | "active" | "all";
const NEEDS_ATTENTION: SyncJobStatus[] = [4, 5];
const ACTIVE: SyncJobStatus[] = [0, 1, 2];

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// What is being sent to the marketplaces, what they answered, and the order lines nobody could place.
export default function SyncQueuePage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [health, setHealth] = useState<SyncHealth | null>(null);
  const [jobs, setJobs] = useState<SyncJob[] | null>(null);
  const [issues, setIssues] = useState<OrderLineIssue[] | null>(null);
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [skus, setSkus] = useState<Map<string, string>>(new Map());
  const [loadError, setLoadError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("attention");
  const [busy, setBusy] = useState<string | null>(null);
  const [detail, setDetail] = useState<SyncJob | null>(null);

  const load = useCallback(
    () =>
      Promise.all([SyncApi.health(), SyncApi.jobs(), SyncApi.orderIssues(), ChannelsApi.list(), ChannelListingsApi.all()])
        .then(([nextHealth, nextJobs, nextIssues, nextAccounts, listings]) => {
          setHealth(nextHealth);
          setJobs(nextJobs);
          setIssues(nextIssues);
          setAccounts(nextAccounts);
          setSkus(new Map(listings.map((l) => [l.id, l.sellerSku])));
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, "Failed to load the sync queue."))),
    []
  );

  useEffect(() => {
    load();
  }, [load]);

  const accountName = useCallback((id: string | null) => accounts.find((a) => a.id === id)?.name ?? "—", [accounts]);

  const run = async (key: string, action: () => Promise<void>, fallback: string) => {
    setBusy(key);
    try {
      await action();
      await load();
    } catch (err) {
      notify(message(err, fallback), "error");
    } finally {
      setBusy(null);
    }
  };

  // A listing's job is retried by sending the listing again; an order import by asking for another one.
  const retry = (job: SyncJob) =>
    run(job.id, async () => {
      if (job.channelListingId) {
        const queued = await ChannelListingsApi.retry(job.channelListingId);
        notify(queued.liveWrites ? "Queued again." : "Queued again as a dry run: live writes are off.", "success");
      } else {
        await SyncApi.importOrders(job.channelAccountId);
        notify("Another order import was queued.", "success");
      }
      setDetail(null);
    }, "Could not retry.");

  const resolve = (issue: OrderLineIssue) =>
    run(issue.id, async () => {
      await SyncApi.resolveOrderIssue(issue.id);
      notify("Marked as sorted out.", "success");
    }, "Could not mark it as sorted out.");

  const openDetail = (job: SyncJob) =>
    run(`detail:${job.id}`, async () => setDetail(await SyncApi.job(job.id)), "Could not load the job.");

  const canRetry = (job: SyncJob) => NEEDS_ATTENTION.includes(job.status) && (job.channelListingId !== null || job.operation === 4);

  const shown = useMemo(
    () =>
      (jobs ?? []).filter((j) =>
        filter === "all" ? true : (filter === "attention" ? NEEDS_ATTENTION : ACTIVE).includes(j.status)
      ),
    [jobs, filter]
  );

  const jobTable = useMemo(() => {
    type CellProps = { row: { original: SyncJob } };
    const columns = [
      {
        Header: "What",
        id: "what",
        accessor: (job: SyncJob) => `${SYNC_OPERATION_LABELS[job.operation]} ${job.channelListingId ? skus.get(job.channelListingId) ?? "" : ""}`,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{SYNC_OPERATION_LABELS[row.original.operation]}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
              {row.original.channelListingId ? skus.get(row.original.channelListingId) ?? "Listing removed" : "Whole account"}
            </Box>
          </Box>
        ),
      },
      { Header: "Channel", id: "channel", accessor: (job: SyncJob) => accountName(job.channelAccountId) },
      {
        Header: "Status",
        id: "status",
        accessor: (job: SyncJob) => STATUS[job.status].label,
        Cell: ({ row }: CellProps) => <StatusPill {...STATUS[row.original.status]} />,
      },
      {
        Header: "Tries",
        id: "attempts",
        accessor: "attempts",
        align: "right" as const,
        Cell: ({ row }: CellProps) => `${row.original.attempts} of ${row.original.maxAttempts}`,
      },
      {
        Header: "Last answer",
        id: "error",
        accessor: (job: SyncJob) => job.lastError ?? "",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ maxWidth: 360, whiteSpace: "normal", overflowWrap: "anywhere", fontSize: "0.8125rem" }}>{value || "—"}</Box>
        ),
      },
      {
        Header: "Queued",
        accessor: "createdAtUtc",
        Cell: ({ value }: { value: string }) => <Box title={formatDateTime(value)}>{timeAgo(value)}</Box>,
      },
      {
        Header: "",
        id: "actions",
        accessor: "id",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
            <MDButton variant="text" color="info" size="small" disabled={!!busy} onClick={() => openDetail(row.original)}>
              Details
            </MDButton>
            {canRetry(row.original) && (
              <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => retry(row.original)}>
                Retry
              </MDButton>
            )}
          </Box>
        ),
      },
    ];
    return { columns, rows: shown };
    // openDetail/retry only close over state setters and `busy`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shown, skus, accountName, busy, c]);

  const issueTable = useMemo(() => {
    type CellProps = { row: { original: OrderLineIssue } };
    const columns = [
      {
        Header: "Order",
        accessor: "externalOrderId",
        Cell: ({ row }: CellProps) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{row.original.externalOrderId}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{accountName(row.original.channelAccountId)}</Box>
          </Box>
        ),
      },
      { Header: "SKU", id: "sku", accessor: (issue: OrderLineIssue) => issue.sellerSku ?? "—" },
      { Header: "Quantity", accessor: "quantity", align: "right" as const },
      {
        Header: "Problem",
        id: "reason",
        accessor: (issue: OrderLineIssue) => ISSUE_REASON[issue.reason].label,
        Cell: ({ row }: CellProps) => (
          <Box title={ISSUE_REASON[row.original.reason].help}>
            <StatusPill tone={ISSUE_REASON[row.original.reason].tone} label={ISSUE_REASON[row.original.reason].label} />
          </Box>
        ),
      },
      {
        Header: "Found",
        accessor: "createdAtUtc",
        Cell: ({ value }: { value: string }) => <Box title={formatDateTime(value)}>{timeAgo(value)}</Box>,
      },
      {
        Header: "",
        id: "actions",
        accessor: "id",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => resolve(row.original)}>
            Mark sorted out
          </MDButton>
        ),
      },
    ];
    return { columns, rows: issues ?? [] };
    // resolve only closes over state setters and `busy`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [issues, accountName, busy, c]);

  const attention = (health?.failedJobs ?? 0) + (health?.needsCorrectionJobs ?? 0);
  const inFlight = (health?.pendingJobs ?? 0) + (health?.runningJobs ?? 0) + (health?.awaitingRemoteJobs ?? 0);
  const stale = health?.accounts.filter((a) => a.ordersStale) ?? [];
  const loading = !loadError && (!health || !jobs || !issues);

  return (
    <PageShell>
      <PageHeader
        icon="sync"
        title="Sync queue"
        subtitle="What is being sent to your marketplaces, what they answered, and what needs your attention."
        actions={
          <MDButton variant="outlined" color="info" disabled={!!busy} onClick={() => run("refresh", async () => undefined, "Could not refresh.")} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      {loadError && <StateBlock kind="error" title="The sync queue could not be loaded" message={loadError} />}
      {loading && <StateBlock kind="loading" title="Loading the sync queue" />}

      {!loading && !loadError && health && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="report" tone={attention > 0 ? "error" : "success"} label="Need attention" value={attention} hint="Failed or waiting for a correction" />
            <StatCard icon="hourglass_top" tone="info" label="In progress" value={inFlight} hint={health.oldestPendingAtUtc ? `Oldest queued ${timeAgo(health.oldestPendingAtUtc)}` : "Nothing waiting"} />
            <StatCard icon="help_center" tone={issues!.length > 0 ? "warning" : "success"} label="Order lines to sort out" value={issues!.length} hint="Could not be matched or reserved" />
            <StatCard icon="task_alt" tone="success" label="Last success" value={health.lastSuccessAtUtc ? timeAgo(health.lastSuccessAtUtc) : "Never"} hint={health.lastSuccessAtUtc ? formatDateTime(health.lastSuccessAtUtc) : "No marketplace has confirmed anything yet"} />
          </Box>

          {stale.length > 0 && (
            <InlineAlert tone="warning" title="Orders have not been read recently">
              {stale.map((a) => a.name).join(", ")}: stock is not sent on until orders are imported again.
            </InlineAlert>
          )}
          {health.accounts.filter((a) => a.lastError).map((a) => (
            <InlineAlert key={a.id} tone="error" title={`${a.name} refused this account`}>
              {a.lastError}
            </InlineAlert>
          ))}

          <Section
            icon="sync"
            title="Jobs"
            subtitle="The newest 200. A dry run builds and checks everything but sends nothing."
            actions={
              <FilterTabs
                label="Filter jobs"
                value={filter}
                onChange={setFilter}
                options={[
                  { value: "attention", label: "Need attention", count: jobs!.filter((j) => NEEDS_ATTENTION.includes(j.status)).length },
                  { value: "active", label: "In progress", count: jobs!.filter((j) => ACTIVE.includes(j.status)).length },
                  { value: "all", label: "All", count: jobs!.length },
                ]}
              />
            }
            flush
          >
            {shown.length === 0 ? (
              <StateBlock
                icon={filter === "attention" ? "task_alt" : "inbox"}
                title={filter === "attention" ? "Nothing needs attention" : "No jobs here"}
                message={filter === "attention" ? "No job has failed or is waiting for a correction." : "Publishing or changing a listing queues work that shows up here."}
              />
            ) : (
              <DataTable table={jobTable} canSearch />
            )}
          </Section>

          <Section icon="help_center" tone="warning" title="Order lines to sort out" subtitle="Imported order lines that could not be matched to a product or reserved. Nothing is guessed." flush>
            {issues!.length === 0 ? (
              <StateBlock icon="task_alt" title="All order lines were placed" message="Every imported order line matched a product and its stock." />
            ) : (
              <DataTable table={issueTable} canSearch />
            )}
          </Section>
        </Box>
      )}

      <KitDialog
        open={!!detail}
        onClose={() => setDetail(null)}
        icon="sync"
        maxWidth="md"
        title={detail ? SYNC_OPERATION_LABELS[detail.operation] : ""}
        subtitle={detail ? `${accountName(detail.channelAccountId)}${detail.channelListingId ? ` · ${skus.get(detail.channelListingId) ?? "listing removed"}` : ""}` : undefined}
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setDetail(null)}>
              Close
            </MDButton>
            {detail && canRetry(detail) && (
              <MDButton variant="gradient" color="info" disabled={!!busy} onClick={() => retry(detail)}>
                Retry
              </MDButton>
            )}
          </>
        }
      >
        {detail && (
          <Box sx={{ display: "grid", gap: 3 }}>
            <DetailList
              items={[
                { label: "Status", value: <StatusPill {...STATUS[detail.status]} /> },
                { label: "Kind of problem", value: ERROR_CLASS[detail.errorClass] },
                { label: "Tries", value: `${detail.attempts} of ${detail.maxAttempts}` },
                { label: "Mode", value: detail.dryRun ? "Dry run: nothing was sent" : "Live" },
                { label: "Queued", value: formatDateTime(detail.createdAtUtc) },
                {
                  label: detail.completedAtUtc ? "Finished" : "Next try",
                  value: formatDateTime(detail.completedAtUtc ?? detail.nextAttemptAtUtc),
                },
                ...(detail.externalSubmissionId ? [{ label: "Marketplace submission", value: detail.externalSubmissionId }] : []),
              ]}
            />
            {detail.lastError && <InlineAlert tone={detail.status === 5 ? "warning" : "error"}>{detail.lastError}</InlineAlert>}
            <Box>
              <Box sx={{ mb: 1, fontSize: "0.8125rem", fontWeight: 700, color: c.text }}>Attempts</Box>
              {(detail.attemptHistory ?? []).length === 0 && <Box sx={{ fontSize: "0.875rem", color: c.muted }}>Not tried yet.</Box>}
              {(detail.attemptHistory ?? []).map((attempt) => (
                <Box
                  key={attempt.number}
                  sx={{ display: "flex", flexWrap: "wrap", alignItems: "baseline", gap: 1.5, py: 1, borderTop: `1px solid ${c.border}`, fontSize: "0.8125rem" }}
                >
                  <Box sx={{ fontWeight: 700, color: c.text }}>#{attempt.number}</Box>
                  <StatusPill {...STATUS[attempt.outcome]} pulse={false} />
                  <Box sx={{ color: c.muted }}>{formatDateTime(attempt.startedAtUtc)}</Box>
                  {attempt.httpStatus && <Box sx={{ color: c.muted }}>HTTP {attempt.httpStatus}</Box>}
                  {attempt.externalRequestId && <Box sx={{ color: c.muted }}>Request {attempt.externalRequestId}</Box>}
                  {attempt.detail && <Box sx={{ flexBasis: "100%", color: c.text, overflowWrap: "anywhere" }}>{attempt.detail}</Box>}
                </Box>
              ))}
            </Box>
          </Box>
        )}
      </KitDialog>
    </PageShell>
  );
}
