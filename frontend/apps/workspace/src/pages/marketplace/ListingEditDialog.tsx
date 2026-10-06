import { useState } from "react";
import Box from "@mui/material/Box";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, KitDialog, formatMoney, useKit } from "examples/Kit";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { ChannelListingsApi, type ChannelListing, type ListingIssue, type ListingPreview, type Marketplace } from "../../api/channels";

type Change = React.ChangeEvent<HTMLInputElement>;

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);

// One listing's own settings on a marketplace: what it overrides of the
// product, and checks of what the marketplace would make of it. Mounted
// afresh per listing (the page keys it by id).
export default function ListingEditDialog({
  listing,
  marketplace,
  onClose,
  onSaved,
}: {
  listing: ChannelListing;
  marketplace: Marketplace;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { c } = useKit();
  const { notify } = useSnackbar();
  const name = marketplace.name;

  const [title, setTitle] = useState(listing.contentOverrides.title?.value ?? "");
  const [price, setPrice] = useState(listing.priceOverride === null ? "" : String(listing.priceOverride));
  const [cap, setCap] = useState(listing.quantityCap === null ? "" : String(listing.quantityCap));
  const [category, setCategory] = useState(listing.externalCategoryId ?? "");
  const [catalogItemId, setCatalogItemId] = useState(listing.references.CatalogItem ?? "");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // null until a check was asked for; an empty list is a listing that is ready.
  const [issues, setIssues] = useState<ListingIssue[] | null>(null);
  const [preview, setPreview] = useState<ListingPreview | null>(null);

  const run = async (key: string, action: () => Promise<void>) => {
    setBusy(key);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(message(err, "That did not work."));
    } finally {
      setBusy(null);
    }
  };

  // Saves first, so a check or a preview is always of what is on the screen.
  const save = async () => {
    const priceValue = price.trim() === "" ? null : Number(price);
    const capValue = cap.trim() === "" ? null : Number(cap);
    if (priceValue !== null && !(priceValue >= 0)) throw new ApiError(400, "The price is a number, 0 or more.");
    if (capValue !== null && !(Number.isInteger(capValue) && capValue >= 0)) throw new ApiError(400, "The quantity cap is a whole number, 0 or more.");
    await ChannelListingsApi.save(listing, {
      title: title.trim() || null,
      priceOverride: priceValue,
      quantityCap: capValue,
      externalCategoryId: category.trim() || null,
      existingCatalogItemId: catalogItemId.trim(),
    });
    onSaved();
  };

  const saveAndClose = () =>
    run("save", async () => {
      await save();
      notify(`${listing.sellerSku} saved.`, "success");
      onClose();
    });

  const check = () =>
    run("check", async () => {
      await save();
      setPreview(null);
      setIssues((await ChannelListingsApi.validate(listing.id)).issues);
    });

  const showPreview = () =>
    run("preview", async () => {
      await save();
      const built = await ChannelListingsApi.preview(listing.id);
      setIssues(built.issues);
      setPreview(built);
    });

  const hint = { fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted };

  return (
    <KitDialog
      open
      onClose={onClose}
      onSubmit={saveAndClose}
      icon="edit"
      maxWidth="md"
      title={`${listing.effectiveTitle ?? listing.sellerSku} on ${name}`}
      subtitle={`${listing.sellerSku} · leave a field empty to follow the product`}
      actions={
        <>
          <MDButton variant="text" color="secondary" onClick={onClose}>
            Cancel
          </MDButton>
          <MDButton variant="outlined" color="info" disabled={!!busy} onClick={showPreview}>
            {busy === "preview" ? "Building…" : "Preview"}
          </MDButton>
          <MDButton variant="outlined" color="info" disabled={!!busy} onClick={check}>
            {busy === "check" ? "Checking…" : "Check"}
          </MDButton>
          <MDButton type="submit" variant="gradient" color="info" disabled={!!busy}>
            {busy === "save" ? "Saving…" : "Save"}
          </MDButton>
        </>
      }
    >
      {error && <InlineAlert sx={{ mb: 2.5 }}>{error}</InlineAlert>}
      {listing.hasPriceConflict && (
        <InlineAlert tone="warning" title="The price differs" sx={{ mb: 2.5 }}>
          {name} shows {listing.observedPrice === null ? "another price" : formatMoney(listing.observedPrice)}; here it is {formatMoney(listing.effectivePrice)}.
        </InlineAlert>
      )}

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2.5, pt: 0.5 }}>
        <MDInput label={`Title on ${name}`} fullWidth value={title} onChange={(e: Change) => setTitle(e.target.value)} sx={{ gridColumn: "1 / -1" }} />
        <MDInput
          label={`Price on ${name}`}
          type="number"
          fullWidth
          inputProps={{ min: 0, step: "0.01" }}
          value={price}
          onChange={(e: Change) => setPrice(e.target.value)}
          helperText="Empty: the product's own price."
        />
        <MDInput
          label="Quantity cap"
          type="number"
          fullWidth
          inputProps={{ min: 0, step: 1 }}
          value={cap}
          onChange={(e: Change) => setCap(e.target.value)}
          helperText={`The most ${name} is told is available. Empty: no cap.`}
        />
        <MDInput
          label={marketplace.categoryLabel}
          fullWidth
          value={category}
          onChange={(e: Change) => setCategory(e.target.value)}
          helperText="Empty: where the product's category is mapped to in this marketplace's settings."
        />
        {marketplace.catalogSearch && (
          <MDInput
            label="ASIN"
            fullWidth
            value={catalogItemId}
            onChange={(e: Change) => setCatalogItemId(e.target.value)}
            helperText={`Makes this an offer on an item ${name} already sells.`}
          />
        )}
      </Box>

      {issues && (
        <Box sx={{ mt: 3 }}>
          {issues.length === 0 ? (
            <InlineAlert tone="success" title={`Ready for ${name}`}>
              Nothing is missing. Saved as it is on the screen.
            </InlineAlert>
          ) : (
            <InlineAlert tone="warning" title={`${issues.length} ${issues.length === 1 ? "thing" : "things"} to fix before it can be published`}>
              <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
                {issues.map((issue) => (
                  <li key={`${issue.path}:${issue.code}`}>{issue.message}</li>
                ))}
              </Box>
            </InlineAlert>
          )}
        </Box>
      )}

      {preview && (
        <Box sx={{ mt: 3 }}>
          <Box sx={{ mb: 1, fontSize: "0.875rem", fontWeight: 700, color: c.text }}>What publishing would send</Box>
          <Box sx={{ ...hint, mb: 1.5 }}>
            {preview.liveWrites ? `Live writes are on: publishing sends this to ${name}.` : `Live writes are off: publishing builds this and sends nothing to ${name}.`}
          </Box>
          {preview.requests.length === 0 && <Box sx={hint}>Nothing would be sent.</Box>}
          {preview.requests.map((request) => (
            <Box key={`${request.method} ${request.url}`} sx={{ mb: 1.5 }}>
              <Box sx={{ fontFamily: "monospace", fontSize: "0.75rem", color: c.text, overflowWrap: "anywhere" }}>
                {request.method} {request.url}
              </Box>
              <Box
                component="pre"
                sx={{ m: 0, mt: 0.5, p: 1.5, maxHeight: 260, overflow: "auto", borderRadius: "8px", fontSize: "0.75rem", color: c.text, backgroundColor: c.surfaceAlt, border: `1px solid ${c.border}` }}
              >
                {JSON.stringify(request.body, null, 2)}
              </Box>
            </Box>
          ))}
        </Box>
      )}
    </KitDialog>
  );
}
