import { useCallback, useEffect, useMemo, useState } from "react";
import { Link as RouterLink, useNavigate } from "react-router-dom";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import Icon from "@mui/material/Icon";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import { FilterTabs, InlineAlert, KitDialog, PageHeader, Section, StatCard, StateBlock, StatusPill, useKit, type KitTone } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import {
  BulkJobsApi,
  ChannelsApi,
  MAGENTO,
  MagentoCategoriesApi,
  settingsPath,
  type ChannelAccount,
  type MagentoCategories,
  type MagentoCategoryBulk,
  type MagentoCategoryRow,
  type MagentoStoreCategory,
} from "../../api/channels";

type Filter = "all" | "unmapped" | "mapped" | "unused";
type Change = React.ChangeEvent<HTMLInputElement>;

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// Where a category of the company's stands with the store.
function state(row: MagentoCategoryRow): { label: string; tone: KitTone } {
  if (row.storeCategoryMissing) return { label: "Store category gone", tone: "error" };
  if (!row.storeCategoryId) return { label: "Not mapped", tone: "warning" };
  if (row.products === 0) return { label: "Mapped, no products", tone: "neutral" };
  return { label: "Mapped", tone: "success" };
}

// A store category as an option of a dropdown, indented by how deep it sits in the tree.
const option = (category: MagentoStoreCategory) => `${"  ".repeat(Math.max(0, category.level - 1))}${category.name}`;

