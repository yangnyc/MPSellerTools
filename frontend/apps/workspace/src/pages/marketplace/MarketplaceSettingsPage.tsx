import { useCallback, useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, StateBlock, StatusPill, formatDateTime, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import {
  ChannelsApi,
  SyncApi,
  type CategoryMapping,
  type ChannelAccount,
  type ChannelAccountUpdate,
  type Marketplace,
  type PriceConflictPolicy,
} from "../../api/channels";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

type Change = React.ChangeEvent<HTMLInputElement>;
type Form = Omit<ChannelAccountUpdate, "channel" | "sellerId"> & { sellerId: string };

const toForm = (account: ChannelAccount): Form => ({
  name: account.name,
  environment: account.environment,
  sellerId: account.sellerId ?? "",
  settings: Object.fromEntries(Object.entries(account.settings ?? {}).map(([key, value]) => [key, String(value)])),
  isEnabled: account.isEnabled,
  liveWritesEnabled: account.liveWritesEnabled,
  inventorySyncEnabled: account.inventorySyncEnabled,
  orderImportEnabled: account.orderImportEnabled,
  priceConflictPolicy: account.priceConflictPolicy,
});

// A marketplace account's own settings: where it points, what it may do, and the keys it does it with.
export default function MarketplaceSettingsPage({ marketplace }: { marketplace: Marketplace }) {
  const { name, path } = marketplace;
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [account, setAccount] = useState<ChannelAccount | null>(null);
  const [form, setForm] = useState<Form | null>(null);
  const [credentials, setCredentials] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [mappings, setMappings] = useState<CategoryMapping[]>([]);
  const [internalCategory, setInternalCategory] = useState("");
  const [externalCategory, setExternalCategory] = useState("");

  const show = useCallback((found: ChannelAccount | null) => {
    setAccount(found);
    setForm(found ? toForm(found) : null);
  }, []);

  const load = useCallback(
    () =>
      Promise.all([ChannelsApi.list(), ChannelsApi.categoryMappings()])
        .then(([accounts, allMappings]) => {
          const found = accounts.find((a) => a.channel === marketplace.kind) ?? null;
          show(found);
          setMappings(allMappings.filter((m) => found?.markets.some((market) => market.id === m.channelMarketId)));
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, `Failed to load ${name}.`)))
        .finally(() => setLoading(false)),
    [marketplace.kind, name, show]
  );

  useEffect(() => {
    load();
  }, [load]);

  const run = async (key: string, action: () => Promise<void>, fallback: string) => {
    setBusy(key);
    try {
      await action();
    } catch (err) {
      notify(message(err, fallback), "error");
    } finally {
      setBusy(null);
    }
  };

  const save = () =>
    run("save", async () => {
      if (!account || !form) return;
      // Settings left empty are not saved at all, so the marketplace's defaults apply.
      const settings = Object.fromEntries(Object.entries(form.settings).map(([k, v]) => [k, v.trim()]).filter(([, v]) => v));
      show(await ChannelsApi.update(account.id, { ...form, channel: marketplace.kind, sellerId: form.sellerId.trim() || null, settings }));
      notify(`${name} settings saved.`, "success");
    }, "Could not save the settings.");

  const saveCredentials = () =>
    run("credentials", async () => {
      if (!account) return;
      await ChannelsApi.setCredentials(account.id, credentials);
      setCredentials({});
      await load();
      notify(`${name} credentials saved.`, "success");
    }, "Could not save the credentials.");

  const saveMapping = () =>
    run("mapping", async () => {
      if (!account?.markets[0]) return;
      await ChannelsApi.saveCategoryMapping(account.markets[0].id, internalCategory.trim(), externalCategory.trim());
      setInternalCategory("");
      setExternalCategory("");
      await load();
      notify("Category saved.", "success");
    }, "Could not save the category.");

  const importOrders = () =>
    run("import", async () => {
      if (!account) return;
      await SyncApi.importOrders(account.id);
      notify("An order import was queued. Its result shows in the sync queue.", "success");
    }, "Could not queue an order import.");

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((current) => (current ? { ...current, [key]: value } : current));
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const toggle = (key: "isEnabled" | "liveWritesEnabled" | "orderImportEnabled" | "inventorySyncEnabled", label: string, help: string) => (
    <Box>
      <FormControlLabel
        control={<Switch checked={form![key]} onChange={(e: Change) => set(key, e.target.checked)} />}
        label={<Box component="span" sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{label}</Box>}
      />
      <Box sx={{ ...hint, pl: 6 }}>{help}</Box>
    </Box>
  );

  const credentialsComplete = marketplace.credentials.every(({ key }) => (credentials[key] ?? "").trim().length > 0);

  return (
    <PageShell>
      <PageHeader
        icon="tune"
        title={`${name} settings`}
        subtitle={`How this workspace talks to ${name}, and what it is allowed to do there.`}
        actions={
          <MDButton component={RouterLink} to={path} variant="outlined" color="info" size="small">
            Back to {name}
          </MDButton>
        }
      />

      {loading && <StateBlock kind="loading" title={`Loading ${name}`} />}
      {loadError && <StateBlock kind="error" title={`${name} could not be loaded`} message={loadError} />}

      {!loading && !loadError && !account && (
        <Section flush>
          <StateBlock
            icon="link"
            title={`${name} is not set up yet`}
            message={`Add ${name} as a sales channel first, then come back to configure it.`}
            action={
              <MDButton component={RouterLink} to={path} variant="gradient" color="info" size="small">
                Set up {name}
              </MDButton>
            }
          />
        </Section>
      )}

      {!loading && !loadError && account && form && (
        <Box sx={{ display: "grid", gap: 3 }}>
          {account.liveWritesEnabled && !account.effectiveLiveWrites && (
            <InlineAlert tone="warning" title="Still a dry run">
              Live writes are on for this account but off for the whole workspace (the host setting Marketplace:LiveWritesEnabled), so nothing
              is sent to {name} yet.
            </InlineAlert>
          )}
          {account.lastError && (
            <InlineAlert tone="error" title={`${name} refused this account`}>
              {account.lastError}
            </InlineAlert>
          )}

          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
            <Section
              icon="tune"
              title="Account"
              subtitle="Saved together with the switches below."
              actions={<StatusPill tone={account.effectiveLiveWrites ? "success" : "warning"} label={account.effectiveLiveWrites ? "Live writes on" : "Dry run only"} />}
            >
              <Box
                component="form"
                noValidate
                onSubmit={(e: React.FormEvent) => {
                  e.preventDefault();
                  save();
                }}
                sx={{ display: "grid", gap: 2.5 }}
              >
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2.5 }}>
                  <MDInput
                    select
                    label="Environment"
                    fullWidth
                    SelectProps={{ native: true }}
                    InputLabelProps={{ shrink: true }}
                    value={form.environment}
                    onChange={(e: Change) => set("environment", Number(e.target.value) as 0 | 1)}
                  >
                    <option value={0}>Sandbox</option>
                    <option value={1}>Production</option>
                  </MDInput>
                  {marketplace.asksSellerId && (
                    <MDInput label="Seller ID" fullWidth value={form.sellerId} onChange={(e: Change) => set("sellerId", e.target.value)} />
                  )}
                  {marketplace.settings.map((setting) => (
                    <MDInput
                      key={setting.key}
                      label={setting.label}
                      fullWidth
                      value={form.settings[setting.key] ?? ""}
                      onChange={(e: Change) => set("settings", { ...form.settings, [setting.key]: e.target.value })}
                      helperText={setting.help}
                    />
                  ))}
                </Box>

                <Box sx={{ display: "grid", gap: 2, pt: 1, borderTop: `1px solid ${c.border}` }}>
                  {toggle("isEnabled", "Account enabled", `Switched off, nothing is sent to ${name} or read from it.`)}
                  {toggle("liveWritesEnabled", "Live writes", `Switched off, everything is prepared and checked as a dry run and nothing on ${name} changes.`)}
                  {toggle("orderImportEnabled", "Import orders", `Reads new orders from ${name} on a schedule.`)}
                  {toggle("inventorySyncEnabled", "Send stock", `Sends quantities to ${name} when stock changes. Needs stock accounting, and order import on every active channel.`)}
                </Box>

                <MDInput
                  select
                  label="When the price on the marketplace differs"
                  fullWidth
                  SelectProps={{ native: true }}
                  InputLabelProps={{ shrink: true }}
                  value={form.priceConflictPolicy}
                  onChange={(e: Change) => set("priceConflictPolicy", Number(e.target.value) as PriceConflictPolicy)}
                >
                  <option value={2}>Change nothing and flag the listing</option>
                  <option value={0}>Send the price from here again</option>
                  <option value={1}>Take the marketplace's price</option>
                </MDInput>

                <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
                  <MDButton variant="text" color="secondary" disabled={!!busy} onClick={() => setForm(toForm(account))}>
                    Reset
                  </MDButton>
                  <MDButton type="submit" variant="gradient" color="info" disabled={!!busy}>
                    {busy === "save" ? "Saving…" : "Save"}
                  </MDButton>
                </Box>
              </Box>
            </Section>

            <Box sx={{ display: "grid", gap: 3 }}>
              <Section
                icon="key"
                tone="warning"
                title="Credentials"
                subtitle="Stored encrypted. They are never shown again."
                actions={<StatusPill tone={account.hasCredentials ? "success" : "neutral"} label={account.hasCredentials ? "Saved" : "Not saved"} />}
              >
                {marketplace.credentials.length === 0 ? (
                  <Box sx={hint}>{marketplace.credentialsNote}</Box>
                ) : (
                  <Box
                    component="form"
                    noValidate
                    onSubmit={(e: React.FormEvent) => {
                      e.preventDefault();
                      if (credentialsComplete) saveCredentials();
                    }}
                    sx={{ display: "grid", gap: 2.5 }}
                  >
                    {marketplace.credentials.map(({ key, label }) => (
                      <MDInput
                        key={key}
                        label={label}
                        type="password"
                        fullWidth
                        autoComplete="off"
                        value={credentials[key] ?? ""}
                        onChange={(e: Change) => setCredentials({ ...credentials, [key]: e.target.value })}
                      />
                    ))}
                    <Box sx={hint}>{account.hasCredentials ? "Saving replaces all of the credentials already stored." : `Needed before anything can be sent to ${name}.`}</Box>
                    <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                      <MDButton type="submit" variant="gradient" color="info" disabled={!credentialsComplete || !!busy}>
                        {busy === "credentials" ? "Saving…" : "Save credentials"}
                      </MDButton>
                    </Box>
                  </Box>
                )}
              </Section>

              <Section icon="category" title="Categories" subtitle={`Where each of your own categories goes on ${name}. A product needs this before it can be published.`}>
                <Box sx={{ display: "grid", gap: 2 }}>
                  {mappings.length === 0 && <Box sx={hint}>No categories mapped yet.</Box>}
                  {mappings.map((mapping) => (
                    <Box key={mapping.id} sx={{ display: "flex", justifyContent: "space-between", gap: 2, fontSize: "0.875rem" }}>
                      <Box sx={{ fontWeight: 500, color: c.text }}>{mapping.internalCategory}</Box>
                      <Box sx={{ color: c.muted, overflowWrap: "anywhere", textAlign: "right" }}>{mapping.externalCategoryId}</Box>
                    </Box>
                  ))}
                  <Box
                    component="form"
                    noValidate
                    onSubmit={(e: React.FormEvent) => {
                      e.preventDefault();
                      if (internalCategory.trim() && externalCategory.trim()) saveMapping();
                    }}
                    sx={{ display: "grid", gap: 2, pt: 2, borderTop: `1px solid ${c.border}` }}
                  >
                    <MDInput label="Your category" fullWidth value={internalCategory} onChange={(e: Change) => setInternalCategory(e.target.value)} helperText="As written on the product. Saving one already listed replaces it." />
                    <MDInput label={marketplace.categoryLabel} fullWidth value={externalCategory} onChange={(e: Change) => setExternalCategory(e.target.value)} />
                    <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                      <MDButton type="submit" variant="outlined" color="info" disabled={!internalCategory.trim() || !externalCategory.trim() || !!busy}>
                        {busy === "mapping" ? "Saving…" : "Save category"}
                      </MDButton>
                    </Box>
                  </Box>
                </Box>
              </Section>

              <Section icon="receipt_long" title="Orders" subtitle={account.lastOrderImportAtUtc ? `Last read ${formatDateTime(account.lastOrderImportAtUtc)}` : "Never read yet"}>
                <Box sx={{ display: "grid", gap: 2 }}>
                  <Box sx={hint}>Reads orders now instead of waiting for the next scheduled import.</Box>
                  <Box>
                    <MDButton variant="outlined" color="info" disabled={!!busy || !account.isEnabled} onClick={importOrders}>
                      {busy === "import" ? "Queuing…" : "Import orders now"}
                    </MDButton>
                  </Box>
                </Box>
              </Section>
            </Box>
          </Box>
        </Box>
      )}
    </PageShell>
  );
}
