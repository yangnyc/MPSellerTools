import { useCallback, useEffect, useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import Link from "@mui/material/Link";
import MDButton from "components/MDButton";
import DataTable from "examples/Tables/DataTable";
import {
  FilterTabs,
  Identity,
  PageHeader,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  formatDateTime,
  formatMoney,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { EbayApi, ListingsApi } from "../api/resources";
import {
  LISTING_STATUS_LABELS,
  SALES_CHANNEL_LABELS,
  type Listing,
  type ListingStatus,
  type Listings,
} from "../api/types";
import { AMAZON, EBAY, type Marketplace } from "../api/channels";
import { LISTING_STATUS_TONE } from "../lib/status";

type StatusFilter = "all" | ListingStatus;

const LISTING_STATUSES: ListingStatus[] = [0, 1, 2];

// "EBAY_US" → "eBay US"; a channel with no marketplace is just its own name.
// Amazon names its sites by an id rather than a region, so only the one this workspace sets up is spelled out.
function siteName(listing: Listing) {
  const channel = SALES_CHANNEL_LABELS[listing.channel];
  if (listing.marketplace === AMAZON.marketplaceCode) return `${channel} US`;
  const region = listing.marketplace?.replace(/^(EBAY|WALMART)_/, "").replaceAll("_", " ");
  return region ? `${channel} ${region}` : channel;
}

// The price in the currency the site asks it in, which need not be the catalog's.
function sitePrice(listing: Listing) {
  if (listing.price === null) return "—";
  if (!listing.currency) return formatMoney(listing.price);
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency: listing.currency }).format(listing.price);
  } catch {
    return `${listing.price.toFixed(2)} ${listing.currency}`;
  }
}

