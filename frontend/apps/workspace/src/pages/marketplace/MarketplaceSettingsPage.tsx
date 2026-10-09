import { useCallback, useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import Link from "@mui/material/Link";
import FormControlLabel from "@mui/material/FormControlLabel";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, StateBlock, StatusPill, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import {
  ChannelsApi,
  MAGENTO,
  MagentoApi,
  type CategoryMapping,
  type ChannelAccount,
  type ChannelAccountUpdate,
  type Marketplace,
  type PriceConflictPolicy,
} from "../../api/channels";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

type Change = React.ChangeEvent<HTMLInputElement>;
type Form = Omit<ChannelAccountUpdate, "channel" | "sellerId"> & { sellerId: string };

// What to do when the marketplace's price for a listing is no longer the one in this workspace: someone
// changed it there, or a repricing tool did. Each choice with what it means for both prices.
const PRICE_CHOICES: { value: PriceConflictPolicy; label: (name: string) => string; means: (name: string) => string }[] = [
  {
    value: 2,
    label: () => "Ask me: leave both prices as they are (safest)",
    means: (name) => `Nothing is changed anywhere. The listing is flagged in the sync queue so you can decide which price is right. Until you do, ${name} keeps selling at its own price.`,
  },
  {
    value: 0,
    label: (name) => `My price wins: put it back on ${name}`,
    means: (name) => `The price in this workspace is sent to ${name} again, overwriting whatever was set there. Choose this when prices are only ever meant to be changed here.`,
  },
  {
    value: 1,
    label: (name) => `${name}'s price wins: copy it here`,
    means: (name) => `The product's price in this workspace is changed to match ${name}. Choose this when you set prices on ${name} itself, or a repricing tool does. It changes the price your other sales channels use too.`,
  },
];

// How the credentials box stands: closed, or open to add them, change some, or replace them all.
type CredentialsMode = "closed" | "edit" | "replace";

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
  const [credentialsMode, setCredentialsMode] = useState<CredentialsMode>("closed");
  const [removing, setRemoving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [mappings, setMappings] = useState<CategoryMapping[]>([]);
  const [internalCategory, setInternalCategory] = useState("");
  const [externalCategory, setExternalCategory] = useState("");
  // The saved settings being changed. One that is saved and left alone shows as its value (a link, when it
  // is an address) with a button to change it, rather than as a field that is always open to a stray edit.
  const [editingLinks, setEditingLinks] = useState<string[]>([]);

  const show = useCallback((found: ChannelAccount | null) => {
    setAccount(found);
    setForm(found ? toForm(found) : null);
    setEditingLinks([]);
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
      notify(`${name} ${(marketplace.settingsName ?? "Settings").toLowerCase()} saved.`, "success");
    }, "Could not save the settings.");

  const saveCredentials = () =>
    run("credentials", async () => {
      if (!account) return;
      if (account.hasCredentials && credentialsMode === "edit") {
        // Only the ones typed in are changed; the rest stay as saved.
        await ChannelsApi.editCredentials(account.id, Object.fromEntries(Object.entries(credentials).filter(([, value]) => value.trim().length > 0)));
      } else {
        await ChannelsApi.setCredentials(account.id, credentials);
      }
      setCredentials({});
      setCredentialsMode("closed");
      await load();
      notify(`${name} credentials saved.`, "success");
    }, "Could not save the credentials.");

  const removeCredentials = () =>
    run("credentials", async () => {
      if (!account) return;
      setRemoving(false);
      await ChannelsApi.removeCredentials(account.id);
      setCredentials({});
      setCredentialsMode("closed");
      await load();
      notify(`${name} credentials removed. Nothing is sent to ${name} or read from it until new ones are added.`, "success");
    }, "Could not remove the credentials.");

  const openCredentials = (mode: CredentialsMode) => {
    setCredentials({});
    setCredentialsMode(mode);
  };

  const saveMapping = () =>
    run("mapping", async () => {
      if (!account?.markets[0]) return;
      await ChannelsApi.saveCategoryMapping(account.markets[0].id, internalCategory.trim(), externalCategory.trim());
      setInternalCategory("");
      setExternalCategory("");
      await load();
      notify("Category saved.", "success");
    }, "Could not save the category.");

  // Tries the address and token as saved, not as typed: both are saved first.
  const testConnection = () =>
    run("test", async () => {
      const store = await MagentoApi.test();
      const views = store.storeViews.length > 0 ? ` Store views: ${store.storeViews.join(", ")}.` : "";
      notify(`Connected to ${store.storeAddress ?? name}.${views}`, "success");
    }, `Could not reach ${name}.`);

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

  // Nothing saved yet: the fields are there to add them. Saved: there are none until one of the buttons opens them.
  const credentialsOpen = !account?.hasCredentials || credentialsMode !== "closed";
  const editingSome = !!account?.hasCredentials && credentialsMode === "edit";
  const filled = marketplace.credentials.filter(({ key }) => (credentials[key] ?? "").trim().length > 0).length;
  // Changing some needs at least one; adding or replacing needs them all.
  const credentialsComplete = editingSome ? filled > 0 : filled === marketplace.credentials.length;
  // Magento's categories have a page of their own in its menu, so they are not repeated here.
  const ownCategoriesPage = marketplace.kind === MAGENTO.kind;
  const priceChoice = PRICE_CHOICES.find((choice) => choice.value === form?.priceConflictPolicy) ?? PRICE_CHOICES[0];

  return (
    <PageShell>
      <PageHeader
        icon={marketplace.settingsName ? "link" : "tune"}
        title={`${name} ${(marketplace.settingsName ?? "Settings").toLowerCase()}`}
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
              Live writes are on for this account, but the server's operator has switched them off for every account (the host setting
              Marketplace:LiveWritesEnabled), so nothing is sent to {name} until that is turned back on.
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
                  {!marketplace.singleEnvironment && (
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
                  )}
                  {marketplace.asksSellerId && (
                    <MDInput label="Seller ID" fullWidth value={form.sellerId} onChange={(e: Change) => set("sellerId", e.target.value)} />
                  )}
                  {marketplace.settings.map((setting) => {
                    const saved = String(account.settings?.[setting.key] ?? "");
                    // A saved address is something to open; any saved value becomes a field again only when asked to.
                    const address = setting.link || /^https:\/\//i.test(saved);
                    return saved && !editingLinks.includes(setting.key) ? (
                      <Box key={setting.key} sx={{ minWidth: 0 }}>
                        <Box sx={{ mb: 0.5, fontSize: "0.75rem", color: c.muted }}>{setting.label}</Box>
                        <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 1 }}>
                          {address ? (
                            <Link
                              href={saved}
                              target="_blank"
                              rel="noreferrer"
                              aria-label={`Open ${setting.label.toLowerCase()} ${saved}`}
                              sx={{ display: "inline-flex", alignItems: "center", gap: 0.5, minWidth: 0, fontSize: "0.9375rem", color: c.accent, overflowWrap: "anywhere" }}
                            >
                              {saved}
                              <Icon sx={{ flexShrink: 0, fontSize: "1rem !important" }}>open_in_new</Icon>
                            </Link>
                          ) : (
                            <Box sx={{ minWidth: 0, fontSize: "0.9375rem", color: c.text, overflowWrap: "anywhere" }}>{saved}</Box>
                          )}
                          <MDButton
                            variant="outlined"
                            color="info"
                            size="small"
                            onClick={() => setEditingLinks([...editingLinks, setting.key])}
                            aria-label={`Edit ${setting.label.toLowerCase()}`}
                            startIcon={<Icon>edit</Icon>}
                            sx={{ flexShrink: 0 }}
                          >
                            Edit
                          </MDButton>
                        </Box>
                      </Box>
                    ) : (
                      <MDInput
                        key={setting.key}
                        label={setting.label}
                        fullWidth
                        value={form.settings[setting.key] ?? ""}
                        onChange={(e: Change) => set("settings", { ...form.settings, [setting.key]: e.target.value })}
                        helperText={setting.help}
                      />
                    );
                  })}
                </Box>

                <Box sx={{ display: "grid", gap: 2, pt: 1, borderTop: `1px solid ${c.border}` }}>
                  {toggle("isEnabled", "Account enabled", `Switched off, nothing is sent to ${name} or read from it.`)}
                  {toggle("liveWritesEnabled", "Live writes", `Switched off, everything is prepared and checked as a dry run and nothing on ${name} changes.`)}
                  {toggle("orderImportEnabled", "Import orders", `Reads new orders from ${name} on a schedule.`)}
                  {toggle("inventorySyncEnabled", "Send stock", `Sends quantities to ${name} when stock changes. Needs stock accounting, and order import on every active channel.`)}
                </Box>

                <Box sx={{ display: "grid", gap: 1, pt: 2, borderTop: `1px solid ${c.border}` }}>
                  <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>If a price is changed on {name} instead of here</Box>
                  <Box sx={hint}>
                    Sometimes the price of a listing on {name} stops matching the price in this workspace: someone edits it there, or a repricing tool does. Choose what
                    should happen when that is noticed.
                  </Box>
                  <MDInput
                    select
                    label={`When ${name}'s price and mine differ`}
                    fullWidth
                    SelectProps={{ native: true }}
                    InputLabelProps={{ shrink: true }}
                    value={form.priceConflictPolicy}
                    onChange={(e: Change) => set("priceConflictPolicy", Number(e.target.value) as PriceConflictPolicy)}
                    sx={{ mt: 1 }}
                  >
                    {PRICE_CHOICES.map((choice) => (
                      <option key={choice.value} value={choice.value}>
                        {choice.label(name)}
                      </option>
                    ))}
                  </MDInput>
                  <Box sx={hint} data-testid="price-choice-means">
                    {priceChoice.means(name)}
                  </Box>
                </Box>

                <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
                  <MDButton variant="text" color="secondary" disabled={!!busy} onClick={() => show(account)}>
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
                ) : !credentialsOpen ? (
                  <Box sx={{ display: "grid", gap: 2 }}>
                    {marketplace.credentials.map(({ key, label }) => (
                      <Box key={key} sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2, fontSize: "0.875rem" }}>
                        <Box sx={{ fontWeight: 500, color: c.text }}>{label}</Box>
                        <Box sx={{ color: c.muted, letterSpacing: "0.12em" }} aria-label="Saved, hidden">
                          ••••••••
                        </Box>
                      </Box>
                    ))}
                    <Box sx={hint}>
                      Saved and in use. The values are hidden for good, so they cannot be read back here: to change one, type the new value in.
                    </Box>
                    <Box sx={{ display: "flex", flexWrap: "wrap", justifyContent: "flex-end", gap: 1 }}>
                      {marketplace.connectionTest && (
                        <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={testConnection}>
                          {busy === "test" ? "Testing…" : "Test connection"}
                        </MDButton>
                      )}
                      {marketplace.credentials.length > 1 && (
                        <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => openCredentials("edit")} startIcon={<Icon>edit</Icon>}>
                          Edit
                        </MDButton>
                      )}
                      <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => openCredentials("replace")} startIcon={<Icon>swap_horiz</Icon>}>
                        Replace
                      </MDButton>
                      <MDButton variant="outlined" color="error" size="small" disabled={!!busy} onClick={() => setRemoving(true)} startIcon={<Icon>delete</Icon>}>
                        Remove
                      </MDButton>
                    </Box>
                  </Box>
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
                    {account.hasCredentials && (
                      <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{editingSome ? "Edit credentials" : "Replace credentials"}</Box>
                    )}
                    {marketplace.credentials.map(({ key, label }) => (
                      <MDInput
                        key={key}
                        label={label}
                        type="password"
                        fullWidth
                        autoComplete="off"
                        value={credentials[key] ?? ""}
                        onChange={(e: Change) => setCredentials({ ...credentials, [key]: e.target.value })}
                        helperText={editingSome ? "Leave empty to keep the one saved." : undefined}
                      />
                    ))}
                    {marketplace.credentialsHelp && <Box sx={hint}>{marketplace.credentialsHelp}</Box>}
                    <Box sx={hint}>
                      {!account.hasCredentials
                        ? `Needed before anything can be sent to ${name}.`
                        : editingSome
                          ? "Only the ones you fill in are changed; the rest stay as saved."
                          : "All of them are needed: saving replaces every credential already stored."}
                    </Box>
                    <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
                      {account.hasCredentials && (
                        <MDButton variant="text" color="secondary" disabled={!!busy} onClick={() => openCredentials("closed")}>
                          Cancel
                        </MDButton>
                      )}
                      <MDButton type="submit" variant="gradient" color="info" disabled={!credentialsComplete || !!busy} startIcon={account.hasCredentials ? undefined : <Icon>add</Icon>}>
                        {busy === "credentials" ? "Saving…" : account.hasCredentials ? "Save credentials" : "Add credentials"}
                      </MDButton>
                    </Box>
                  </Box>
                )}
              </Section>

              {!ownCategoriesPage && (
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
              )}
            </Box>
          </Box>
        </Box>
      )}
      <ConfirmDialog
        open={removing}
        title={`Remove the ${name} credentials?`}
        message={`Nothing can be sent to ${name} or read from it until new ones are added. Your listings and settings stay as they are, and nothing changes on ${name} itself.`}
        confirmLabel="Remove"
        onConfirm={removeCredentials}
        onCancel={() => setRemoving(false)}
      />
    </PageShell>
  );
}
