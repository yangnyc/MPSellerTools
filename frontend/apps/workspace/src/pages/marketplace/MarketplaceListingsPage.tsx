import { useCallback, useEffect, useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import { Identity, InlineAlert, PageHeader, Section, StateBlock, StatusPill, formatMoney, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ListingEditDialog from "./ListingEditDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import {
  ChannelListingsApi,
  ChannelsApi,
  type ChannelAccount,
  type ChannelListing,
  type Marketplace,
  canSendAgain,
} from "../../api/channels";
import { LISTING_OBSERVED } from "../../lib/status";

const OBSERVED = LISTING_OBSERVED;

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// A marketplace's own page: setting it up as a sales channel, then the products offered on it.
export default function MarketplaceListingsPage({ marketplace }: { marketplace: Marketplace }) {
  const { name, path } = marketplace;
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [account, setAccount] = useState<ChannelAccount | null>(null);
  const [listings, setListings] = useState<ChannelListing[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [sellerId, setSellerId] = useState("");
  const [editing, setEditing] = useState<ChannelListing | null>(null);

  const load = useCallback(
    () =>
      ChannelsApi.list()
        .then(async (accounts) => {
          const found = accounts.find((a) => a.channel === marketplace.kind) ?? null;
          const listed = found ? await ChannelListingsApi.list(found.id) : [];
          setAccount(found);
          setListings(listed);
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, `Failed to load ${name}.`)))
        .finally(() => setLoading(false)),
    [marketplace.kind, name]
  );

  useEffect(() => {
    load();
  }, [load]);

  const run = async (name: string, action: () => Promise<void>, fallback: string) => {
    setBusy(name);
    try {
      await action();
    } catch (err) {
      notify(message(err, fallback), "error");
    } finally {
      setBusy(null);
    }
  };

  const setUp = () =>
    run("setup", async () => {
      const created = await ChannelsApi.create({ channel: marketplace.kind, name, sellerId: sellerId.trim() || null });
      await ChannelsApi.addMarket(created.id, marketplace.marketplaceCode);
      await load();
      notify(`${name} added. Nothing is sent to ${name} until live writes are switched on.`, "success");
    }, `Could not add ${name}.`);

  const publish = (listing: ChannelListing) =>
    run(listing.id, async () => {
      try {
        const queued = await ChannelListingsApi.publish(listing.id);
        notify(queued.liveWrites ? `Queued for ${name}.` : `Queued as a dry run: live writes to ${name} are off.`, "success");
      } catch (err) {
        // 422: the listing does not validate. The reasons are fetched and shown, not just the status.
        if (!(err instanceof ApiError) || err.status !== 422) throw err;
        const { issues } = await ChannelListingsApi.validate(listing.id);
        notify(issues.map((i) => i.message).join(" ") || `This listing is not ready for ${name}.`, "error");
      }
      await load();
    }, "Could not publish.");

  const sendAgain = (listing: ChannelListing) =>
    run(listing.id, async () => {
      const queued = await ChannelListingsApi.retry(listing.id);
      await load();
      notify(queued.liveWrites ? `Queued for ${name} again.` : `Queued as a dry run: live writes to ${name} are off.`, "success");
    }, "Could not send it again.");

  const deactivate = (listing: ChannelListing) =>
    run(listing.id, async () => {
      await ChannelListingsApi.deactivate(listing.id);
      await load();
      notify("Taking it off sale was queued.", "success");
    }, "Could not take it off sale.");

  const table = useMemo(() => {
    type CellProps = { row: { original: ChannelListing } };
    const columns = [
      {
        Header: "Product",
        id: "product",
        accessor: (row: ChannelListing) => `${row.effectiveTitle ?? ""} ${row.sellerSku}`,
        Cell: ({ row }: CellProps) => (
          <Box component={RouterLink} to={`/products/${row.original.productId}`} aria-label={`View ${row.original.sellerSku}`} sx={{ display: "block", color: "inherit" }}>
            <Identity name={row.original.effectiveTitle ?? row.original.sellerSku} secondary={row.original.sellerSku} square />
          </Box>
        ),
      },
      {
        Header: "Price",
        accessor: "effectivePrice",
        align: "right" as const,
        Cell: ({ row }: CellProps) => formatMoney(row.original.effectivePrice),
      },
      { Header: "Quantity", accessor: "effectiveQuantity", align: "right" as const },
      {
        Header: "Status",
        id: "status",
        accessor: (row: ChannelListing) => (row.desiredState === 0 ? "Draft" : OBSERVED[row.observedStatus].label),
        Cell: ({ row }: CellProps) =>
          row.original.desiredState === 0 ? (
            <StatusPill tone="info" label="Draft" />
          ) : (
            <StatusPill tone={OBSERVED[row.original.observedStatus].tone} label={OBSERVED[row.original.observedStatus].label} />
          ),
      },
      {
        Header: "",
        id: "actions",
        accessor: "id",
        align: "right" as const,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
            <MDButton variant="text" color="info" size="small" disabled={!!busy} onClick={() => setEditing(row.original)} aria-label={`Edit ${row.original.sellerSku}`}>
              Edit
            </MDButton>
            {canSendAgain(row.original) && (
              <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => sendAgain(row.original)} aria-label={`Send ${row.original.sellerSku} again`}>
                Send again
              </MDButton>
            )}
            {row.original.desiredState === 1 ? (
              <MDButton variant="outlined" color="secondary" size="small" disabled={!!busy} onClick={() => deactivate(row.original)}>
                Take off sale
              </MDButton>
            ) : (
              <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => publish(row.original)}>
                Publish
              </MDButton>
            )}
          </Box>
        ),
      },
    ];
    return { columns, rows: listings };
    // publish/sendAgain/deactivate only close over state setters and `busy`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [listings, busy]);

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const addButton = (size: "small" | "medium") => (
    <MDButton component={RouterLink} to={`${path}/add-product`} variant="gradient" color="info" size={size} startIcon={<Icon>add</Icon>}>
      Add product
    </MDButton>
  );

  return (
    <PageShell>
      <PageHeader
        icon={marketplace.icon}
        title={name}
        subtitle={`Your products offered on ${name}.`}
        actions={account && addButton("medium")}
      />

      {loading && <StateBlock kind="loading" title={`Loading ${name}`} />}
      {loadError && <StateBlock kind="error" title={`${name} could not be loaded`} message={loadError} />}

      {!loading && !loadError && !account && (
        <Section icon="link" title={`Add ${name}`} subtitle={`Sets ${name} up as a sales channel for the United States marketplace.`}>
          <Box sx={{ display: "grid", gap: 2.5, maxWidth: 480 }}>
            {marketplace.asksSellerId && (
              <MDInput
                label="Seller ID (optional for now)"
                fullWidth
                value={sellerId}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSellerId(e.target.value)}
                helperText="Your seller ID with the marketplace. Needed before anything can be sent."
              />
            )}
            <Box sx={hint}>Nothing is sent to {name} by adding it here: listings are prepared and checked as dry runs until live writes are switched on.</Box>
            <Box>
              <MDButton variant="gradient" color="info" disabled={!!busy} onClick={setUp} startIcon={<Icon>add</Icon>}>
                {busy === "setup" ? "Adding…" : `Add ${name}`}
              </MDButton>
            </Box>
          </Box>
        </Section>
      )}

      {!loading && !loadError && account && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1.5 }}>
            <StatusPill tone={account.environment === 1 ? "info" : "neutral"} label={account.environment === 1 ? "Production" : "Sandbox"} />
            <StatusPill tone={account.effectiveLiveWrites ? "success" : "warning"} label={account.effectiveLiveWrites ? "Live writes on" : "Dry run only"} />
          </Box>
          {account.lastError && (
            <InlineAlert tone="error" title={`${name} refused this account`}>
              {account.lastError}
            </InlineAlert>
          )}
          <Section flush>
            {listings.length === 0 ? (
              <StateBlock
                icon={marketplace.icon}
                title={`Nothing on ${name} yet`}
                message="Add a product and it shows up here, ready to publish."
                action={addButton("small")}
              />
            ) : (
              <DataTable table={table} canSearch />
            )}
          </Section>
        </Box>
      )}
      {editing && <ListingEditDialog key={editing.id} listing={editing} marketplace={marketplace} onClose={() => setEditing(null)} onSaved={load} />}
    </PageShell>
  );
}
