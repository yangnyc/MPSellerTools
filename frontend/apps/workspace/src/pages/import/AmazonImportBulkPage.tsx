import { useCallback, useEffect, useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import Icon from "@mui/material/Icon";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, StateBlock, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ProgressBar from "../../components/ProgressBar";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { AMAZON, AmazonImportApi, BulkJobsApi, settingsPath, type AmazonImportStatus, type BulkJob } from "../../api/channels";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

type Change = React.ChangeEvent<HTMLInputElement>;

// Remembered across a reload, so the import that was started can still be followed.
const LAST_JOB = "amazon-import-job";
const unfinished = (job: BulkJob) => job.status === 0 || job.status === 1;

// A pasted list of Amazon items, handed to a background job that makes each a product.
export default function AmazonImportBulkPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [status, setStatus] = useState<AmazonImportStatus | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [lines, setLines] = useState("");
  const [prefix, setPrefix] = useState("");
  const [updateExisting, setUpdateExisting] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [jobId, setJobId] = useState<string | null>(() => window.sessionStorage.getItem(LAST_JOB));
  const [job, setJob] = useState<BulkJob | null>(null);

  useEffect(() => {
    AmazonImportApi.status()
      .then((next) => {
        setStatus(next);
        setPrefix(next.skuPrefix);
      })
      .catch((err) => setLoadError(message(err, "Failed to load the import page.")));
  }, []);

  const follow = useCallback(() => {
    if (!jobId) return;
    BulkJobsApi.get(jobId)
      .then(setJob)
      // Removed from the Jobs page since: there is nothing left to follow.
      .catch(() => {
        window.sessionStorage.removeItem(LAST_JOB);
        setJobId(null);
        setJob(null);
      });
  }, [jobId]);

  useEffect(() => {
    follow();
  }, [follow]);

  // While the import is waiting or running, the page follows it without being asked.
  const active = !!job && unfinished(job);
  useEffect(() => {
    if (!active) return undefined;
    const timer = window.setInterval(follow, 3000);
    return () => window.clearInterval(timer);
  }, [active, follow]);

  const count = useMemo(() => lines.split("\n").filter((line) => line.trim().length > 0).length, [lines]);
  const tooMany = !!status && count > status.maxItems;

  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      const queued = await AmazonImportApi.importBulk({ lines, skuPrefix: prefix.trim(), updateExisting });
      window.sessionStorage.setItem(LAST_JOB, queued.jobId);
      setJob(null);
      setJobId(queued.jobId);
      setLines("");
      notify(`${queued.total.toLocaleString()} item(s) queued for import.`, "success");
    } catch (err) {
      setError(message(err, "Could not start the import."));
    } finally {
      setBusy(false);
    }
  };

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };

  return (
    <PageShell>
      <PageHeader
        icon="playlist_add"
        title="Import from Amazon: in bulk"
        subtitle="Paste a list of Amazon items; each becomes a product here, in the background."
        actions={
          <MDButton component={RouterLink} to="/import/amazon" variant="outlined" color="info" size="small">
            Import one item
          </MDButton>
        }
      />

      {loadError && <StateBlock kind="error" title="This page could not be loaded" message={loadError} />}
      {!loadError && !status && <StateBlock kind="loading" title="Loading" />}

      {status && !status.ready && (
        <Section flush>
          <StateBlock
            icon="link"
            title="Amazon's catalog cannot be read yet"
            message={status.problem ?? "Add Amazon as a sales channel first."}
            action={
              <MDButton component={RouterLink} to={settingsPath(AMAZON)} variant="gradient" color="info" size="small">
                Set up Amazon
              </MDButton>
            }
          />
        </Section>
      )}

      {status?.ready && (
        <Box sx={{ display: "grid", gap: 3 }}>
          {job && (
            <Section
              icon="playlist_play"
              title={active ? "Import under way" : "Last import"}
              subtitle="It carries on in the background; you can leave this page."
              actions={
                <MDButton component={RouterLink} to="/jobs" variant="text" color="info" size="small">
                  Open Jobs
                </MDButton>
              }
            >
              <Box sx={{ display: "grid", gap: 1.5 }}>
                <ProgressBar
                  label="Import progress"
                  value={job.total > 0 ? (job.processed / job.total) * 100 : undefined}
                  tone={job.status === 4 ? "error" : job.status === 3 ? "warning" : job.status === 2 ? "success" : "info"}
                />
                <Box sx={hint}>
                  {job.processed.toLocaleString()} of {job.total.toLocaleString()} done
                  {job.failed > 0 && ` · ${job.failed.toLocaleString()} held back`}
                  {job.status === 0 && " · waiting for the jobs ahead of it"}
                </Box>
                {job.summary && <InlineAlert tone={job.status === 3 ? "warning" : job.status === 2 ? "success" : "info"}>{job.summary}</InlineAlert>}
                {job.lastError && <InlineAlert tone="error">{job.lastError}</InlineAlert>}
                {job.errors.length > 0 && (
                  <Box>
                    <Box sx={{ mb: 1, fontSize: "0.8125rem", fontWeight: 700, color: c.text }}>
                      Held back{job.failed > job.errors.length ? ` (the first ${job.errors.length} of ${job.failed.toLocaleString()})` : ""}
                    </Box>
                    <Box sx={{ maxHeight: 280, overflowY: "auto" }}>
                      {job.errors.map((held) => (
                        <Box key={held.item} sx={{ display: "flex", flexWrap: "wrap", gap: 1.5, py: 1, borderTop: `1px solid ${c.border}`, fontSize: "0.8125rem" }}>
                          <Box sx={{ fontFamily: "monospace", fontWeight: 700, color: c.text }}>{held.item}</Box>
                          <Box sx={{ flex: 1, minWidth: 220, color: c.text, overflowWrap: "anywhere" }}>{held.message}</Box>
                        </Box>
                      ))}
                    </Box>
                  </Box>
                )}
                {!active && job.succeeded > 0 && (
                  <Box>
                    <MDButton component={RouterLink} to="/products" variant="outlined" color="info" size="small">
                      See the products
                    </MDButton>
                  </Box>
                )}
              </Box>
            </Section>
          )}

          <Section icon="playlist_add" title="The items to import" subtitle={`Read through ${status.accountName}. Nothing is changed on Amazon.`}>
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                if (count > 0 && !tooMany && !busy && !active) start();
              }}
              sx={{ display: "grid", gap: 2.5 }}
            >
              <MDInput
                label="One item to a line"
                multiline
                minRows={8}
                maxRows={20}
                fullWidth
                value={lines}
                onChange={(e: Change) => setLines(e.target.value)}
                placeholder={"B08N5WRWNW\nB07FZ8S74R, MY-SKU-001\nhttps://www.amazon.com/dp/B09G9FPHY6\n012345678905"}
                inputProps={{ spellCheck: false, style: { fontFamily: "monospace" } }}
              />
              <Box sx={hint}>
                Each line is an ASIN, the address of the item's page on Amazon, or a UPC or EAN barcode. To give a product a SKU of your own, put it after a comma on the same line; a
                column of ASINs and a column of SKUs copied from a spreadsheet works as it is. Up to {status.maxItems.toLocaleString()} items at a time.
              </Box>
              <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 2fr" }, gap: 2.5, alignItems: "start" }}>
                <MDInput
                  label="SKU prefix"
                  fullWidth
                  value={prefix}
                  onChange={(e: Change) => setPrefix(e.target.value)}
                  helperText={`A product with no SKU of its own gets this and its ASIN: ${prefix.trim()}B08N5WRWNW`}
                />
                <Box>
                  <FormControlLabel
                    control={<Switch checked={updateExisting} onChange={(e: Change) => setUpdateExisting(e.target.checked)} />}
                    label={<Box sx={{ fontSize: "0.875rem", color: c.text }}>Bring products that are already here up to date</Box>}
                  />
                  <Box sx={hint}>
                    {updateExisting
                      ? "A product already under the SKU gets Amazon's name, brand, description and category. Its price, stock and pictures are kept."
                      : "A product already under the SKU is left as it is, and listed as held back."}
                  </Box>
                </Box>
              </Box>
              <Box sx={hint}>
                Each product gets the item's name, brand, description and bullet points, category, pictures, barcodes, part number, weight and size. Amazon's catalog holds no selling
                price or stock: the price is Amazon's list price where it has one, otherwise 0, and stock starts at 0.
              </Box>
              {tooMany && <InlineAlert tone="warning">That is {count.toLocaleString()} lines; an import takes up to {status.maxItems.toLocaleString()}.</InlineAlert>}
              {error && <InlineAlert tone="error">{error}</InlineAlert>}
              {active && <InlineAlert tone="info">An import is under way. The next one can be started when it has finished.</InlineAlert>}
              <Box sx={{ display: "flex", justifyContent: "flex-end", alignItems: "center", gap: 2 }}>
                <Box sx={hint}>{count.toLocaleString()} line(s)</Box>
                <MDButton type="submit" variant="gradient" color="info" disabled={count === 0 || tooMany || busy || active} startIcon={<Icon>download</Icon>}>
                  {busy ? "Starting…" : "Start import"}
                </MDButton>
              </Box>
            </Box>
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
