import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import LinearProgress from "@mui/material/LinearProgress";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
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
import PageShell from "../components/PageShell";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import {
  BULK_JOB_TYPES,
  BulkJobsApi,
  ChannelsApi,
  type BulkJob,
  type BulkJobStatus,
  type BulkJobType,
  type ChannelAccount,
} from "../api/channels";

const STATUS: Record<BulkJobStatus, { label: string; tone: KitTone; pulse?: boolean }> = {
  0: { label: "Waiting", tone: "neutral" },
  1: { label: "Running", tone: "info", pulse: true },
  2: { label: "Done", tone: "success" },
  3: { label: "Done, some held back", tone: "warning" },
  4: { label: "Failed", tone: "error" },
  5: { label: "Stopped", tone: "neutral" },
};

type Filter = "active" | "attention" | "all";
const UNFINISHED: BulkJobStatus[] = [0, 1];
const NEEDS_A_LOOK: BulkJobStatus[] = [3, 4];

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);
const unfinished = (job: BulkJob) => UNFINISHED.includes(job.status);
type Change = React.ChangeEvent<HTMLInputElement>;

// How far a job has got. One that is not counted in items (reading a store) only shows that it is busy.
function Progress({ job }: { job: BulkJob }) {
  const { c } = useKit();
  const counted = job.total > 0;
  const percent = counted ? Math.round((job.processed / job.total) * 100) : job.status === 1 ? 0 : 100;
  return (
    <Box sx={{ minWidth: 160 }}>
      <LinearProgress
        aria-label={`${BULK_JOB_TYPES[job.type].label} progress`}
        variant={job.status === 1 && !counted ? "indeterminate" : "determinate"}
        value={job.status === 0 ? 0 : percent}
        color={job.status === 4 ? "error" : job.status === 3 ? "warning" : job.status === 2 ? "success" : "info"}
        sx={{ height: 6, borderRadius: 3 }}
      />
      <Box sx={{ mt: 0.5, fontSize: "0.75rem", color: c.muted }}>
        {counted ? `${job.processed.toLocaleString()} of ${job.total.toLocaleString()}` : job.status === 0 ? "Not started" : job.status === 1 ? "Working…" : "—"}
        {job.failed > 0 && ` · ${job.failed.toLocaleString()} held back`}
      </Box>
    </Box>
  );
}

