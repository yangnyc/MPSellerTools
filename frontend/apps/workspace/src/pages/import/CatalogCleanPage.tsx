import { useCallback, useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, StatCard, StateBlock, StatusPill, formatDateTime, formatMoney, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { useLoaded } from "../../lib/useLoaded";
import { CatalogToolsApi, type CatalogScan, type CleanStrategy, type DuplicateGroup } from "../../api/tools";

type Change = React.ChangeEvent<HTMLInputElement>;

const STRATEGIES: { value: CleanStrategy; label: string; help: string }[] = [
  { value: "exact", label: "Same name", help: "Two products with exactly the same name." },
  { value: "sku", label: "Same code", help: "The same manufacturer code written two ways, such as 0904-7280-80 and 00904728080." },
  { value: "normalized", label: "Same words", help: "The same words in another order or spelling: “Tablets 100 Count” and “100ct tabs”." },
  { value: "fuzzy", label: "Similar name", help: "Names sharing most of their words, with the same strength and size. Look at these before retiring them." },
];
const SHOWN = 50;

// The catalog's health, and its duplicates: the same item entered more than once, with the best of each to keep.
export default function CatalogCleanPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [strategies, setStrategies] = useState<CleanStrategy[]>(["exact", "sku", "normalized"]);
  const [threshold, setThreshold] = useState(80);
  const read = useCallback(() => CatalogToolsApi.scan(strategies, threshold), [strategies, threshold]);
  const { data, error, loading, reload } = useLoaded<CatalogScan>(read, "The catalog could not be scanned.");

  // Which product of a group to keep, where that is not the suggested one, and the groups left out of the clean.
  const [keep, setKeep] = useState<Record<string, string>>({});
  const [left, setLeft] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<"clean" | "undo" | null>(null);

  const groups = useMemo(() => data?.groups ?? [], [data]);
  const chosen = useMemo(() => groups.filter((g) => !left.has(g.key)), [groups, left]);
  const toRetire = chosen.reduce((sum, g) => sum + g.products.length - 1, 0);
  const keeperOf = (group: DuplicateGroup) => keep[group.key] ?? group.keepId;

  const run = async (key: string, action: () => Promise<void>, fallback: string) => {
    setBusy(key);
    try {
      await action();
      await reload();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : fallback, "error");
    } finally {
      setBusy(null);
    }
  };

  const clean = () =>
    run("clean", async () => {
      setConfirming(null);
      const result = await CatalogToolsApi.apply(
        chosen.map((g) => ({ key: g.key, keepId: keeperOf(g), retireIds: g.products.map((p) => p.id).filter((id) => id !== keeperOf(g)) }))
      );
      notify(
        `${result.retired} duplicate(s) archived.` +
          (result.skipped > 0 ? ` ${result.skipped} left alone because they are on sale on a sales channel: take them off sale first, then clean again.` : " Undo brings them back."),
        result.skipped > 0 ? "warning" : "success"
      );
      setKeep({});
      setLeft(new Set());
    }, "Could not clean the catalog.");

  const undo = () =>
    run("undo", async () => {
      setConfirming(null);
      const result = await CatalogToolsApi.undo();
      notify(`${result.restored} product(s) brought back.`, "success");
    }, "Could not undo the last clean.");

  const ignore = (group: DuplicateGroup) =>
    run(group.key, async () => {
      await CatalogToolsApi.ignore(group.key);
      notify("That group will not be shown again.", "success");
    }, "Could not dismiss the group.");

  const toggleStrategy = (value: CleanStrategy) =>
    setStrategies((current) => (current.includes(value) ? current.filter((s) => s !== value) : [...current, value]));

  const health = data?.health;
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };

  return (
    <PageShell>
      <PageHeader
        icon="cleaning_services"
        title="Clean catalog"
        subtitle="What your products still lack, and the same item entered more than once. Nothing is deleted: duplicates are archived, and the last clean can be undone."
        actions={
          <MDButton component={RouterLink} to="/import/website" variant="outlined" color="info" size="small">
            Import from a website
          </MDButton>
        }
      />

      {error && <StateBlock kind="error" title="The catalog could not be scanned" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Scanning the catalog" />}

      {health && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 3 }}>
            <StatCard icon="inventory_2" tone="primary" label="Products" value={health.products.toLocaleString()} hint={`${health.archived.toLocaleString()} archived`} to="/products" />
            <StatCard icon="content_copy" tone={health.duplicateProducts > 0 ? "warning" : "success"} label="Duplicates" value={health.duplicateProducts.toLocaleString()} hint={`in ${health.duplicateGroups.toLocaleString()} group(s)`} />
            <StatCard icon="image_not_supported" tone={health.noPicture > 0 ? "warning" : "success"} label="No picture" value={health.noPicture.toLocaleString()} hint="Refused by most marketplaces" />
            <StatCard icon="notes" tone={health.noDescription > 0 ? "warning" : "success"} label="No description" value={health.noDescription.toLocaleString()} hint="Under 40 characters" />
            <StatCard icon="sell" tone={health.noPrice > 0 ? "error" : "success"} label="No price" value={health.noPrice.toLocaleString()} hint="Would be listed at 0" />
            <StatCard icon="category" tone={health.noCategory > 0 ? "warning" : "success"} label="No category" value={health.noCategory.toLocaleString()} hint="Goes to the store's default" />
            <StatCard icon="branding_watermark" tone={health.noBrand > 0 ? "warning" : "success"} label="No brand" value={health.noBrand.toLocaleString()} hint="Asked for by Amazon and eBay" />
            <StatCard icon="visibility_off" tone="neutral" label="Not listed" value={health.notListed.toLocaleString()} hint="On no sales channel yet" />
          </Box>

          <Section icon="tune" title="What counts as a duplicate" subtitle="Two products are never matched when one costs well over one and a half times the other: that is a case against a single piece.">
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, gap: 1.5 }}>
              {STRATEGIES.map((strategy) => (
                <Box key={strategy.value}>
                  <FormControlLabel
                    control={<Checkbox size="small" checked={strategies.includes(strategy.value)} onChange={() => toggleStrategy(strategy.value)} />}
                    label={<Box component="span" sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{strategy.label}</Box>}
                  />
                  <Box sx={{ ...hint, pl: 3.75 }}>{strategy.help}</Box>
                </Box>
              ))}
            </Box>
            {strategies.includes("fuzzy") && (
              <Box sx={{ mt: 2, maxWidth: 280 }}>
                <MDInput
                  label="How alike, for “Similar name” (%)"
                  type="number"
                  fullWidth
                  inputProps={{ min: 50, max: 100, step: 5 }}
                  value={threshold}
                  onChange={(e: Change) => setThreshold(Math.min(100, Math.max(50, Number(e.target.value) || 80)))}
                  helperText="Lower finds more, and more that are not really the same."
                />
              </Box>
            )}
          </Section>

          <Section
            flush
            icon="content_copy"
            tone={groups.length > 0 ? "warning" : "success"}
            title="Duplicates"
            subtitle={
              groups.length === 0
                ? "None found with what is ticked above."
                : `${groups.length.toLocaleString()} group(s)${groups.length > SHOWN ? `, the first ${SHOWN} shown` : ""}. The product marked Keep stays; the others are archived.`
            }
            actions={
              <Box sx={{ display: "flex", gap: 1 }}>
                {data?.canUndo && (
                  <MDButton variant="outlined" color="secondary" size="small" disabled={!!busy} onClick={() => setConfirming("undo")} startIcon={<Icon>undo</Icon>}>
                    Undo last clean
                  </MDButton>
                )}
                <MDButton variant="gradient" color="info" size="small" disabled={!!busy || toRetire === 0} onClick={() => setConfirming("clean")} startIcon={<Icon>cleaning_services</Icon>}>
                  {busy === "clean" ? "Cleaning…" : `Archive ${toRetire.toLocaleString()} duplicate(s)`}
                </MDButton>
              </Box>
            }
          >
            {data?.lastCleanAtUtc && (
              <Box sx={{ ...hint, px: 3, pt: 2 }}>
                Last clean {formatDateTime(data.lastCleanAtUtc)}: {data.lastCleanRetired.toLocaleString()} archived. {health.retiredAsDuplicates.toLocaleString()} archived as duplicates in all.
              </Box>
            )}
            {groups.length === 0 && <StateBlock icon="task_alt" title="No duplicates found" message="Tick more ways of matching above to look harder." />}
            {groups.slice(0, SHOWN).map((group) => {
              const out = left.has(group.key);
              return (
                <Box key={group.key} sx={{ px: 3, py: 2, borderTop: `1px solid ${c.border}`, opacity: out ? 0.55 : 1 }}>
                  <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 1.5, mb: 1 }}>
                    <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1 }}>
                      <StatusPill tone={group.similarity >= 100 ? "error" : "warning"} label={`${group.reason} · ${group.similarity}% alike`} />
                      <Box sx={hint}>{group.products.length} products</Box>
                    </Box>
                    <Box sx={{ display: "flex", gap: 1 }}>
                      <MDButton variant="text" color="secondary" size="small" disabled={!!busy} onClick={() => setLeft((current) => { const next = new Set(current); if (out) next.delete(group.key); else next.add(group.key); return next; })}>
                        {out ? "Include" : "Skip this time"}
                      </MDButton>
                      <MDButton variant="text" color="secondary" size="small" disabled={!!busy} onClick={() => ignore(group)} title="They are different products: do not show this group again">
                        Not duplicates
                      </MDButton>
                    </Box>
                  </Box>
                  {group.products.map((product) => {
                    const kept = keeperOf(group) === product.id;
                    return (
                      <Box key={product.id} sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1.5, py: 0.75 }}>
                        <MDButton
                          variant={kept ? "gradient" : "outlined"}
                          color={kept ? "success" : "secondary"}
                          size="small"
                          disabled={out || !!busy}
                          onClick={() => setKeep((current) => ({ ...current, [group.key]: product.id }))}
                          aria-label={`Keep ${product.sku}`}
                          sx={{ minWidth: 86 }}
                        >
                          {kept ? "Keep" : "Archive"}
                        </MDButton>
                        <Box sx={{ flex: 1, minWidth: 240, lineHeight: 1.35 }}>
                          <Box component={RouterLink} to={`/products/${product.id}`} sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text, "&:hover": { color: c.accent } }}>
                            {product.name}
                          </Box>
                          <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                            {[product.sku, product.brand, product.category, formatMoney(product.price), `${product.stock} in stock`, `${product.pictures} picture(s)`, product.listings > 0 && `${product.listings} listing(s)`, product.orders > 0 && "sold before"]
                              .filter(Boolean)
                              .join(" · ")}
                          </Box>
                        </Box>
                        <Box sx={hint} title={Object.entries(product.scoreParts).map(([part, points]) => `${part}: ${points}`).join("\n")}>
                          score {product.score}
                        </Box>
                      </Box>
                    );
                  })}
                </Box>
              );
            })}
          </Section>

          {groups.length > 0 && (
            <InlineAlert tone="info">
              A product on sale on a sales channel is never archived from here: take it off sale first. The score says how complete and established a product is (sold before, listed,
              pictures, description); hover it to see why.
            </InlineAlert>
          )}
        </Box>
      )}

      <ConfirmDialog
        open={confirming === "clean"}
        title={`Archive ${toRetire.toLocaleString()} duplicate(s)?`}
        message="In each group the product marked Keep stays and the others are archived: hidden from the catalog, not deleted. Undo last clean brings them back."
        confirmLabel="Archive duplicates"
        confirmColor="warning"
        onConfirm={clean}
        onCancel={() => setConfirming(null)}
      />
      <ConfirmDialog
        open={confirming === "undo"}
        title="Undo the last clean?"
        message={`The ${data?.lastCleanRetired.toLocaleString() ?? ""} product(s) it archived come back into the catalog.`}
        confirmLabel="Bring them back"
        confirmColor="info"
        onConfirm={undo}
        onCancel={() => setConfirming(null)}
      />
    </PageShell>
  );
}
