// MP Seller Tools page kit — display formatting shared by both apps.

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
const relative = new Intl.RelativeTimeFormat("en", { numeric: "auto" });

export const formatMoney = (value) => money.format(value);

export const formatDateTime = (iso) => dateTime.format(new Date(iso));

const units = [
  ["year", 365 * 24 * 3600],
  ["month", 30 * 24 * 3600],
  ["day", 24 * 3600],
  ["hour", 3600],
  ["minute", 60],
];

export function timeAgo(iso) {
  const seconds = Math.round((new Date(iso).getTime() - Date.now()) / 1000);
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) {
      return relative.format(Math.round(seconds / size), unit);
    }
  }
  return "just now";
}

const roleLabels = {
  PlatformAdmin: "Platform admin",
  TenantAdmin: "Company admin",
  Employee: "Employee",
};

export const roleLabel = (role) => roleLabels[role] ?? role;

// Saves rows as a CSV file. A cell that a spreadsheet would run as a formula
// (one starting with = + - or @) is prefixed with a quote so it stays text.
export function downloadCsv(filename, headers, rows) {
  const cell = (value) => {
    const text = value === null || value === undefined ? "" : String(value);
    const safe = /^[=+\-@]/.test(text) ? `'${text}` : text;
    return `"${safe.replace(/"/g, '""')}"`;
  };
  const csv = [headers, ...rows].map((row) => row.map(cell).join(",")).join("\r\n");
  // The byte-order mark makes Excel read the file as UTF-8.
  const url = URL.createObjectURL(new Blob(["\uFEFF", csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

export function initialsOf(name) {
  const parts = (name ?? "").trim().split(/[\s@._-]+/).filter(Boolean);
  if (parts.length === 0) return "?";
  return (parts[0][0] + (parts[1]?.[0] ?? "")).toUpperCase();
}
