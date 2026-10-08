import { useCallback, useEffect, useRef, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, StateBlock, StatusPill, timeAgo, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import ConfirmDialog from "../components/ConfirmDialog";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { EbayApi } from "../api/resources";
import type { EbayEnvironment, EbayStatus } from "../api/types";

const ENVIRONMENTS: { value: EbayEnvironment; label: string }[] = [
  { value: 0, label: "Sandbox (eBay's test site)" },
  { value: 1, label: "Production (live eBay)" },
];

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

export default function EbayPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [status, setStatus] = useState<EbayStatus | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);

  const [environment, setEnvironment] = useState<EbayEnvironment>(0);
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [ruName, setRuName] = useState("");
  const [pasted, setPasted] = useState("");

  const show = useCallback((next: EbayStatus) => {
    setStatus(next);
    setEnvironment(next.environment);
    setClientId(next.clientId ?? "");
    setRuName(next.ruName ?? "");
    setClientSecret("");
  }, []);

  // eBay sends the seller back to this page with the code in the address.
  // It is handed to the server once, then taken out of the address bar.
  const returned = useRef(false);
  useEffect(() => {
    const query = new URLSearchParams(window.location.search);
    const cameBack = query.has("code") || query.has("error");
    if (cameBack && !returned.current) {
      returned.current = true;
      const address = window.location.href;
      window.history.replaceState({}, "", window.location.pathname);
      if (query.has("code")) {
        EbayApi.complete(address)
          .then((next) => {
            show(next);
            notify("eBay account connected.", "success");
          })
          .catch((err) => {
            notify(message(err, "Could not finish connecting to eBay."), "error");
            EbayApi.get().then(show).catch(() => undefined);
          });
        return;
      }
      notify("eBay did not grant access.", "error");
    }
    EbayApi.get()
      .then(show)
      .catch((err) => setLoadError(message(err, "Failed to load the eBay connection.")));
  }, [show, notify]);

  const run = async (name: string, action: () => Promise<void>, fallback: string) => {
    setBusy(name);
    try {
      await action();
    } catch (err) {
      notify(message(err, fallback), "error");
      // A failed import leaves its reason on the connection.
      EbayApi.get().then(setStatus).catch(() => undefined);
    } finally {
      setBusy(null);
    }
  };

  const saveKeys = () => {
    if (!clientId.trim() || !ruName.trim() || (!status?.configured && !clientSecret.trim())) {
      notify("App ID, Cert ID and RuName are required.", "error");
      return;
    }
    run("save", async () => {
      show(await EbayApi.saveSettings({ environment, clientId: clientId.trim(), clientSecret: clientSecret.trim(), ruName: ruName.trim() }));
      notify("eBay application keys saved.", "success");
    }, "Could not save the keys.");
  };

  const connect = () =>
    run("connect", async () => {
      const { authorizeUrl } = await EbayApi.connect();
      window.location.assign(authorizeUrl);
    }, "Could not start the connection.");

  const finishByHand = () =>
    run("finish", async () => {
      show(await EbayApi.complete(pasted.trim()));
      setPasted("");
      notify("eBay account connected.", "success");
    }, "Could not finish connecting to eBay.");

  const disconnect = () => {
    setConfirmDisconnect(false);
    run("disconnect", async () => {
      show(await EbayApi.disconnect());
      notify("eBay account disconnected.", "success");
    }, "Could not disconnect.");
  };

  const importOrders = () =>
    run("orders", async () => {
      const result = await EbayApi.importOrders();
      setStatus(await EbayApi.get());
      notify(
        `Orders imported: ${result.created} new, ${result.updated} updated` +
          (result.productsCreated ? `, ${result.productsCreated} products added.` : "."),
        "success"
      );
    }, "Order import failed.");

  const importProducts = () =>
    run("products", async () => {
      const result = await EbayApi.importProducts();
      setStatus(await EbayApi.get());
      const summary = `Products imported: ${result.created} new, ${result.updated} updated, ${result.listings} posted on eBay.`;
      if (result.warning) notify(`${summary} ${result.warning}`, "warning");
      else notify(summary, "success");
    }, "Product import failed.");

  const keysChanged =
    !!status &&
    (environment !== status.environment || clientId.trim() !== (status.clientId ?? "") || ruName.trim() !== (status.ruName ?? "") || !!clientSecret);
  const returnAddress = status ? `${window.location.origin}${status.callbackPath}` : "";
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };

  return (
    <PageShell>
      <PageHeader
        icon="storefront"
        title="eBay"
        subtitle="Link your company's eBay seller account, then bring its orders and products in here."
      />

      {!status && !loadError && <StateBlock kind="loading" title="Loading the eBay connection" />}
      {loadError && <StateBlock kind="error" title="The eBay connection could not be loaded" message={loadError} />}

      {status && (
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, alignItems: "start", gap: 3 }}>
          <Box sx={{ display: "grid", gap: 3 }}>
            <Section icon="link" title="Account" subtitle="Access is read-only: nothing here changes your listings or orders on eBay.">
              <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1.5, mb: 2 }}>
                <StatusPill
                  tone={status.connected ? "success" : status.configured ? "warning" : "neutral"}
                  label={status.connected ? "Connected" : status.configured ? "Not connected" : "Not set up"}
                />
                {status.configured && <StatusPill tone={status.environment === 1 ? "info" : "neutral"} label={status.environment === 1 ? "Production" : "Sandbox"} />}
                {status.connected && status.connectedAtUtc && (
                  <Box component="span" sx={hint}>
                    since {timeAgo(status.connectedAtUtc)}
                  </Box>
                )}
              </Box>

              {!status.configured && <Box sx={hint}>Save your eBay application keys first, then connect the account.</Box>}

              {status.configured && !status.connected && (
                <>
                  <Box sx={{ ...hint, mb: 2 }}>
                    Connect opens eBay, where you sign in as the seller and allow access. eBay then brings you back to this page.
                  </Box>
                  <MDButton variant="gradient" color="info" disabled={!!busy || keysChanged} onClick={connect} startIcon={<Icon>login</Icon>}>
                    Connect eBay account
                  </MDButton>
                  <Box sx={{ mt: 3, pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                    <Box sx={{ ...hint, mb: 1.5 }}>
                      If eBay did not bring you back here, copy the address of the page it left you on and paste it below.
                    </Box>
                    <Box sx={{ display: "flex", gap: 1, alignItems: "flex-start" }}>
                      <MDInput
                        label="Address eBay sent you to"
                        fullWidth
                        size="small"
                        value={pasted}
                        onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPasted(e.target.value)}
                      />
                      <MDButton variant="outlined" color="info" disabled={!!busy || !pasted.trim()} onClick={finishByHand}>
                        Finish
                      </MDButton>
                    </Box>
                  </Box>
                </>
              )}

              {status.connected && (
                <MDButton variant="outlined" color="error" disabled={!!busy} onClick={() => setConfirmDisconnect(true)}>
                  Disconnect
                </MDButton>
              )}
            </Section>

            <Section icon="sync" title="Import" subtitle="Run these whenever you want this workspace brought up to date.">
              {status.lastSyncError && (
                <InlineAlert tone="error" title="The last import failed" sx={{ mb: 2.5 }}>
                  {status.lastSyncError}
                </InlineAlert>
              )}
              <Box sx={{ display: "grid", gap: 2.5 }}>
                <Box>
                  <MDButton variant="gradient" color="info" disabled={!status.connected || !!busy} onClick={importOrders} startIcon={<Icon>receipt_long</Icon>}>
                    {busy === "orders" ? "Importing…" : "Import orders"}
                  </MDButton>
                  <Box sx={{ ...hint, mt: 1 }}>
                    New eBay orders appear under Orders as EBAY-…; ones imported before only have their status updated. The first import
                    reaches back 90 days. {status.importedOrders} imported so far
                    {status.lastOrderSyncAtUtc ? `, last ${timeAgo(status.lastOrderSyncAtUtc)}.` : "."}
                  </Box>
                </Box>
                <Box>
                  <MDButton variant="gradient" color="info" disabled={!status.connected || !!busy} onClick={importProducts} startIcon={<Icon>inventory_2</Icon>}>
                    {busy === "products" ? "Importing…" : "Import products"}
                  </MDButton>
                  <Box sx={{ ...hint, mt: 1 }}>
                    Matches products by SKU and takes eBay's name, quantity and price. Everything on sale on the account then shows
                    under Listings, including listings made by hand on the eBay site; one of those with a SKU not in the catalog gets a
                    product made for it.
                    {status.lastProductSyncAtUtc ? ` Last ${timeAgo(status.lastProductSyncAtUtc)}.` : ""}
                  </Box>
                </Box>
              </Box>
            </Section>
          </Box>

          <Section icon="key" title="Application keys" subtitle="From your eBay developer account, under Application Keys.">
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                saveKeys();
              }}
              sx={{ display: "grid", gap: 2.5 }}
            >
              <MDInput
                select
                label="Environment"
                fullWidth
                SelectProps={{ native: true }}
                value={environment}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEnvironment(Number(e.target.value) as EbayEnvironment)}
              >
                {ENVIRONMENTS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </MDInput>
              <MDInput label="App ID (Client ID)" fullWidth value={clientId} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setClientId(e.target.value)} />
              <MDInput
                label="Cert ID (Client Secret)"
                type="password"
                fullWidth
                autoComplete="new-password"
                value={clientSecret}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setClientSecret(e.target.value)}
                helperText={status.configured ? "Saved. Leave empty to keep it; it is never shown again." : "Stored encrypted and never shown again."}
              />
              <MDInput label="RuName (eBay Redirect URL name)" fullWidth value={ruName} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setRuName(e.target.value)} />
              <Box sx={hint}>
                In the RuName's settings on eBay, set "Your auth accepted URL" to:
                <Box
                  sx={{
                    mt: 1,
                    p: 1.25,
                    borderRadius: "10px",
                    fontFamily: "monospace",
                    wordBreak: "break-all",
                    color: c.text,
                    backgroundColor: c.surfaceAlt,
                    border: `1px solid ${c.border}`,
                  }}
                >
                  {returnAddress}
                </Box>
              </Box>
              {status.connected && keysChanged && (
                <InlineAlert tone="warning">Saving changed keys disconnects the account; you will need to connect it again.</InlineAlert>
              )}
              <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                <MDButton type="submit" variant="gradient" color="info" disabled={!!busy || (status.configured && !keysChanged)}>
                  {busy === "save" ? "Saving…" : "Save keys"}
                </MDButton>
              </Box>
            </Box>
          </Section>
        </Box>
      )}

      <ConfirmDialog
        open={confirmDisconnect}
        title="Disconnect eBay"
        message="This workspace forgets its access to the eBay account. Orders and products already imported stay. You can connect again at any time."
        confirmLabel="Disconnect"
        confirmColor="error"
        onConfirm={disconnect}
        onCancel={() => setConfirmDisconnect(false)}
      />
    </PageShell>
  );
}