// The company's product categories beside the Magento store's own: which goes where, and making the store match.
export default function MagentoCategoriesPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();
  const navigate = useNavigate();

  const [data, setData] = useState<MagentoCategories | null>(null);
  const [account, setAccount] = useState<ChannelAccount | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("all");
  // The options the categories this page creates in the store are made with.
  const [isActive, setIsActive] = useState(true);
  const [includeInMenu, setIncludeInMenu] = useState(true);
  const [plan, setPlan] = useState<MagentoCategoryBulk | null>(null);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState("");
  const [newParent, setNewParent] = useState("");
  const [defaultChoice, setDefaultChoice] = useState("");

  const load = useCallback(
    () =>
      Promise.all([MagentoCategoriesApi.get(), ChannelsApi.list()])
        .then(([next, accounts]) => {
          setData(next);
          setDefaultChoice(next.defaultCategoryId ?? "");
          setAccount(accounts.find((a) => a.channel === MAGENTO.kind) ?? null);
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, "Failed to load the categories."))),
    []
  );

  useEffect(() => {
    load();
  }, [load]);

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

  const market = account?.markets[0];
  const store = data?.storeCategories ?? [];

  // Choosing a store category for one of the company's saves it at once; choosing none removes the mapping.
  const map = (row: MagentoCategoryRow, storeCategoryId: string) =>
    run(`map:${row.category}`, async () => {
      if (storeCategoryId) {
        if (!market) return;
        await ChannelsApi.saveCategoryMapping(market.id, row.category, storeCategoryId);
        notify(`${row.category} now goes to ${store.find((s) => String(s.id) === storeCategoryId)?.path ?? storeCategoryId}.`, "success");
      } else if (row.mappingId) {
        await ChannelsApi.removeCategoryMapping(row.mappingId);
        notify(`${row.category} is no longer mapped.`, "success");
      }
    }, "Could not save the mapping.");

  const match = () =>
    run("match", async () => {
      const result = await MagentoCategoriesApi.match();
      notify(result.mapped > 0 ? `${result.mapped} matched to store categories of the same name.` : "No unmapped category has a namesake in the store.", result.mapped > 0 ? "success" : "info");
    }, "Could not match the categories.");

  const preview = () =>
    run("preview", async () => setPlan(await MagentoCategoriesApi.createMissing({ isActive, includeInMenu, dryRun: true })), "Could not work out what is missing.");

  const createMissing = () =>
    run("create-missing", async () => {
      const result = await MagentoCategoriesApi.createMissing({ isActive, includeInMenu, dryRun: false });
      setPlan(null);
      const failed = result.items.filter((i) => i.error).length;
      notify(
        `${result.created} created in the store, ${result.mapped} mapped.${failed > 0 ? ` ${failed} could not be: ${result.items.find((i) => i.error)?.error}` : ""}`,
        failed > 0 ? "warning" : "success"
      );
    }, "Could not create the categories.");

  const createOne = () =>
    run("create-one", async () => {
      const created = await MagentoCategoriesApi.create({ name: newName.trim(), parentId: newParent ? Number(newParent) : null, isActive, includeInMenu });
      setCreating(false);
      setNewName("");
      notify(`${created.path} was created in the store.`, "success");
    }, "Could not create the category.");

  const saveDefault = () =>
    run("default", async () => {
      await MagentoCategoriesApi.setDefault(defaultChoice || null);
      notify(defaultChoice ? "Unmapped products now go to that category." : "Unmapped products now go to no category.", "success");
    }, "Could not save the default category.");

  const removeUnused = () =>
    run("unused", async () => {
      const result = await MagentoCategoriesApi.removeUnused();
      notify(`${result.removed} unused mapping(s) removed.`, "success");
    }, "Could not remove the unused mappings.");

  // A mapping only reaches a product already in the store when the product is sent again.
  const sendAgain = () =>
    run("resend", async () => {
      if (!account) return;
      await BulkJobsApi.start(5, account.id);
      notify("Sending every published product again was queued as a job.", "success");
      navigate("/jobs");
    }, "Could not queue the job.");

  const rows = data?.categories ?? [];
  const unmapped = rows.filter((r) => !r.storeCategoryId && r.products > 0);
  const unused = rows.filter((r) => r.products === 0);
  const broken = rows.filter((r) => r.storeCategoryMissing);
  const shown = useMemo(
    () =>
      rows.filter((r) =>
        filter === "all" ? true : filter === "unmapped" ? !r.storeCategoryId || r.storeCategoryMissing : filter === "mapped" ? !!r.storeCategoryId && !r.storeCategoryMissing : r.products === 0
      ),
    // `rows` is a new array each render; the data it comes from is what changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data, filter]
  );

  const table = useMemo(() => {
    type CellProps = { row: { original: MagentoCategoryRow } };
    const columns = [
      {
        Header: "Your category",
        accessor: "category",
        Cell: ({ row }: CellProps) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{row.original.category}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
              {row.original.products === 1 ? "1 product" : `${row.original.products.toLocaleString()} products`}
            </Box>
          </Box>
        ),
      },
      {
        Header: "Goes to, in the store",
        id: "store",
        accessor: (row: MagentoCategoryRow) => row.storeCategoryPath ?? row.storeCategoryId ?? "",
        Cell: ({ row }: CellProps) => (
          <MDInput
            select
            size="small"
            fullWidth
            SelectProps={{ native: true }}
            inputProps={{ "aria-label": `Store category for ${row.original.category}` }}
            disabled={!!busy || !market}
            value={row.original.storeCategoryId ?? ""}
            onChange={(e: Change) => map(row.original, e.target.value)}
            sx={{ minWidth: 220 }}
          >
            <option value="">Not mapped</option>
            {/* A number the store no longer has is still shown, so it can be seen and changed. */}
            {row.original.storeCategoryId && !store.some((s) => String(s.id) === row.original.storeCategoryId) && (
              <option value={row.original.storeCategoryId}>Category {row.original.storeCategoryId}{data?.storeReachable ? " (not in the store)" : ""}</option>
            )}
            {store.map((category) => (
              <option key={category.id} value={category.id}>
                {option(category)}
              </option>
            ))}
          </MDInput>
        ),
      },
      {
        Header: "Status",
        id: "status",
        accessor: (row: MagentoCategoryRow) => state(row).label,
        Cell: ({ row }: CellProps) => <StatusPill {...state(row.original)} />,
      },
    ];
    return { columns, rows: shown };
    // map only closes over state setters, `busy` and the store's categories.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shown, busy, c, data, market]);

  const loading = !loadError && !data;
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const canWrite = !!data?.liveWrites && !!data.storeReachable;
  const toggle = (checked: boolean, onChange: (value: boolean) => void, label: string) => (
    <FormControlLabel
      control={<Switch checked={checked} onChange={(e: Change) => onChange(e.target.checked)} />}
      label={<Box component="span" sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{label}</Box>}
    />
  );
  const creationOptions = (
    <Box sx={{ display: "flex", flexWrap: "wrap", columnGap: 3 }}>
      {toggle(isActive, setIsActive, "Enabled in the store")}
      {toggle(includeInMenu, setIncludeInMenu, "Shown in the store's menu")}
    </Box>
  );

  return (
    <PageShell>
      <PageHeader
        icon="account_tree"
        title="Magento categories"
        subtitle="Your product categories beside the store's own: which goes where, and making the store match."
        actions={
          <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1 }}>
            <MDButton variant="outlined" color="info" disabled={!!busy} onClick={() => run("refresh", async () => undefined, "Could not refresh.")} startIcon={<Icon>refresh</Icon>}>
              Refresh
            </MDButton>
            <MDButton variant="gradient" color="info" disabled={!!busy || !canWrite} onClick={() => { setNewParent(""); setCreating(true); }} startIcon={<Icon>add</Icon>}>
              New store category
            </MDButton>
          </Box>
        }
      />

      {loadError && (
        <StateBlock
          kind="error"
          title="The categories could not be loaded"
          message={loadError}
          action={
            <MDButton component={RouterLink} to={MAGENTO.path} variant="outlined" color="info" size="small">
              Set up Magento
            </MDButton>
          }
        />
      )}
      {loading && <StateBlock kind="loading" title="Loading the categories" />}

      {!loading && !loadError && data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          {!data.storeReachable && (
            <InlineAlert tone="error" title="The store could not be read">
              {data.storeError} Your mappings are shown, but the store's own categories are not, and nothing can be created there until it answers.
            </InlineAlert>
          )}
          {data.storeReachable && !data.liveWrites && (
            <InlineAlert tone="warning" title="Nothing is created in the store">
              Live writes are off for Magento, so categories can be mapped here but not created in the store. They are switched on on the{" "}
              <Box component={RouterLink} to={settingsPath(MAGENTO)} sx={{ color: c.accent }}>
                Connection page
              </Box>
              .
            </InlineAlert>
          )}
          {broken.length > 0 && (
            <InlineAlert tone="error" title="Mapped to categories the store no longer has">
              {broken.map((r) => r.category).join(", ")}. Choose another store category for each, or products in them are refused when sent.
            </InlineAlert>
          )}

          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="category" tone="info" label="Your categories" value={rows.filter((r) => r.products > 0).length} hint="That products are in" />
            <StatCard icon="link" tone="success" label="Mapped" value={rows.filter((r) => r.storeCategoryId && !r.storeCategoryMissing && r.products > 0).length} hint="Go to a store category" />
            <StatCard icon="link_off" tone={unmapped.length > 0 ? "warning" : "success"} label="Not mapped" value={unmapped.length} hint={data.defaultCategoryId ? "Go to the default category" : "Go to no category"} />
            <StatCard icon="account_tree" tone="primary" label="Store categories" value={data.storeReachable ? store.length : "—"} hint="In the Magento store now" />
          </Box>

          <Section
            icon="category"
            title="Your categories"
            subtitle="Choose where each goes in the store. A choice is saved at once."
            actions={
              <FilterTabs
                label="Filter categories"
                value={filter}
                onChange={setFilter}
                options={[
                  { value: "all", label: "All", count: rows.length },
                  { value: "unmapped", label: "Not mapped", count: rows.filter((r) => !r.storeCategoryId || r.storeCategoryMissing).length },
                  { value: "mapped", label: "Mapped", count: rows.filter((r) => r.storeCategoryId && !r.storeCategoryMissing).length },
                  { value: "unused", label: "No products", count: unused.length },
                ]}
              />
            }
            flush
          >
            {shown.length === 0 ? (
              <StateBlock
                icon="category"
                title={rows.length === 0 ? "No categories yet" : "None here"}
                message={rows.length === 0 ? "Give your products a category and they show up here to be mapped." : "No category matches this filter."}
              />
            ) : (
              <DataTable table={table} canSearch />
            )}
          </Section>

          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, alignItems: "start", gap: 3 }}>
            <Section icon="auto_fix_high" title="Make the store match" subtitle="For the categories of yours that are not mapped yet.">
              <Box sx={{ display: "grid", gap: 2.5 }}>
                <Box>
                  <Box sx={{ ...hint, mb: 1 }}>
                    Maps each unmapped category to the store category with the same name, where there is exactly one. Nothing changes in the store.
                  </Box>
                  <MDButton variant="outlined" color="info" disabled={!!busy || !data.storeReachable || unmapped.length === 0} onClick={match} startIcon={<Icon>join_inner</Icon>}>
                    {busy === "match" ? "Matching…" : "Match by name"}
                  </MDButton>
                </Box>
                <Box sx={{ pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                  <Box sx={{ ...hint, mb: 1 }}>
                    Creates in the store every category of yours it does not have, and maps it. A category written “A / B” becomes B inside A.
                  </Box>
                  {creationOptions}
                  <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, mt: 1.5 }}>
                    <MDButton variant="outlined" color="info" disabled={!!busy || !data.storeReachable || unmapped.length === 0} onClick={preview} startIcon={<Icon>visibility</Icon>}>
                      {busy === "preview" ? "Working it out…" : "Preview"}
                    </MDButton>
                    <MDButton variant="gradient" color="info" disabled={!!busy || !canWrite || unmapped.length === 0} onClick={createMissing} startIcon={<Icon>create_new_folder</Icon>}>
                      {busy === "create-missing" ? "Creating…" : `Create ${unmapped.length} missing in the store`}
                    </MDButton>
                  </Box>
                </Box>
              </Box>
            </Section>

            <Section icon="tune" title="Options" subtitle="How unmapped products, old mappings and published products are treated.">
              <Box sx={{ display: "grid", gap: 2.5 }}>
                <Box>
                  <Box sx={{ ...hint, mb: 1.5 }}>Where a product goes when its own category is not mapped.</Box>
                  <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "flex-start", gap: 1 }}>
                    <MDInput
                      select
                      label="Default category"
                      size="small"
                      SelectProps={{ native: true }}
                      InputLabelProps={{ shrink: true }}
                      value={defaultChoice}
                      onChange={(e: Change) => setDefaultChoice(e.target.value)}
                      sx={{ flex: 1, minWidth: 220 }}
                    >
                      <option value="">No category</option>
                      {defaultChoice && !store.some((s) => String(s.id) === defaultChoice) && <option value={defaultChoice}>Category {defaultChoice}</option>}
                      {store.map((category) => (
                        <option key={category.id} value={category.id}>
                          {option(category)}
                        </option>
                      ))}
                    </MDInput>
                    <MDButton variant="outlined" color="info" disabled={!!busy || defaultChoice === (data.defaultCategoryId ?? "")} onClick={saveDefault}>
                      {busy === "default" ? "Saving…" : "Save default"}
                    </MDButton>
                  </Box>
                </Box>
                <Box sx={{ pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                  <Box sx={{ ...hint, mb: 1 }}>
                    A product already in the store keeps its old category until it is sent again. This queues every published product as a job.
                  </Box>
                  <MDButton variant="outlined" color="info" disabled={!!busy || !account} onClick={sendAgain} startIcon={<Icon>playlist_play</Icon>}>
                    {busy === "resend" ? "Queuing…" : "Send published products again"}
                  </MDButton>
                </Box>
                <Box sx={{ pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                  <Box sx={{ ...hint, mb: 1 }}>Mappings of categories no product has any more. Removing them changes nothing in the store.</Box>
                  <MDButton variant="outlined" color="secondary" disabled={!!busy || unused.length === 0} onClick={removeUnused} startIcon={<Icon>cleaning_services</Icon>}>
                    {busy === "unused" ? "Removing…" : `Remove ${unused.length} unused mapping${unused.length === 1 ? "" : "s"}`}
                  </MDButton>
                </Box>
              </Box>
            </Section>
          </Box>

          <Section icon="account_tree" title="The store's categories" subtitle="As the Magento store has them now, with how many products each holds.">
            {!data.storeReachable && <Box sx={hint}>Not available while the store cannot be read.</Box>}
            {data.storeReachable && store.length === 0 && <Box sx={hint}>The store has no categories.</Box>}
            {store.map((category) => (
              <Box
                key={category.id}
                sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2, py: 0.75, pl: (category.level - 1) * 3, borderTop: category.level > 1 ? `1px solid ${c.border}` : "none", fontSize: "0.875rem" }}
              >
                <Box sx={{ display: "flex", alignItems: "center", gap: 1, minWidth: 0 }}>
                  <Icon sx={{ fontSize: "1rem !important", color: c.muted }}>{category.level === 1 ? "home" : "subdirectory_arrow_right"}</Icon>
                  <Box sx={{ fontWeight: category.level === 1 ? 700 : 500, color: c.text, overflowWrap: "anywhere" }}>{category.name}</Box>
                  <Box sx={{ color: c.muted, fontFamily: "monospace", fontSize: "0.75rem" }}>#{category.id}</Box>
                  {!category.isActive && <StatusPill tone="neutral" label="Disabled" />}
                </Box>
                <Box sx={{ flexShrink: 0, color: c.muted, fontSize: "0.8125rem" }}>
                  {category.productCount === 1 ? "1 product" : `${category.productCount.toLocaleString()} products`}
                </Box>
              </Box>
            ))}
          </Section>
        </Box>
      )}

      <KitDialog
        open={!!plan}
        onClose={() => setPlan(null)}
        icon="visibility"
        maxWidth="md"
        title="What would be created"
        subtitle={plan ? `${plan.created} categor${plan.created === 1 ? "y" : "ies"} to create in the store, ${plan.mapped} to map. Nothing has been changed yet.` : undefined}
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setPlan(null)}>
              Close
            </MDButton>
            <MDButton variant="gradient" color="info" disabled={!!busy || !canWrite || !plan || plan.items.length === 0} onClick={createMissing}>
              {busy === "create-missing" ? "Creating…" : "Create and map"}
            </MDButton>
          </>
        }
      >
        {plan && (
          <Box>
            {plan.items.length === 0 && <Box sx={hint}>Every category of yours is already mapped.</Box>}
            {plan.items.map((item) => (
              <Box key={item.category} sx={{ display: "flex", flexWrap: "wrap", justifyContent: "space-between", gap: 1.5, py: 1, borderTop: `1px solid ${c.border}`, fontSize: "0.8125rem" }}>
                <Box sx={{ fontWeight: 600, color: c.text, overflowWrap: "anywhere" }}>{item.category}</Box>
                <Box sx={{ color: c.muted }}>{item.created === 0 ? "already in the store; only mapped" : `${item.created} new store categor${item.created === 1 ? "y" : "ies"}`}</Box>
              </Box>
            ))}
            {!canWrite && plan.items.length > 0 && (
              <InlineAlert tone="warning" sx={{ mt: 2 }}>
                Live writes are off for Magento, so this can be previewed but not carried out.
              </InlineAlert>
            )}
          </Box>
        )}
      </KitDialog>

      <KitDialog
        open={creating}
        onClose={() => setCreating(false)}
        icon="create_new_folder"
        title="New store category"
        subtitle="Created in the Magento store at once."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setCreating(false)}>
              Cancel
            </MDButton>
            <MDButton variant="gradient" color="info" disabled={!!busy || !newName.trim()} onClick={createOne}>
              {busy === "create-one" ? "Creating…" : "Create"}
            </MDButton>
          </>
        }
      >
        <Box sx={{ display: "grid", gap: 2.5 }}>
          <MDInput label="Name" fullWidth value={newName} onChange={(e: Change) => setNewName(e.target.value)} />
          <MDInput select label="Inside" fullWidth SelectProps={{ native: true }} InputLabelProps={{ shrink: true }} value={newParent} onChange={(e: Change) => setNewParent(e.target.value)}>
            <option value="">The store's top level</option>
            {store.filter((s) => s.level >= 2).map((category) => (
              <option key={category.id} value={category.id}>
                {category.path}
              </option>
            ))}
          </MDInput>
          {creationOptions}
        </Box>
      </KitDialog>
    </PageShell>
  );
}
