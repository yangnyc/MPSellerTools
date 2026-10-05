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

export function initialsOf(name) {
  const parts = (name ?? "").trim().split(/[\s@._-]+/).filter(Boolean);
  if (parts.length === 0) return "?";
  return (parts[0][0] + (parts[1]?.[0] ?? "")).toUpperCase();
}
