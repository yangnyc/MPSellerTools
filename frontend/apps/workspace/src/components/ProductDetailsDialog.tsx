import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, KitDialog, StateBlock, useKit } from "examples/Kit";
import { ApiError } from "../lib/api";
import { CatalogApi, IDENTIFIER_LABELS, type CatalogProduct, type ProductIdentifierType } from "../api/channels";
import { useSnackbar } from "./useSnackbar";

const IDENTIFIER_TYPES: ProductIdentifierType[] = [1, 2, 0, 3, 4];

type Change = React.ChangeEvent<HTMLInputElement>;
type Identifiers = Record<ProductIdentifierType, string>;

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// The product's own identifiers; one that belongs to a single variant is not edited here.
const identifiersOf = (product: CatalogProduct): Identifiers =>
  Object.fromEntries(
    IDENTIFIER_TYPES.map((type) => [type, product.identifiers.find((i) => i.type === type && i.variantId === null)?.value ?? ""])
  ) as Identifiers;

// What the marketplaces are told about a product beyond its name, price and
// stock: brand, description, category, barcodes and images. Each part saves
// on its own, so a mistake in one does not hold up the others.
export default function ProductDetailsDialog({ productId, onClose }: { productId: string; onClose: () => void }) {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [product, setProduct] = useState<CatalogProduct | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [brand, setBrand] = useState("");
  const [category, setCategory] = useState("");
  const [description, setDescription] = useState("");
  const [identifiers, setIdentifiers] = useState<Identifiers>({ 0: "", 1: "", 2: "", 3: "", 4: "" });
  const [imageUrl, setImageUrl] = useState("");

  const show = (next: CatalogProduct) => {
    setProduct(next);
    setBrand(next.brand ?? "");
    setCategory(next.category ?? "");
    setDescription(next.description ?? "");
    setIdentifiers(identifiersOf(next));
  };

  // Mounted afresh for each product (the page keys it by id), so there is nothing to reset here.
  useEffect(() => {
    CatalogApi.product(productId)
      .then(show)
      .catch((err) => setError(message(err, "The product could not be loaded.")));
  }, [productId]);

  const run = async (key: string, action: () => Promise<string>) => {
    setBusy(key);
    setError(null);
    try {
      notify(await action(), "success");
    } catch (err) {
      setError(message(err, "Save failed."));
    } finally {
      setBusy(null);
    }
  };

  const saveContent = () =>
    run("content", async () => {
      show(await CatalogApi.updateContent(product!.id, { brand: brand.trim() || null, description: description.trim() || null, category: category.trim() || null }));
      return "Details saved.";
    });

  const saveIdentifiers = () =>
    run("identifiers", async () => {
      const saved = identifiersOf(product!);
      let latest = product!;
      // One request per identifier that changed; the first one refused stops the rest, with its reason shown.
      for (const type of IDENTIFIER_TYPES) {
        if (identifiers[type].trim() !== saved[type]) {
          latest = await CatalogApi.setIdentifier(product!.id, type, identifiers[type].trim());
          setProduct(latest);
        }
      }
      // Only this part is refreshed: details typed in above but not yet saved stay as they are.
      setIdentifiers(identifiersOf(latest));
      return "Identifiers saved.";
    });

  const images = product?.media.filter((m) => m.variantId === null) ?? [];

  const addImage = () =>
    run("image", async () => {
      const next = await CatalogApi.addImage(product!.id, imageUrl.trim(), images.length === 0 ? 0 : Math.max(...images.map((m) => m.position)) + 1);
      setProduct(next);
      setImageUrl("");
      return "Image added.";
    });

  const removeImage = (mediaId: string) =>
    run(mediaId, async () => {
      await CatalogApi.removeImage(mediaId);
      setProduct(await CatalogApi.product(product!.id));
      return "Image removed.";
    });

  const heading = { mb: 1.5, fontSize: "0.875rem", fontWeight: 700, color: c.text };
  const part = { pt: 3, mt: 3, borderTop: `1px solid ${c.border}` };
  const actions = { display: "flex", justifyContent: "flex-end", mt: 2 };
  const identifiersChanged = product ? IDENTIFIER_TYPES.some((type) => identifiers[type].trim() !== identifiersOf(product)[type]) : false;

  return (
    <KitDialog
      open
      onClose={onClose}
      icon="description"
      maxWidth="md"
      title={product ? product.name : "Product details"}
      subtitle={product ? `${product.sku} · what the marketplaces are told about it` : undefined}
      actions={
        <MDButton variant="text" color="secondary" onClick={onClose}>
          Close
        </MDButton>
      }
    >
      {error && <InlineAlert sx={{ mb: 2.5 }}>{error}</InlineAlert>}
      {!product && !error && <StateBlock kind="loading" title="Loading the product" />}
      {product && (
        <Box>
          <Box sx={heading}>Details</Box>
          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2.5 }}>
            <MDInput label="Brand" fullWidth value={brand} onChange={(e: Change) => setBrand(e.target.value)} />
            <MDInput
              label="Category"
              fullWidth
              value={category}
              onChange={(e: Change) => setCategory(e.target.value)}
              helperText="Your own name for it. Each marketplace's settings say where it goes there."
            />
            <MDInput
              label="Description"
              fullWidth
              multiline
              minRows={3}
              value={description}
              onChange={(e: Change) => setDescription(e.target.value)}
              sx={{ gridColumn: "1 / -1" }}
            />
          </Box>
          <Box sx={actions}>
            <MDButton variant="gradient" color="info" disabled={!!busy} onClick={saveContent}>
              {busy === "content" ? "Saving…" : "Save details"}
            </MDButton>
          </Box>

          <Box sx={part}>
            <Box sx={heading}>Identifiers</Box>
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "repeat(3, 1fr)" }, gap: 2.5 }}>
              {IDENTIFIER_TYPES.map((type) => (
                <MDInput
                  key={type}
                  label={IDENTIFIER_LABELS[type]}
                  fullWidth
                  value={identifiers[type]}
                  onChange={(e: Change) => setIdentifiers({ ...identifiers, [type]: e.target.value })}
                />
              ))}
            </Box>
            <Box sx={{ mt: 1, fontSize: "0.8125rem", color: c.muted }}>
              The numbers printed on the product. Barcodes are 8, 10, 12, 13 or 14 digits; empty one to remove it.
            </Box>
            <Box sx={actions}>
              <MDButton variant="gradient" color="info" disabled={!!busy || !identifiersChanged} onClick={saveIdentifiers}>
                {busy === "identifiers" ? "Saving…" : "Save identifiers"}
              </MDButton>
            </Box>
          </Box>

          <Box sx={part}>
            <Box sx={heading}>Images</Box>
            {images.length === 0 && <Box sx={{ mb: 2, fontSize: "0.875rem", color: c.muted }}>No images yet. The first one added is the main image.</Box>}
            <Box sx={{ display: "grid", gap: 1.5, mb: 2 }}>
              {images.map((image, index) => (
                <Box key={image.id} sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
                  <Box
                    component="img"
                    src={image.url}
                    alt={image.altText ?? ""}
                    sx={{ flexShrink: 0, width: 48, height: 48, objectFit: "cover", borderRadius: "8px", border: `1px solid ${c.border}`, backgroundColor: c.surfaceAlt }}
                  />
                  <Box sx={{ flex: 1, minWidth: 0, fontSize: "0.8125rem", color: c.muted, overflowWrap: "anywhere" }}>
                    <Box component="span" sx={{ fontWeight: 500, color: c.text }}>
                      {index === 0 ? "Main" : `Gallery ${index}`}
                    </Box>{" "}
                    {image.url}
                  </Box>
                  <IconButton size="small" disabled={!!busy} onClick={() => removeImage(image.id)} aria-label={`Remove image ${index + 1}`} sx={{ color: c.muted }}>
                    <Icon fontSize="small">delete</Icon>
                  </IconButton>
                </Box>
              ))}
            </Box>
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                if (imageUrl.trim()) addImage();
              }}
              sx={{ display: "flex", alignItems: "flex-start", gap: 1.5 }}
            >
              <MDInput
                label="Image address"
                fullWidth
                value={imageUrl}
                onChange={(e: Change) => setImageUrl(e.target.value)}
                helperText="An https address the marketplaces can fetch the image from."
              />
              <MDButton type="submit" variant="outlined" color="info" disabled={!imageUrl.trim() || !!busy}>
                {busy === "image" ? "Adding…" : "Add"}
              </MDButton>
            </Box>
          </Box>
        </Box>
      )}
    </KitDialog>
  );
}
