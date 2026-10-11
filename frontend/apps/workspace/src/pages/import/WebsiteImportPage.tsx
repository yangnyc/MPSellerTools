import { useCallback, useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import Icon from "@mui/material/Icon";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { DetailList, InlineAlert, PageHeader, Section, StatusPill, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ProgressBar from "../../components/ProgressBar";
import { useSnackbar } from "../../components/useSnackbar";
import { JOB_FINISHED, watchJob } from "../../components/jobWatch";
import { ApiError } from "../../lib/api";
import { BulkJobsApi, type BulkJob } from "../../api/channels";
import { CatalogToolsApi, type WebsiteImportOptions } from "../../api/tools";

type Change = React.ChangeEvent<HTMLInputElement>;

// Remembered across a reload, so the import that was started can still be followed.
const LAST_JOB = "website-import-job";
const unfinished = (job: BulkJob) => job.status === 0 || job.status === 1;

const START: WebsiteImportOptions = {
  source: "",
  platform: "shopify",
  rules: "health",
  limit: null,
  existing: "skip",
  updatePrices: false,
  priceAdjustPercent: 0,
  stock: 0,
  maxPictures: 8,
  inStockOnly: false,
  variants: "first",
  allowClinical: false,
  keepUncategorised: false,
  includeNeedsReview: false,
  skuPrefix: null,
  plainTextDescriptions: false,
  dryRun: true,
};

// Another store's catalog brought into the products here, cleaned up on the way: first as a dry run that
// only reports, then for real.
export default function WebsiteImportPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [options, setOptions] = useState<WebsiteImportOptions>(START);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [jobId, setJobId] = useState<string | null>(() => window.sessionStorage.getItem(LAST_JOB));
  const [job, setJob] = useState<BulkJob | null>(null);

  const set = <K extends keyof WebsiteImportOptions>(key: K, value: WebsiteImportOptions[K]) => setOptions((current) => ({ ...current, [key]: value }));

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

  // While it is waiting or running the page follows it; when it is done the job watcher says so, wherever the user is.
  const active = !!job && unfinished(job);
  useEffect(() => {
    if (!active) return undefined;
    const timer = window.setInterval(follow, 3000);
    window.addEventListener(JOB_FINISHED, follow);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener(JOB_FINISHED, follow);
    };
  }, [active, follow]);

  const start = async (dryRun: boolean) => {
    setBusy(true);
    setError(null);
    try {
      const queued = await CatalogToolsApi.importWebsite({ ...options, source: options.source.trim(), skuPrefix: options.skuPrefix?.trim() || null, dryRun });
      window.sessionStorage.setItem(LAST_JOB, queued.jobId);
      setJob(null);
      setJobId(queued.jobId);
      watchJob(queued.jobId);
      notify(
        dryRun
          ? "Reading the store's catalog as a dry run. Nothing is changed; the report appears here and you will get a notification when it is done."
          : "The import has started in the background. You will get a notification when it is done; you can leave this page.",
        "info"
      );
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start the import.");
    } finally {
      setBusy(false);
    }
  };

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const toggle = (key: "inStockOnly" | "allowClinical" | "keepUncategorised" | "includeNeedsReview" | "updatePrices" | "plainTextDescriptions", label: string, help: string) => (
    <Box>
      <FormControlLabel
        control={<Switch checked={options[key]} onChange={(e: Change) => set(key, e.target.checked)} />}
        label={<Box component="span" sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{label}</Box>}
      />
      <Box sx={{ ...hint, pl: 6 }}>{help}</Box>
    </Box>
  );
  const select = <K extends "platform" | "rules" | "existing" | "variants">(key: K, label: string, choices: [WebsiteImportOptions[K], string][], help: string) => (
    <MDInput
      select
      label={label}
      fullWidth
      SelectProps={{ native: true }}
      InputLabelProps={{ shrink: true }}
      value={options[key]}
      onChange={(e: Change) => set(key, e.target.value as WebsiteImportOptions[K])}
      helperText={help}
    >
      {choices.map(([value, text]) => (
        <option key={value} value={value}>
          {text}
        </option>
      ))}
    </MDInput>
  );
  const health = options.rules === "health";
  const ready = options.source.trim().length > 3 && !busy && !active;
  const dryRunDone = !!job && !unfinished(job) && job.summary?.startsWith("Dry run");

  return (
    <PageShell>
      <PageHeader
        icon="travel_explore"
        title="Import from a website"
        subtitle="Bring another store's catalog into your products, cleaned up on the way: unsuitable items left out, descriptions tidied, each product sorted into a category."
        actions={
          <MDButton component={RouterLink} to="/import/clean" variant="outlined" color="info" size="small">
            Clean catalog
          </MDButton>
        }
      />

      <Box sx={{ display: "grid", gap: 3 }}>
        <InlineAlert tone="info" title="Import only what you may publish">
          The names, descriptions and pictures belong to the source store and the brands. Use this for a store of your own, or one whose content you have the right to use.
        </InlineAlert>

        {job && (
          <Section
            icon="playlist_play"
            title={active ? "Under way" : dryRunDone ? "Dry run report" : "Last import"}
            subtitle={active ? "It carries on in the background; you can leave this page." : undefined}
            actions={
              <MDButton component={RouterLink} to="/jobs" variant="text" color="info" size="small">
                Open Jobs
              </MDButton>
            }
          >
            <Box sx={{ display: "grid", gap: 1.5 }}>
              <ProgressBar
                label="Import progress"
                value={job.total > 0 ? (job.processed / job.total) * 100 : job.status === 1 ? undefined : job.status === 0 ? 0 : 100}
                tone={job.status === 4 ? "error" : job.status === 3 ? "warning" : job.status === 2 ? "success" : "info"}
              />
              <Box sx={hint}>
                {job.status === 0
                  ? "Waiting for the jobs ahead of it."
                  : job.total > 0
                    ? `${job.processed.toLocaleString()} of ${job.total.toLocaleString()} products`
                    : job.status === 1
                      ? "Reading the store's catalog…"
                      : ""}
                {job.failed > 0 && ` · ${job.failed.toLocaleString()} held back`}
              </Box>
              {job.summary && <InlineAlert tone={job.status === 3 ? "warning" : job.status === 2 ? "success" : "info"}>{job.summary}</InlineAlert>}
              {job.lastError && <InlineAlert tone="error">{job.lastError}</InlineAlert>}
              {job.report.length > 0 && <DetailList items={job.report.map((line) => ({ label: line.label, value: line.value }))} />}
              {job.errors.length > 0 && (
                <Box sx={{ maxHeight: 240, overflowY: "auto" }}>
                  {job.errors.map((held) => (
                    <Box key={held.item} sx={{ display: "flex", flexWrap: "wrap", gap: 1.5, py: 1, borderTop: `1px solid ${c.border}`, fontSize: "0.8125rem" }}>
                      <Box sx={{ fontFamily: "monospace", fontWeight: 700, color: c.text }}>{held.item}</Box>
                      <Box sx={{ flex: 1, minWidth: 220, color: c.text, overflowWrap: "anywhere" }}>{held.message}</Box>
                    </Box>
                  ))}
                </Box>
              )}
              {dryRunDone && (
                <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 2 }}>
                  <MDButton variant="gradient" color="info" disabled={!ready} onClick={() => start(false)} startIcon={<Icon>download</Icon>}>
                    Import these products
                  </MDButton>
                  <Box sx={hint}>With the options as they are set below.</Box>
                </Box>
              )}
              {!active && !dryRunDone && job.succeeded > 0 && (
                <Box sx={{ display: "flex", gap: 1 }}>
                  <MDButton component={RouterLink} to="/products" variant="outlined" color="info" size="small">
                    See the products
                  </MDButton>
                  <MDButton component={RouterLink} to="/import/clean" variant="outlined" color="info" size="small">
                    Check for duplicates
                  </MDButton>
                </Box>
              )}
            </Box>
          </Section>
        )}

        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, alignItems: "start", gap: 3 }}>
          <Section icon="public" title="The store to import from" subtitle="Its public catalog is read as any visitor's browser reads it. Nothing is changed there.">
            <Box sx={{ display: "grid", gap: 2.5 }}>
              <MDInput label="Store address" fullWidth value={options.source} onChange={(e: Change) => set("source", e.target.value)} helperText="The store's name, such as shop.example.com." />
              {select("platform", "The store runs on", [["shopify", "Shopify"], ["magento", "Magento (Adobe Commerce)"]], "If you are not sure, try Shopify first: the dry run says when the store does not answer as one.")}
              {select(
                "rules",
                "What kind of products",
                [["health", "Health and pharmacy (over-the-counter)"], ["general", "Anything else"]],
                health
                  ? "Prescription drugs are never imported, clinical supplies are left out, and each product is sorted into a health category by its name."
                  : "Nothing is left out for what it is, and each product keeps the product type the source gives it as its category."
              )}
              <Box sx={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 2.5 }}>
                <MDInput
                  label="At most"
                  type="number"
                  fullWidth
                  inputProps={{ min: 1, step: 1 }}
                  value={options.limit ?? ""}
                  onChange={(e: Change) => set("limit", e.target.value === "" ? null : Math.max(1, Number(e.target.value)))}
                  helperText="Products. Empty for all of them."
                />
                <MDInput label="SKU prefix" fullWidth value={options.skuPrefix ?? ""} onChange={(e: Change) => set("skuPrefix", e.target.value)} helperText="For products the source gives no SKU. Empty: made from the store's name." />
              </Box>
            </Box>
          </Section>

          <Section icon="tune" title="How they arrive" subtitle="What each product gets here.">
            <Box sx={{ display: "grid", gap: 2.5 }}>
              <Box sx={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 2.5 }}>
                <MDInput label="Price adjustment %" type="number" fullWidth inputProps={{ step: 1 }} value={options.priceAdjustPercent} onChange={(e: Change) => set("priceAdjustPercent", Number(e.target.value) || 0)} helperText="Added to the source's price; negative takes off." />
                <MDInput label="Stock" type="number" fullWidth inputProps={{ min: 0, step: 1 }} value={options.stock} onChange={(e: Change) => set("stock", Math.max(0, Number(e.target.value) || 0))} helperText="A source only says “available”." />
                <MDInput label="Pictures" type="number" fullWidth inputProps={{ min: 1, max: 12, step: 1 }} value={options.maxPictures} onChange={(e: Change) => set("maxPictures", Math.min(12, Math.max(1, Number(e.target.value) || 1)))} helperText="At most, per product." />
              </Box>
              {select(
                "existing",
                "Products already here",
                [["skip", "Leave them as they are"], ["refresh", "Refresh their name, description and category"]],
                "Matched by SKU. A refreshed product keeps its own pictures and stock."
              )}
              {select(
                "variants",
                "Products with several variants",
                [["first", "One product, from the first variant in stock"], ["all", "Every variant as a product of its own"], ["skip", "Leave them out"]],
                "Pack sizes and the like. Each product here is sold on its own."
              )}
              <Box sx={{ display: "grid", gap: 2, pt: 1, borderTop: `1px solid ${c.border}` }}>
                {options.existing === "refresh" && toggle("updatePrices", "Also update prices", "The price of a product already here follows the source again, with the adjustment above.")}
                {toggle("inStockOnly", "Only what the source has in stock", "Products the source shows as sold out are left out.")}
                {health && toggle("allowClinical", "Keep clinical supplies", "Syringes, catheters, exam gloves and the like, which are otherwise left out. Prescription drugs are still never imported.")}
                {health && toggle("includeNeedsReview", "Include items that need a review", "Things that read like a medicine but name no common over-the-counter ingredient or brand. Left out unless you will check each one yourself.")}
                {health && toggle("keepUncategorised", "Keep products that fit no category", "They are imported without a category instead of being left out.")}
                {toggle("plainTextDescriptions", "Descriptions as plain text", "Otherwise they keep simple formatting (paragraphs, lists, headings), which stores show as written.")}
              </Box>
            </Box>
          </Section>
        </Box>

        <Section icon="rocket_launch" title="Start" subtitle="A dry run reads the whole catalog and reports what would be imported, by category and with what was left out and why. Nothing is changed.">
          {error && <InlineAlert tone="error" sx={{ mb: 2 }}>{error}</InlineAlert>}
          {active && <InlineAlert tone="info" sx={{ mb: 2 }}>An import is under way. The next one can be started when it has finished.</InlineAlert>}
          <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 2 }}>
            <MDButton variant="gradient" color="info" disabled={!ready} onClick={() => start(true)} startIcon={<Icon>preview</Icon>}>
              {busy ? "Starting…" : "Dry run first"}
            </MDButton>
            <MDButton variant="outlined" color="info" disabled={!ready} onClick={() => start(false)} startIcon={<Icon>download</Icon>}>
              Import now
            </MDButton>
            <StatusPill tone="neutral" label="Reading a large store takes a few minutes" />
          </Box>
        </Section>
      </Box>
    </PageShell>
  );
}
