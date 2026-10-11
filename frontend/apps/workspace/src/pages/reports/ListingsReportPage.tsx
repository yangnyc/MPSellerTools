import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { PageHeader, Section, SimpleTable, StatCard, StateBlock, downloadCsv, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useLoaded } from "../../lib/useLoaded";
import { marketplaces } from "../../api/channels";
import { ReportsApi, type ListingsReport } from "../../api/tools";

type Channel = ListingsReport["channels"][number];
type Problem = ListingsReport["problems"][number];

const read = () => ReportsApi.listings();

// How the products stand on each sales channel, and which listings something is wrong with.
export default function ListingsReportPage() {
  const { c } = useKit();
  const { data, error, loading } = useLoaded<ListingsReport>(read, "The listings report could not be loaded.");

  const live = data?.channels.reduce((sum, channel) => sum + channel.live, 0) ?? 0;
  const rejected = data?.channels.reduce((sum, channel) => sum + channel.rejected, 0) ?? 0;
  const drafts = data?.channels.reduce((sum, channel) => sum + channel.drafts, 0) ?? 0;
  const pageOf = (kind: number) => marketplaces().find((m) => m.kind === kind)?.path;

  const exportCsv = () =>
    downloadCsv("listing-problems.csv", ["Sales channel", "SKU", "Problem"], (data?.problems ?? []).map((p) => [p.channel, p.sku, p.problem]));

  return (
    <PageShell>
      <PageHeader
        icon="sell"
        title="Listings report"
        subtitle="How your products stand on each sales channel, and which listings have something wrong with them."
        actions={
          <MDButton variant="outlined" color="info" disabled={!data || data.problems.length === 0} onClick={exportCsv} startIcon={<Icon>download</Icon>}>
            Export problems (CSV)
          </MDButton>
        }
      />

      {error && <StateBlock kind="error" title="The report could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Counting the listings" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="storefront" tone="success" label="On sale" value={live.toLocaleString()} hint="Listings the sales channels report as live" />
            <StatCard icon="edit_note" tone="info" label="Drafts" value={drafts.toLocaleString()} hint="Prepared, waiting to be published" />
            <StatCard icon="report" tone={rejected > 0 ? "error" : "success"} label="Turned down" value={rejected.toLocaleString()} hint="Refused by a sales channel" to="/sync" />
            <StatCard icon="visibility_off" tone={data.notListed > 0 ? "warning" : "success"} label="Not listed anywhere" value={data.notListed.toLocaleString()} hint={`of ${data.products.toLocaleString()} active products`} to="/products" />
          </Box>

          <Section flush icon="storefront" title="By sales channel" subtitle="A listing is counted by what the channel last reported about it.">
            <SimpleTable
              rows={data.channels}
              getRowId={(row: Channel) => row.accountId}
              emptyMessage="No sales channels yet. Add one from its menu to start listing products."
              columns={[
                {
                  key: "channel",
                  header: "Sales channel",
                  render: (row: Channel) => {
                    const path = pageOf(row.channel);
                    return path ? (
                      <Box component={RouterLink} to={path} sx={{ fontWeight: 500, color: c.text, "&:hover": { color: c.accent } }}>{row.name}</Box>
                    ) : (
                      <Box sx={{ fontWeight: 500, color: c.text }}>{row.name}</Box>
                    );
                  },
                },
                { key: "total", header: "Listings", align: "right", render: (row: Channel) => row.total.toLocaleString() },
                { key: "live", header: "Live", align: "right", render: (row: Channel) => row.live.toLocaleString() },
                { key: "processing", header: "Processing", align: "right", render: (row: Channel) => row.processing.toLocaleString() },
                { key: "drafts", header: "Drafts", align: "right", render: (row: Channel) => row.drafts.toLocaleString() },
                { key: "rejected", header: "Turned down", align: "right", render: (row: Channel) => row.rejected.toLocaleString() },
                { key: "offSale", header: "Off sale", align: "right", render: (row: Channel) => row.offSale.toLocaleString() },
                { key: "issues", header: "With problems", align: "right", render: (row: Channel) => row.withIssues.toLocaleString() },
              ]}
            />
          </Section>

          <Section
            flush
            icon="report_problem"
            tone={data.problems.length > 0 ? "warning" : "success"}
            title="Listings with a problem"
            subtitle={data.problems.length >= 500 ? "The first 500. Put these right and the rest follow." : "What stops each being published or kept on sale. Open the sales channel's products to put it right."}
          >
            <SimpleTable
              rows={data.problems}
              getRowId={(row: Problem) => row.listingId}
              emptyMessage="No listing has a problem recorded."
              columns={[
                { key: "channel", header: "Sales channel", render: (row: Problem) => row.channel },
                { key: "sku", header: "SKU", render: (row: Problem) => <Box sx={{ fontFamily: "monospace", fontSize: "0.8125rem" }}>{row.sku}</Box> },
                { key: "problem", header: "Problem", render: (row: Problem) => <Box sx={{ whiteSpace: "normal", overflowWrap: "anywhere", maxWidth: 560 }}>{row.problem}</Box> },
              ]}
            />
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
