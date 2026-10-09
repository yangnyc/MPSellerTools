import { useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import FormControlLabel from "@mui/material/FormControlLabel";
import Icon from "@mui/material/Icon";
import Switch from "@mui/material/Switch";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { DetailList, InlineAlert, PageHeader, Section, StateBlock, formatMoney, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { AMAZON, AmazonImportApi, IDENTIFIER_LABELS, settingsPath, type AmazonImported, type AmazonImportStatus, type AmazonItem } from "../../api/channels";

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

type Change = React.ChangeEvent<HTMLInputElement>;

// One item of Amazon's catalog, looked up, shown, and made a product here.
export default function AmazonImportItemPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [status, setStatus] = useState<AmazonImportStatus | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [item, setItem] = useState<AmazonItem | null>(null);
  // What a search by name found, to choose from; null when nothing was searched for by name.
  const [matches, setMatches] = useState<AmazonItem[] | null>(null);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"lookup" | "import" | null>(null);
  const [imported, setImported] = useState<(AmazonImported & { title: string }) | null>(null);

  const [sku, setSku] = useState("");
  const [price, setPrice] = useState("");
  const [stock, setStock] = useState("0");
  const [updateExisting, setUpdateExisting] = useState(false);

  useEffect(() => {
    AmazonImportApi.status()
      .then(setStatus)
      .catch((err) => setLoadError(message(err, "Failed to load the import page.")));
  }, []);

  const choose = (found: AmazonItem) => {
    setItem(found);
    setSku(found.suggestedSku);
    setPrice(found.listPrice === null ? "" : String(found.listPrice));
    setStock("0");
    setUpdateExisting(false);
  };

  const lookup = async () => {
    setBusy("lookup");
    setLookupError(null);
    setImported(null);
    try {
      const found = await AmazonImportApi.find(query.trim());
      // One answer is the item asked for; several are a search by name to choose from.
      setMatches(found.length > 1 ? found : null);
      if (found.length === 1) choose(found[0]);
      else setItem(null);
      if (found.length === 0) setLookupError("Amazon's catalog has nothing for that in this marketplace.");
    } catch (err) {
      setItem(null);
      setMatches(null);
      setLookupError(message(err, "Could not look the item up on Amazon."));
    } finally {
      setBusy(null);
    }
  };

  const priceValue = price === "" ? null : Number(price);
  const stockValue = Number(stock);
  const valid =
    sku.trim().length > 0 && sku.trim().length <= 64 && (priceValue === null || priceValue >= 0) && stock !== "" && Number.isInteger(stockValue) && stockValue >= 0;
  // Only the product under the suggested SKU is known about before importing; another SKU is checked when it is saved.
  const existing = item?.existingProductId && sku.trim() === item.suggestedSku ? item.existingProductId : null;

  const importItem = async () => {
    if (!item) return;
    setBusy("import");
    try {
      const result = await AmazonImportApi.importItem({ asin: item.asin, sku: sku.trim(), price: priceValue, stockQuantity: stockValue, updateExisting });
      setImported({ ...result, title: item.title ?? item.asin });
      notify(result.outcome === 0 ? "The product was imported." : "The product was brought up to date.", "success");
      setItem(null);
      // What was searched for by name stays, with this one marked as here now, so the next can be chosen.
      setMatches((current) => current?.map((m) => (m.asin === item.asin && sku.trim() === m.suggestedSku ? { ...m, existingProductId: result.productId } : m)) ?? null);
      if (!matches) setQuery("");
    } catch (err) {
      notify(message(err, "Could not import the item."), "error");
    } finally {
      setBusy(null);
    }
  };

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };
  const size = item && item.length !== null ? `${item.length} × ${item.width} × ${item.height} ${item.dimensionUnit}` : null;

  return (
    <PageShell>
      <PageHeader
        icon="download"
        title="Import from Amazon: one item"
        subtitle="Find an item in Amazon's catalog by its name, ASIN or barcode and make it a product here, with its name, description, pictures, barcodes, weight and size."
        actions={
          <MDButton component={RouterLink} to="/import/amazon/bulk" variant="outlined" color="info" size="small">
            Import in bulk
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
          <Section icon="search" title="Find the item" subtitle={`Read through ${status.accountName}. Nothing is changed on Amazon.`}>
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                if (query.trim().length > 0 && !busy) lookup();
              }}
              sx={{ display: "flex", gap: 1.5, alignItems: "flex-start" }}
            >
              <MDInput
                label="Name, ASIN, Amazon address or barcode"
                fullWidth
                value={query}
                onChange={(e: Change) => setQuery(e.target.value)}
                helperText="Words of the item's name, an ASIN such as B08N5WRWNW, the address of its page on Amazon, or a UPC or EAN."
              />
              <MDButton type="submit" variant="gradient" color="info" disabled={query.trim().length === 0 || !!busy} startIcon={<Icon>search</Icon>}>
                {busy === "lookup" ? "Searching…" : "Search"}
              </MDButton>
            </Box>
            {lookupError && (
              <InlineAlert tone="error" sx={{ mt: 2 }}>
                {lookupError}
              </InlineAlert>
            )}
            {imported && (
              <InlineAlert tone="success" title={imported.outcome === 0 ? "Imported" : "Brought up to date"} sx={{ mt: 2 }}>
                {imported.title} is in your catalog as {imported.sku}.{" "}
                <Box component={RouterLink} to={`/products/${imported.productId}`} sx={{ color: c.accent }}>
                  Open the product
                </Box>
              </InlineAlert>
            )}
          </Section>

          {matches && (
            <Section icon="list" title="Found on Amazon" subtitle={`The ${matches.length} items Amazon puts first for that name. Choose the one to import.`} flush>
              {matches.map((match) => (
                <Box
                  key={match.asin}
                  sx={{ display: "flex", alignItems: "center", gap: 2, px: 3, py: 1.5, borderTop: `1px solid ${c.border}`, backgroundColor: item?.asin === match.asin ? c.hover : undefined }}
                >
                  {match.imageUrls[0] ? (
                    <Box component="img" src={match.imageUrls[0]} alt="" sx={{ width: 56, height: 56, flexShrink: 0, objectFit: "contain", borderRadius: 1, border: `1px solid ${c.border}`, backgroundColor: "#fff" }} />
                  ) : (
                    <Box sx={{ width: 56, height: 56, flexShrink: 0, borderRadius: 1, border: `1px solid ${c.border}` }} />
                  )}
                  <Box sx={{ flex: 1, minWidth: 0, lineHeight: 1.35 }}>
                    <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text, overflowWrap: "anywhere" }}>{match.title ?? match.asin}</Box>
                    <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                      {[match.brand, match.asin, match.category, match.existingProductId ? "already in your catalog" : null].filter(Boolean).join(" · ")}
                    </Box>
                  </Box>
                  <MDButton variant={item?.asin === match.asin ? "gradient" : "outlined"} color="info" size="small" onClick={() => choose(match)} aria-label={`Choose ${match.asin}`}>
                    {item?.asin === match.asin ? "Chosen" : "Choose"}
                  </MDButton>
                </Box>
              ))}
            </Section>
          )}

          {item && (
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
              <Section icon="inventory_2" title={item.title ?? item.asin} subtitle={[item.brand, item.asin].filter(Boolean).join(" · ")}>
                <Box sx={{ display: "grid", gap: 2.5 }}>
                  {item.imageUrls.length > 0 && (
                    <Box sx={{ display: "flex", gap: 1.5, overflowX: "auto", pb: 0.5 }}>
                      {item.imageUrls.map((url, index) => (
                        <Box
                          key={url}
                          component="img"
                          src={url}
                          alt={index === 0 ? "Main picture" : `Picture ${index + 1}`}
                          sx={{ width: 96, height: 96, flexShrink: 0, objectFit: "contain", borderRadius: 1, border: `1px solid ${c.border}`, backgroundColor: "#fff" }}
                        />
                      ))}
                    </Box>
                  )}
                  <DetailList
                    items={[
                      { label: "Category", value: item.category ?? "—" },
                      { label: "Amazon product type", value: item.productType ?? "—" },
                      { label: "List price", value: item.listPrice === null ? "Amazon has none" : `${formatMoney(item.listPrice)}${item.currency && item.currency !== "USD" ? ` ${item.currency}` : ""}` },
                      ...item.identifiers.map((id) => ({ label: IDENTIFIER_LABELS[id.type], value: id.value })),
                      { label: "Weight", value: item.weightValue === null ? "—" : `${item.weightValue} ${item.weightUnit}` },
                      { label: "Size", value: size ?? "—" },
                      { label: "Pictures", value: String(item.imageUrls.length) },
                    ]}
                  />
                  {item.description && (
                    <Box>
                      <Box sx={{ mb: 0.5, fontSize: "0.8125rem", fontWeight: 700, color: c.text }}>Description</Box>
                      <Box sx={{ maxHeight: 260, overflowY: "auto", whiteSpace: "pre-wrap", overflowWrap: "anywhere", fontSize: "0.875rem", lineHeight: 1.5, color: c.text }}>
                        {item.description}
                      </Box>
                    </Box>
                  )}
                </Box>
              </Section>

              <Section icon="add_box" title="Make it a product" subtitle="Everything alongside is saved with it. Price and stock are yours to set.">
                <Box
                  component="form"
                  noValidate
                  onSubmit={(e: React.FormEvent) => {
                    e.preventDefault();
                    if (valid && !busy && (!existing || updateExisting)) importItem();
                  }}
                  sx={{ display: "grid", gap: 2.5 }}
                >
                  <MDInput label="SKU" fullWidth value={sku} onChange={(e: Change) => setSku(e.target.value)} helperText="Your own code for the product. It cannot be changed later." />
                  <Box sx={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 2.5 }}>
                    <MDInput label="Price (USD)" type="number" fullWidth inputProps={{ min: 0, step: "0.01" }} value={price} onChange={(e: Change) => setPrice(e.target.value)} />
                    <MDInput label="Stock" type="number" fullWidth inputProps={{ min: 0, step: 1 }} value={stock} onChange={(e: Change) => setStock(e.target.value)} />
                  </Box>
                  <Box sx={hint}>
                    Amazon's catalog holds no selling price or stock. The price starts from Amazon's list price where it has one; left empty, it is saved as 0.
                  </Box>
                  {existing && (
                    <InlineAlert tone="warning" title="Already in your catalog">
                      A product with this SKU is{" "}
                      <Box component={RouterLink} to={`/products/${existing}`} sx={{ color: c.accent }}>
                        already here
                      </Box>
                      . Choose another SKU to import a second one, or bring that one up to date.
                    </InlineAlert>
                  )}
                  <FormControlLabel
                    control={<Switch checked={updateExisting} onChange={(e: Change) => setUpdateExisting(e.target.checked)} />}
                    label={<Box sx={{ fontSize: "0.875rem", color: c.text }}>If a product with this SKU is already here, bring it up to date</Box>}
                  />
                  {updateExisting && (
                    <Box sx={hint}>Its name, brand, description and category are replaced with Amazon's. Its stock, its pictures and anything Amazon has nothing for are kept; its price is kept unless one is entered above.</Box>
                  )}
                  <Box sx={{ display: "flex", justifyContent: "flex-end" }}>
                    <MDButton type="submit" variant="gradient" color="info" disabled={!valid || !!busy || (!!existing && !updateExisting)} startIcon={<Icon>download</Icon>}>
                      {busy === "import" ? "Importing…" : existing ? "Bring up to date" : "Import"}
                    </MDButton>
                  </Box>
                </Box>
              </Section>
            </Box>
          )}
        </Box>
      )}
    </PageShell>
  );
}
