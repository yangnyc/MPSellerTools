import { useCallback, useEffect, useState } from "react";
import { Link as RouterLink, useParams } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { DetailList, IconTile, InlineAlert, PageHeader, Section, StatCard, StateBlock, StatusPill, formatDateTime, formatMoney, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import ProductDetailsDialog from "../components/ProductDetailsDialog";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { LISTING_OBSERVED } from "../lib/status";
import {
  CatalogApi,
  ChannelListingsApi,
  ChannelsApi,
  IDENTIFIER_LABELS,
  marketplaces,
  type CatalogProduct,
  type ChannelAccount,
  type ChannelListing,
  type Marketplace,
  canSendAgain,
} from "../api/channels";
import ListingEditDialog from "./marketplace/ListingEditDialog";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// One product on one page: what it is, what is in stock, and where it stands
// on every marketplace, with the same actions the marketplaces' own pages have.
export default function ProductViewPage() {
  const { id = "" } = useParams();
  const { user } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [product, setProduct] = useState<CatalogProduct | null>(null);
  const [listings, setListings] = useState<ChannelListing[]>([]);
  const [accounts, setAccounts] = useState<ChannelAccount[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [editingDetails, setEditingDetails] = useState(false);
  const [editing, setEditing] = useState<{ listing: ChannelListing; marketplace: Marketplace } | null>(null);

  const load = useCallback(
    () =>
      // The channel accounts are the admin's to see; an employee gets the listings without them.
      Promise.all([CatalogApi.product(id), ChannelListingsApi.ofProduct(id), isTenantAdmin ? ChannelsApi.list() : Promise.resolve([])])
        .then(([nextProduct, nextListings, nextAccounts]) => {
          setProduct(nextProduct);
          setListings(nextListings);
          setAccounts(nextAccounts);
          setLoadError(null);
        })
        .catch((err) => setLoadError(err instanceof ApiError && err.status === 404 ? "This product does not exist." : message(err, "Failed to load the product."))),
    [id, isTenantAdmin]
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

  const defaultVariant = product?.variants.find((v) => v.isDefault) ?? product?.variants[0];

  const addTo = (marketplace: Marketplace, account: ChannelAccount) =>
    run(`add:${marketplace.name}`, async () => {
      if (!defaultVariant || !account.markets[0]) throw new ApiError(409, "This product has no variant to list yet.");
      await ChannelListingsApi.add(account.markets[0].id, defaultVariant.id);
      notify(`Added to ${marketplace.name} as a draft.`, "success");
    }, `Could not add it to ${marketplace.name}.`);

  const publish = (listing: ChannelListing, marketplace: Marketplace) =>
    run(listing.id, async () => {
      try {
        const queued = await ChannelListingsApi.publish(listing.id);
        notify(queued.liveWrites ? `Queued for ${marketplace.name}.` : `Queued as a dry run: live writes to ${marketplace.name} are off.`, "success");
      } catch (err) {
        // 422: the listing does not validate. The reasons are fetched and shown, not just the status.
        if (!(err instanceof ApiError) || err.status !== 422) throw err;
        const { issues } = await ChannelListingsApi.validate(listing.id);
        notify(issues.map((i) => i.message).join(" ") || `This listing is not ready for ${marketplace.name}.`, "error");
      }
    }, "Could not publish.");

  const sendAgain = (listing: ChannelListing, marketplace: Marketplace) =>
    run(listing.id, async () => {
      const queued = await ChannelListingsApi.retry(listing.id);
      notify(queued.liveWrites ? `Queued for ${marketplace.name} again.` : `Queued as a dry run: live writes to ${marketplace.name} are off.`, "success");
    }, "Could not send it again.");

  const deactivate = (listing: ChannelListing) =>
    run(listing.id, async () => {
      await ChannelListingsApi.deactivate(listing.id);
      notify("Taking it off sale was queued.", "success");
    }, "Could not take it off sale.");

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const images = product?.media.filter((m) => m.variantId === null) ?? [];
  const sum = (pick: (v: CatalogProduct["variants"][number]) => number) => product?.variants.filter((v) => !v.isArchived).reduce((total, v) => total + pick(v), 0);

  const marketplaceCard = (marketplace: Marketplace) => {
    const { name } = marketplace;
    const own = listings.filter((l) => l.channel === marketplace.kind);
    const account = accounts.find((a) => a.channel === marketplace.kind);

    return (
      <Section
        key={name}
        icon={marketplace.icon}
        title={name}
        subtitle={own.length === 0 ? `Not on ${name}` : own.length === 1 ? "1 listing" : `${own.length} listings`}
        actions={
          isTenantAdmin &&
          (account ? (
            <MDButton component={RouterLink} to={marketplace.path} variant="text" color="info" size="small">
              All {name} products
            </MDButton>
          ) : (
            <MDButton component={RouterLink} to={marketplace.path} variant="outlined" color="info" size="small">
              Set up {name}
            </MDButton>
          ))
        }
      >
        {own.length === 0 && (
          <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 2 }}>
            <Box sx={hint}>
              {!isTenantAdmin
                ? `This product is not offered on ${name}.`
                : account
                  ? `This product is not offered on ${name} yet. Adding it creates a draft; nothing is sent until you publish.`
                  : `${name} is not set up as a sales channel yet.`}
            </Box>
            {isTenantAdmin && account && (
              <MDButton variant="gradient" color="info" size="small" disabled={!!busy} onClick={() => addTo(marketplace, account)} startIcon={<Icon>add</Icon>}>
                {busy === `add:${name}` ? "Adding…" : `Add to ${name}`}
              </MDButton>
            )}
          </Box>
        )}

        {own.map((listing, index) => {
          const draft = listing.desiredState === 0;
          const status = draft ? { label: "Draft", tone: "info" as const } : LISTING_OBSERVED[listing.observedStatus];
          const externalId = listing.references.Listing ?? listing.references.CatalogItem;
          return (
            <Box key={listing.id} sx={index > 0 ? { mt: 3, pt: 3, borderTop: `1px solid ${c.border}` } : null}>
              <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 1.5, mb: 2 }}>
                <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1 }}>
                  <StatusPill tone={status.tone} label={status.label} pulse={!draft && listing.observedStatus === 2} />
                  {listing.hasPriceConflict && <StatusPill tone="warning" label="Price differs" />}
                  <Box sx={{ fontSize: "0.8125rem", fontFamily: "monospace", color: c.muted }}>{listing.sellerSku}</Box>
                </Box>
                {isTenantAdmin && (
                  <Box sx={{ display: "flex", gap: 1 }}>
                    {listing.storeUrl && (
                      <MDButton component="a" href={listing.storeUrl} target="_blank" rel="noreferrer" variant="text" color="info" size="small" endIcon={<Icon>open_in_new</Icon>}>
                        View on {name}
                      </MDButton>
                    )}
                    <MDButton variant="text" color="info" size="small" disabled={!!busy} onClick={() => setEditing({ listing, marketplace })} aria-label={`Edit on ${name}`}>
                      Edit
                    </MDButton>
                    {canSendAgain(listing) && (
                      <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => sendAgain(listing, marketplace)} aria-label={`Send to ${name} again`}>
                        Send again
                      </MDButton>
                    )}
                    {listing.desiredState === 1 ? (
                      <MDButton variant="outlined" color="secondary" size="small" disabled={!!busy} onClick={() => deactivate(listing)} aria-label={`Take off sale on ${name}`}>
                        Take off sale
                      </MDButton>
                    ) : (
                      <MDButton variant="outlined" color="info" size="small" disabled={!!busy} onClick={() => publish(listing, marketplace)} aria-label={`Publish on ${name}`}>
                        Publish
                      </MDButton>
                    )}
                  </Box>
                )}
              </Box>
              <DetailList
                columns={3}
                items={[
                  { label: "Title", value: listing.effectiveTitle ?? "—" },
                  { label: "Price", value: formatMoney(listing.effectivePrice) + (listing.priceOverride === null ? "" : " (its own)") },
                  { label: "Quantity offered", value: `${listing.effectiveQuantity}${listing.quantityCap === null ? "" : ` (capped at ${listing.quantityCap})`}` },
                  { label: "Pictures sent", value: `${listing.effectiveImageUrls.length} of up to ${listing.imageRules.maxImages}` },
                  { label: `Number on ${name}`, value: externalId ?? "None yet" },
                  { label: "Last reported", value: listing.observedAtUtc ? formatDateTime(listing.observedAtUtc) : "Never" },
                ]}
              />
              {(listing.issues?.length ?? 0) > 0 && (
                <InlineAlert tone="warning" title="To fix before it can be published" sx={{ mt: 2 }}>
                  <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
                    {listing.issues!.map((issue) => (
                      <li key={`${issue.path}:${issue.code}`}>{issue.message}</li>
                    ))}
                  </Box>
                </InlineAlert>
              )}
            </Box>
          );
        })}
      </Section>
    );
  };

  return (
    <PageShell>
      <PageHeader
        icon="inventory_2"
        title={product?.name ?? "Product"}
        subtitle={product ? product.sku : undefined}
        backTo="/products"
        backLabel="All products"
        actions={
          isTenantAdmin &&
          product && (
            <MDButton variant="gradient" color="info" onClick={() => setEditingDetails(true)} startIcon={<Icon>edit</Icon>}>
              Edit details
            </MDButton>
          )
        }
      />

      {loadError && <StateBlock kind="error" title="The product could not be loaded" message={loadError} />}
      {!product && !loadError && <StateBlock kind="loading" title="Loading the product" />}

      {product && !loadError && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 3 }}>
            <StatCard icon="payments" tone="success" label="Price" value={defaultVariant ? formatMoney(defaultVariant.price) : "—"} hint="The product's own price" />
            <StatCard icon="warehouse" tone="info" label="On hand" value={sum((v) => v.onHand)} hint="In the warehouse" />
            <StatCard icon="sell" tone="primary" label="Left to sell" value={sum((v) => v.availableToSell)} hint="On hand − held − safety stock" />
            <StatCard icon="storefront" tone="warning" label="Marketplaces" value={new Set(listings.map((l) => l.channel)).size} hint={`of ${marketplaces().length} it is offered on`} />
          </Box>

          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
            <Section icon="description" title="Details" subtitle="What every marketplace is told, unless a listing says otherwise.">
              <DetailList
                items={[
                  { label: "Brand", value: product.brand ?? "—" },
                  { label: "Category", value: product.category ?? "—" },
                  ...product.identifiers.filter((i) => i.variantId === null).map((i) => ({ label: IDENTIFIER_LABELS[i.type], value: i.value })),
                ]}
              />
              <Box sx={{ mt: 2.5, fontSize: "0.9375rem", lineHeight: 1.6, color: product.description ? c.text : c.muted, whiteSpace: "pre-wrap" }}>
                {product.description ?? "No description yet."}
              </Box>
              {product.variants.length > 1 && (
                <Box sx={{ mt: 2.5, pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                  <Box sx={{ mb: 1, fontSize: "0.875rem", fontWeight: 700, color: c.text }}>Variants</Box>
                  {product.variants.map((variant) => (
                    <Box key={variant.id} sx={{ display: "flex", justifyContent: "space-between", gap: 2, py: 0.75, fontSize: "0.875rem" }}>
                      <Box sx={{ color: c.text }}>
                        {variant.name ?? product.name} <Box component="span" sx={{ color: c.muted, fontFamily: "monospace", fontSize: "0.8125rem" }}>{variant.sku}</Box>
                      </Box>
                      <Box sx={{ color: c.muted, whiteSpace: "nowrap" }}>
                        {formatMoney(variant.price)} · {variant.availableToSell} left
                      </Box>
                    </Box>
                  ))}
                </Box>
              )}
            </Section>

            <Section icon="image" title="Pictures" subtitle={images.length === 0 ? "None yet" : `${images.length}; the first is the main picture`}>
              {images.length === 0 ? (
                <Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
                  <IconTile icon="hide_image" tone="neutral" size={40} />
                  <Box sx={hint}>Marketplaces need at least one picture. {isTenantAdmin ? "Add them under Edit details." : ""}</Box>
                </Box>
              ) : (
                <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(96px, 1fr))", gap: 1.5 }}>
                  {images.map((image, index) => (
                    <Box
                      key={image.id}
                      component="img"
                      src={image.url}
                      alt={image.altText ?? `${product.name}, picture ${index + 1}`}
                      sx={{ width: "100%", aspectRatio: "1 / 1", objectFit: "cover", borderRadius: "12px", border: `1px solid ${c.border}`, backgroundColor: c.surfaceAlt }}
                    />
                  ))}
                </Box>
              )}
            </Section>
          </Box>

          <Box>
            <Box component="h2" sx={{ mb: 2, fontSize: "1.125rem", fontWeight: 700, color: c.text }}>
              On the marketplaces
            </Box>
            <Box sx={{ display: "grid", gap: 3 }}>{marketplaces().map(marketplaceCard)}</Box>
          </Box>
        </Box>
      )}

      {editingDetails && product && (
        <ProductDetailsDialog
          key={product.id}
          productId={product.id}
          onClose={() => {
            setEditingDetails(false);
            load();
          }}
        />
      )}
      {editing && (
        <ListingEditDialog key={editing.listing.id} listing={editing.listing} marketplace={editing.marketplace} onClose={() => setEditing(null)} onSaved={load} />
      )}
    </PageShell>
  );
}