// Large pieces of work on a sales channel's listings: asked for here, carried out in the background.
export default function JobsPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [jobs, setJobs] = useState<BulkJob[] | null>(null);
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("all");
  const [busy, setBusy] = useState<string | null>(null);
  const [detailId, setDetailId] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [newType, setNewType] = useState<BulkJobType>(0);
  const [newAccount, setNewAccount] = useState("");

  const load = useCallback(
    () =>
      Promise.all([BulkJobsApi.list(), ChannelsApi.list()])
        .then(([nextJobs, nextAccounts]) => {
          setJobs(nextJobs);
          // The company's own website has no listings to work through.
          setAccounts(nextAccounts.filter((a) => a.channel !== 3));
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, "Failed to load the jobs."))),
    []
  );

  useEffect(() => {
    load();
  }, [load]);

  // While anything is waiting or running, the page follows it without being asked.
  const active = (jobs ?? []).some(unfinished);
  useEffect(() => {
    if (!active) return undefined;
    const timer = window.setInterval(load, 3000);
    return () => window.clearInterval(timer);
  }, [active, load]);

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

  const start = () =>
    run("start", async () => {
      await BulkJobsApi.start(newType, newAccount);
      setStarting(false);
      notify("The job was queued. It starts when the ones ahead of it are done.", "success");
    }, "Could not queue the job.");

  const stop = (job: BulkJob) =>
    run(job.id, async () => {
      await BulkJobsApi.cancel(job.id);
      notify(job.status === 0 ? "The job was cancelled." : "The job stops after the items it is on.", "success");
    }, "Could not stop the job.");

  const runAgain = (job: BulkJob) =>
    run(job.id, async () => {
      await BulkJobsApi.runAgain(job.id);
      setDetailId(null);
      notify("Queued again. It works on whatever is left to do.", "success");
    }, "Could not queue the job again.");

  const remove = (job: BulkJob) =>
    run(job.id, async () => {
      await BulkJobsApi.remove(job.id);
      setDetailId(null);
    }, "Could not remove the job.");

  const openNew = () => {
    setNewType(0);
    setNewAccount(accounts[0]?.id ?? "");
    setStarting(true);
  };

  const shown = useMemo(
    () => (jobs ?? []).filter((j) => (filter === "all" ? true : (filter === "active" ? UNFINISHED : NEEDS_A_LOOK).includes(j.status))),
    [jobs, filter]
  );
  const detail = jobs?.find((j) => j.id === detailId) ?? null;
  const chosenAccount = accounts.find((a) => a.id === newAccount);
  // Only eBay and a Magento store can be read on request.
  const canRead = chosenAccount?.channel === 0 || chosenAccount?.channel === 4;

  const table = useMemo(() => {
    type CellProps = { row: { original: BulkJob } };
    const columns = [
      {
        Header: "Job",
        id: "job",
        accessor: (job: BulkJob) => `${BULK_JOB_TYPES[job.type].label} ${job.accountName ?? ""}`,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{BULK_JOB_TYPES[row.original.type].label}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{row.original.accountName ?? "Sales channel removed"}</Box>
          </Box>
        ),
      },
      {
        Header: "Progress",
        id: "progress",
        accessor: (job: BulkJob) => (job.total > 0 ? job.processed / job.total : 0),
        Cell: ({ row }: CellProps) => <Progress job={row.original} />,
      },
      {
        Header: "Status",
        id: "status",
        accessor: (job: BulkJob) => STATUS[job.status].label,
        Cell: ({ row }: CellProps) => <StatusPill {...STATUS[row.original.status]} />,
      },
      {
        Header: "Result",
        id: "result",
        accessor: (job: BulkJob) => job.lastError ?? job.summary ?? "",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ maxWidth: 320, whiteSpace: "normal", overflowWrap: "anywhere", fontSize: "0.8125rem" }}>{value || "—"}</Box>
        ),
      },
      {
        Header: "Asked for",
        accessor: "createdAtUtc",
        Cell: ({ row }: CellProps) => (
          <Box sx={{ lineHeight: 1.35 }} title={formatDateTime(row.original.createdAtUtc)}>
            <Box>{timeAgo(row.original.createdAtUtc)}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{row.original.createdByEmail}</Box>
          </Box>
        ),
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
            <MDButton variant="text" color="info" size="small" onClick={() => setDetailId(row.original.id)} aria-label={`Details of ${BULK_JOB_TYPES[row.original.type].label}`}>
              Details
            </MDButton>
            {unfinished(row.original) ? (
              <MDButton variant="outlined" color="secondary" size="small" disabled={!!busy || row.original.cancelRequested} onClick={() => stop(row.original)}>
                {row.original.cancelRequested ? "Stopping…" : "Stop"}
              </MDButton>
            ) : (
              <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => runAgain(row.original)}>
                Run again
              </MDButton>
            )}
          </Box>
        ),
      },
    ];
    return { columns, rows: shown };
    // stop/runAgain only close over state setters and `busy`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shown, busy, c]);

  const count = (statuses: BulkJobStatus[]) => (jobs ?? []).filter((j) => statuses.includes(j.status)).length;
  const loading = !loadError && !jobs;
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };

  return (
    <PageShell>
      <PageHeader
        icon="playlist_play"
        title="Jobs"
        subtitle="Large pieces of work on a sales channel's listings, carried out in the background one after another."
        actions={
          <MDButton variant="gradient" color="info" disabled={accounts.length === 0} onClick={openNew} startIcon={<Icon>add</Icon>}>
            New job
          </MDButton>
        }
      />

      {loadError && <StateBlock kind="error" title="The jobs could not be loaded" message={loadError} />}
      {loading && <StateBlock kind="loading" title="Loading the jobs" />}

      {!loading && !loadError && jobs && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="play_circle" tone="info" label="Running" value={count([1])} hint="One at a time, oldest first" />
            <StatCard icon="hourglass_top" tone="neutral" label="Waiting" value={count([0])} hint="Start when the ones ahead are done" />
            <StatCard icon="report" tone={count(NEEDS_A_LOOK) > 0 ? "warning" : "success"} label="Need a look" value={count(NEEDS_A_LOOK)} hint="Failed, or finished with items held back" />
            <StatCard icon="task_alt" tone="success" label="Done" value={count([2])} hint="Finished with nothing held back" />
          </Box>

          <Section
            icon="playlist_play"
            title="Jobs"
            subtitle="The newest 200. A job queues its listings; the sync queue then sends each one to the marketplace."
            actions={
              <FilterTabs
                label="Filter jobs"
                value={filter}
                onChange={setFilter}
                options={[
                  { value: "all", label: "All", count: jobs.length },
                  { value: "active", label: "In progress", count: count(UNFINISHED) },
                  { value: "attention", label: "Need a look", count: count(NEEDS_A_LOOK) },
                ]}
              />
            }
            flush
          >
            {shown.length === 0 ? (
              <StateBlock
                icon="playlist_play"
                title={jobs.length === 0 ? "No jobs yet" : "No jobs here"}
                message={
                  accounts.length === 0
                    ? "Add a sales channel first; a job works on that channel's listings."
                    : "Start a job to publish every draft on a sales channel, take everything off sale, or read what a store has."
                }
                action={
                  jobs.length === 0 &&
                  accounts.length > 0 && (
                    <MDButton variant="gradient" color="info" size="small" onClick={openNew}>
                      New job
                    </MDButton>
                  )
                }
              />
            ) : (
              <DataTable table={table} canSearch />
            )}
          </Section>
        </Box>
      )}

      <KitDialog
        open={starting}
        onClose={() => setStarting(false)}
        icon="playlist_add"
        title="New job"
        subtitle="It is queued and carried out in the background; you can leave this page."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setStarting(false)}>
              Cancel
            </MDButton>
            <MDButton variant="gradient" color="info" disabled={!!busy || !newAccount || (newType === 4 && !canRead)} onClick={start}>
              {busy === "start" ? "Queuing…" : "Start job"}
            </MDButton>
          </>
        }
      >
        <Box sx={{ display: "grid", gap: 2.5 }}>
          <MDInput select label="Sales channel" fullWidth SelectProps={{ native: true }} InputLabelProps={{ shrink: true }} value={newAccount} onChange={(e: Change) => setNewAccount(e.target.value)}>
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </MDInput>
          <MDInput
            select
            label="What to do"
            fullWidth
            SelectProps={{ native: true }}
            InputLabelProps={{ shrink: true }}
            value={newType}
            onChange={(e: Change) => setNewType(Number(e.target.value) as BulkJobType)}
          >
            {(Object.keys(BULK_JOB_TYPES) as unknown as string[]).map((key) => (
              <option key={key} value={key}>
                {BULK_JOB_TYPES[Number(key) as BulkJobType].label}
              </option>
            ))}
          </MDInput>
          <Box sx={hint}>{BULK_JOB_TYPES[newType].help}</Box>
          {newType === 4 && chosenAccount && !canRead && (
            <InlineAlert tone="warning">{chosenAccount.name} reports its listings through the sync queue; there is nothing to read on request.</InlineAlert>
          )}
          {chosenAccount && newType !== 3 && newType !== 4 && !chosenAccount.effectiveLiveWrites && (
            <InlineAlert tone="warning" title="A dry run">
              Live writes are off for {chosenAccount.name}, so the listings are prepared and checked but nothing is sent to it.
            </InlineAlert>
          )}
        </Box>
      </KitDialog>

      <KitDialog
        open={!!detail}
        onClose={() => setDetailId(null)}
        icon="playlist_play"
        maxWidth="md"
        title={detail ? BULK_JOB_TYPES[detail.type].label : ""}
        subtitle={detail?.accountName ?? undefined}
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setDetailId(null)}>
              Close
            </MDButton>
            {detail && !unfinished(detail) && (
              <>
                <MDButton variant="text" color="error" disabled={!!busy} onClick={() => remove(detail)}>
                  Remove from the list
                </MDButton>
                <MDButton variant="gradient" color="info" disabled={!!busy} onClick={() => runAgain(detail)}>
                  Run again
                </MDButton>
              </>
            )}
            {detail && unfinished(detail) && (
              <MDButton variant="gradient" color="secondary" disabled={!!busy || detail.cancelRequested} onClick={() => stop(detail)}>
                {detail.cancelRequested ? "Stopping…" : "Stop"}
              </MDButton>
            )}
          </>
        }
      >
        {detail && (
          <Box sx={{ display: "grid", gap: 3 }}>
            <Progress job={detail} />
            <DetailList
              items={[
                { label: "Status", value: <StatusPill {...STATUS[detail.status]} /> },
                { label: "Done", value: detail.succeeded.toLocaleString() },
                { label: "Held back", value: detail.failed.toLocaleString() },
                { label: "Asked for by", value: detail.createdByEmail },
                { label: "Asked for", value: formatDateTime(detail.createdAtUtc) },
                { label: "Started", value: detail.startedAtUtc ? formatDateTime(detail.startedAtUtc) : "Not yet" },
                { label: "Finished", value: detail.finishedAtUtc ? formatDateTime(detail.finishedAtUtc) : "Not yet" },
              ]}
            />
            {detail.summary && <InlineAlert tone={detail.status === 3 ? "warning" : "info"}>{detail.summary}</InlineAlert>}
            {detail.lastError && <InlineAlert tone="error">{detail.lastError}</InlineAlert>}
            {detail.errors.length > 0 && (
              <Box>
                <Box sx={{ mb: 1, fontSize: "0.8125rem", fontWeight: 700, color: c.text }}>
                  Held back{detail.failed > detail.errors.length ? ` (the first ${detail.errors.length} of ${detail.failed.toLocaleString()})` : ""}
                </Box>
                {detail.errors.map((error) => (
                  <Box key={error.item} sx={{ display: "flex", flexWrap: "wrap", gap: 1.5, py: 1, borderTop: `1px solid ${c.border}`, fontSize: "0.8125rem" }}>
                    <Box sx={{ fontFamily: "monospace", fontWeight: 700, color: c.text }}>{error.item}</Box>
                    <Box sx={{ flex: 1, minWidth: 220, color: c.text, overflowWrap: "anywhere" }}>{error.message}</Box>
                  </Box>
                ))}
              </Box>
            )}
          </Box>
        )}
      </KitDialog>
    </PageShell>
  );
}
