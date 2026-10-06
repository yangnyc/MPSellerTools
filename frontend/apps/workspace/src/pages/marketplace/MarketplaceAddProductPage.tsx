import { useCallback, useEffect, useMemo, useState } from "react";
import { Link as RouterLink, useNavigate } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { PageHeader, Section, StateBlock, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { ProductsApi } from "../../api/resources";
import type { Product } from "../../api/types";
import {
  CatalogApi,
  ChannelListingsApi,
  ChannelsApi,
  type CatalogSearchResult,
  type ChannelAccount,
  type ChannelMarket,
  type Marketplace,
} from "../../api/channels";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

type Change = React.ChangeEvent<HTMLInputElement>;

// Adds a product to a marketplace: one already in the catalog, or a new one created on the spot.
export default function MarketplaceAddProductPage({ marketplace }: { marketplace: Marketplace }) {
  const { name: channelName, path } = marketplace;
  const { c } = useKit();
  const { notify } = useSnackbar();
  const navigate = useNavigate();

  const [market, setMarket] = useState<ChannelMarket | null>(null);
  const [account, setAccount] = useState<ChannelAccount | null>(null);
  // The marketplace's own item to make the offer on (an ASIN), typed in or picked from a search.
  const [catalogItemId, setCatalogItemId] = useState("");
  const [searchText, setSearchText] = useState("");
  const [matches, setMatches] = useState<CatalogSearchResult[] | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [listedSkus, setListedSkus] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const [productId, setProductId] = useState("");
  const [sku, setSku] = useState("");
  const [name, setName] = useState("");
  const [price, setPrice] = useState("");
  const [stock, setStock] = useState("");

  const load = useCallback(
    () =>
      Promise.all([ChannelsApi.list(), ProductsApi.list()])
        .then(async ([accounts, catalog]) => {
          const found = accounts.find((a) => a.channel === marketplace.kind);
          const listed = found ? await ChannelListingsApi.list(found.id) : [];
          setMarket(found?.markets[0] ?? null);
          setAccount(found ?? null);
          setProducts(catalog);
          setListedSkus(new Set(listed.map((l) => l.sellerSku)));
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, `Failed to load ${channelName}.`)))
        .finally(() => setLoading(false)),
    [marketplace.kind, channelName]
  );

  useEffect(() => {
    load();
  }, [load]);

  // A product is offered on a marketplace through its own SKU, which is its default variant.
  const list = async (id: string) => {
    const detail = await CatalogApi.product(id);
    const variant = detail.variants.find((v) => v.isDefault) ?? detail.variants[0];
    if (!market || !variant) throw new ApiError(409, "This product has no variant to list yet.");
    await ChannelListingsApi.add(market.id, variant.id, catalogItemId.trim() || undefined);
    notify(`${detail.name} added to ${channelName} as a draft.`, "success");
    navigate(path);
  };

  const search = async () => {
    if (!account) return;
    setBusy("search");
    try {
      setMatches(await ChannelsApi.catalogSearch(account.id, searchText.trim()));
    } catch (err) {
      setMatches(null);
      notify(message(err, `Could not search ${channelName}.`), "error");
    } finally {
      setBusy(null);
    }
  };

  const run = async (kind: string, action: () => Promise<void>) => {
    setBusy(kind);
    try {
      await action();
    } catch (err) {
      notify(message(err, `Could not add the product to ${channelName}.`), "error");
      // A new product may have been created before the listing step failed; show it in the dropdown.
      load();
    } finally {
      setBusy(null);
    }
  };

  const addExisting = () => run("existing", () => list(productId));

  const priceValue = Number(price);
  const stockValue = Number(stock);
  const newValid =
    sku.trim().length > 0 && sku.trim().length <= 64 && name.trim().length > 0 && name.trim().length <= 200 &&
    price !== "" && priceValue >= 0 && stock !== "" && Number.isInteger(stockValue) && stockValue >= 0;

  const addNew = () =>
    run("new", async () => {
      const created = await ProductsApi.create({ sku: sku.trim(), name: name.trim(), price: priceValue, stockQuantity: stockValue });
      await list(created.id);
    });

  // Products whose own SKU is not on the marketplace yet.
  const addable = useMemo(() => products.filter((p) => !listedSkus.has(p.sku)), [products, listedSkus]);
  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const back = (
    <MDButton component={RouterLink} to={path} variant="outlined" color="info" size="small">
      Back to {channelName}
    </MDButton>
  );

  return (
    <PageShell>
      <PageHeader icon="add_shopping_cart" title={`Add product to ${channelName}`} subtitle="Pick one from your catalog, or create a new product and list it in one step." actions={back} />

      {loading && <StateBlock kind="loading" title="Loading" />}
      {loadError && <StateBlock kind="error" title="This page could not be loaded" message={loadError} />}

      {!loading && !loadError && !market && (
        <Section flush>
          <StateBlock
            icon="link"
            title={`${channelName} is not set up yet`}
            message={`Add ${channelName} as a sales channel first, then come back to add products.`}
            action={
              <MDButton component={RouterLink} to={path} variant="gradient" color="info" size="small">
                Set up {channelName}
              </MDButton>
            }
          />
        </Section>
      )}

      {!loading && !loadError && market && (
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, alignItems: "start", gap: 3 }}>
          <Section icon="inventory_2" title="From your catalog" subtitle={`A product you already have. It is added to ${channelName} as a draft.`}>
            <Box sx={{ display: "grid", gap: 2.5 }}>
              <MDInput
                select
                label="Product"
                fullWidth
                SelectProps={{ native: true }}
                InputLabelProps={{ shrink: true }}
                value={productId}
                disabled={addable.length === 0}
                onChange={(e: Change) => setProductId(e.target.value)}
              >
                <option value="">{addable.length === 0 ? "No products left to add" : "Choose a product…"}</option>
                {addable.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.sku})
                  </option>
                ))}
              </MDInput>
              {products.length === 0 && <Box sx={hint}>Your catalog is empty. Create the first product with the form alongside.</Box>}
              {marketplace.catalogSearch && (
                <Box sx={{ display: "grid", gap: 1.5, pt: 2, borderTop: `1px solid ${c.border}` }}>
                  <MDInput
                    label="ASIN (optional)"
                    fullWidth
                    value={catalogItemId}
                    onChange={(e: Change) => setCatalogItemId(e.target.value)}
                    helperText={`If ${channelName} already sells this item, its ASIN makes your listing an offer on it. Applies to either way of adding.`}
                  />
                  {account?.effectiveLiveWrites ? (
                    <Box
                      component="form"
                      noValidate
                      onSubmit={(e: React.FormEvent) => {
                        e.preventDefault();
                        if (searchText.trim().length >= 2) search();
                      }}
                      sx={{ display: "flex", gap: 1.5, alignItems: "flex-start" }}
                    >
                      <MDInput label={`Find on ${channelName}`} fullWidth value={searchText} onChange={(e: Change) => setSearchText(e.target.value)} helperText="A product name, or a UPC or EAN." />
                      <MDButton type="submit" variant="outlined" color="info" disabled={searchText.trim().length < 2 || !!busy}>
                        {busy === "search" ? "Searching…" : "Search"}
                      </MDButton>
                    </Box>
                  ) : (
                    <Box sx={hint}>Searching {channelName} for the ASIN needs live writes switched on for the account; until then, type the ASIN in.</Box>
                  )}
                  {matches?.length === 0 && <Box sx={hint}>{channelName} found nothing for that.</Box>}
                  {matches?.map((item) => (
                    <Box key={item.catalogItemId} sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2 }}>
                      <Box sx={{ minWidth: 0, lineHeight: 1.35 }}>
                        <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{item.title ?? item.catalogItemId}</Box>
                        <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                          {item.catalogItemId}
                          {item.brand ? ` · ${item.brand}` : ""}
                        </Box>
                      </Box>
                      <MDButton variant="text" color="info" size="small" onClick={() => setCatalogItemId(item.catalogItemId)} aria-label={`Use ${item.catalogItemId}`}>
                        {catalogItemId === item.catalogItemId ? "Chosen" : "Use"}
                      </MDButton>
                    </Box>
                  ))}
                </Box>
              )}
              <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                <MDButton variant="gradient" color="info" disabled={!productId || !!busy} onClick={addExisting} startIcon={<Icon>add</Icon>}>
                  {busy === "existing" ? "Adding…" : `Add to ${channelName}`}
                </MDButton>
              </Box>
            </Box>
          </Section>

          <Section icon="add_box" title="New product" subtitle={`Creates the product in your catalog and adds it to ${channelName} as a draft.`}>
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                if (newValid) addNew();
              }}
              sx={{ display: "grid", gap: 2.5 }}
            >
              <MDInput label="SKU" fullWidth value={sku} onChange={(e: Change) => setSku(e.target.value)} helperText="Your own code for the product. It cannot be changed later." />
              <MDInput label="Name" fullWidth value={name} onChange={(e: Change) => setName(e.target.value)} />
              <Box sx={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 2.5 }}>
                <MDInput label="Price (USD)" type="number" fullWidth inputProps={{ min: 0, step: "0.01" }} value={price} onChange={(e: Change) => setPrice(e.target.value)} />
                <MDInput label="Stock" type="number" fullWidth inputProps={{ min: 0, step: 1 }} value={stock} onChange={(e: Change) => setStock(e.target.value)} />
              </Box>
              <Box sx={hint}>{marketplace.publishNeeds}</Box>
              <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                <MDButton type="submit" variant="gradient" color="info" disabled={!newValid || !!busy} startIcon={<Icon>add</Icon>}>
                  {busy === "new" ? "Creating…" : `Create and add to ${channelName}`}
                </MDButton>
              </Box>
            </Box>
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