// Every posting, or with `marketplace` only that marketplace's: the same page sits in each marketplace's menu.
export default function ListingsPage({ marketplace }: { marketplace?: Marketplace }) {
  // eBay is the one site that can be read on request; the others report through the sync queue.
  const readsEbay = !marketplace || marketplace.kind === EBAY.kind;
  const where = marketplace?.name ?? "your marketplaces";
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [data, setData] = useState<Listings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  const fetchData = useCallback(() => {
    ListingsApi.list(marketplace?.kind)
      .then((value) => { setData(value); setError(null); })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load listings."))
      .finally(() => setLoading(false));
  }, [marketplace?.kind]);

  useEffect(fetchData, [fetchData]);

  // Listings come from the site itself, so refreshing them is the eBay product import.
  const refresh = async () => {
    setRefreshing(true);
    try {
      const result = await EbayApi.importProducts();
      notify(`Read from eBay: ${result.listings} posted.`, "success");
      fetchData();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Could not read from eBay.", "error");
    } finally {
      setRefreshing(false);
    }
  };

  const listings = data?.listings;

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = Listing & { site: string; statusLabel: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (listings ?? [])
      .filter((l) => statusFilter === "all" || l.status === statusFilter)
      .map((l) => ({ ...l, site: siteName(l), statusLabel: LISTING_STATUS_LABELS[l.status] }));

    const columns = [
      {
        Header: "Product",
        id: "product",
        // Name and SKU together, so searching and sorting cover both.
        accessor: (row: Row) => `${row.productName} ${row.productSku}`,
        Cell: ({ row }: CellProps) => <Identity name={row.original.productName} secondary={row.original.productSku} square />,
      },
      { Header: "Posted on", accessor: "site" },
      {
        Header: "Item number",
        accessor: "externalId",
        Cell: ({ row }: CellProps) =>
          row.original.url ? (
            <Link
              href={row.original.url}
              target="_blank"
              rel="noreferrer"
              aria-label={`Open ${row.original.productName} on ${row.original.site}`}
              sx={{ display: "inline-flex", alignItems: "center", gap: 0.5, fontFamily: "monospace", color: c.accent }}
            >
              {row.original.externalId}
              <Icon sx={{ fontSize: "0.875rem !important" }}>open_in_new</Icon>
            </Link>
          ) : (
            <Box component="span" sx={{ fontFamily: "monospace" }}>
              {row.original.externalId}
            </Box>
          ),
      },
      {
        Header: "Price",
        id: "price",
        accessor: (row: Row) => row.price ?? -1,
        align: "right" as const,
        Cell: ({ row }: CellProps) => sitePrice(row.original),
      },
      {
        Header: "Available",
        id: "available",
        accessor: (row: Row) => row.availableQuantity ?? -1,
        align: "right" as const,
        Cell: ({ row }: CellProps) => row.original.availableQuantity ?? "—",
      },
      {
        Header: "Sold",
        id: "sold",
        accessor: (row: Row) => row.soldQuantity ?? -1,
        align: "right" as const,
        Cell: ({ row }: CellProps) => row.original.soldQuantity ?? "—",
      },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill tone={LISTING_STATUS_TONE[row.original.status]} label={row.original.statusLabel} />
        ),
      },
    ];
    return { columns, rows };
  }, [listings, statusFilter, c]);

  const count = (status: ListingStatus) => listings?.filter((l) => l.status === status).length;
  const unitsSold = listings?.reduce((sum, l) => sum + (l.soldQuantity ?? 0), 0);

  const refreshButton = (size: "small" | "medium") => (
    <MDButton variant="gradient" color="info" size={size} disabled={refreshing} onClick={refresh} startIcon={<Icon>sync</Icon>}>
      {refreshing ? "Reading eBay…" : "Refresh from eBay"}
    </MDButton>
  );
  const connectButton = (
    <MDButton component={RouterLink} to={!marketplace || marketplace.kind === EBAY.kind ? "/ebay" : marketplace.path} variant="gradient" color="info" size="small">
      Set up {marketplace?.name ?? "eBay"}
    </MDButton>
  );

  return (
    <PageShell>
      <PageHeader
        icon="sell"
        title={marketplace ? `${marketplace.name} listings` : "Listings"}
        subtitle={
          data?.lastSyncedAtUtc ? (
            <span title={formatDateTime(data.lastSyncedAtUtc)}>
              Your products as posted on {where}, read {timeAgo(data.lastSyncedAtUtc)}.
            </span>
          ) : (
            `Your products as posted on ${where}.`
          )
        }
        actions={isTenantAdmin && readsEbay && data?.connected && refreshButton("medium")}
      />

      <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3, mb: 3 }}>
        <StatCard icon="storefront" tone="success" label="Live" value={count(0)} hint="On sale now" />
        <StatCard icon="production_quantity_limits" tone="warning" label="Out of stock" value={count(1)} hint="Posted, nothing left" />
        <StatCard icon="event_busy" tone="info" label="Ended" value={count(2)} hint="No longer on sale" />
        <StatCard icon="shopping_bag" tone="primary" label="Units sold" value={unitsSold} hint="Across these listings" />
      </Box>

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading listings" />}
        {error && (
          <StateBlock
            kind="error"
            title="Listings could not be loaded"
            message={error}
            action={
              <MDButton variant="outlined" color="info" size="small" onClick={fetchData}>
                Try again
              </MDButton>
            }
          />
        )}
        {!loading && !error && listings?.length === 0 && (
          <StateBlock
            icon="sell"
            title="Nothing posted yet"
            message={
              marketplace && !readsEbay
                ? data?.connected
                  ? `${marketplace.name} has not reported any of your products as on sale yet. A product shows up here once ${marketplace.name} confirms it.`
                  : isTenantAdmin
                    ? `Add ${marketplace.name} as a sales channel and the products it reports as on sale show up here.`
                    : `${marketplace.name} is not set up yet. A company admin can add it.`
                : !data?.connected
                ? isTenantAdmin
                  ? "Connect your eBay account and the products you have posted there show up here."
                  : "No e-commerce site is connected yet. A company admin can connect eBay."
                : isTenantAdmin
                  ? "eBay reported no posted items. Only items listed through eBay's inventory tools can be read, not listings made by hand on the eBay site."
                  : "eBay reported no posted items the last time it was read."
            }
            action={isTenantAdmin && (!data?.connected ? connectButton : readsEbay && refreshButton("small"))}
          />
        )}
        {!loading && !error && (listings?.length ?? 0) > 0 && (
          <>
            <Box sx={{ px: 3, pt: 2.5 }}>
              <FilterTabs
                label="Filter by status"
                value={statusFilter}
                onChange={setStatusFilter}
                options={[
                  { value: "all", label: "All", count: listings?.length },
                  ...LISTING_STATUSES.map((status) => ({
                    value: status,
                    label: LISTING_STATUS_LABELS[status],
                    count: count(status),
                  })),
                ]}
              />
            </Box>
            <DataTable table={table} canSearch />
          </>
        )}
      </Section>
    </PageShell>
  );
}
