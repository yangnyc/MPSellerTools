import { readFileSync } from "node:fs";
import { join } from "node:path";

const repoRoot = join(import.meta.dirname, "..", "..", "..");

function parseCredentialsFile(path: string): Record<string, string> {
  const text = readFileSync(path, "utf-8");
  const result: Record<string, string> = {};
  for (const line of text.split(/\r?\n/)) {
    const match = line.match(/^([^:]+):\s*(.+)$/);
    if (match) result[match[1].trim()] = match[2].trim();
  }
  return result;
}

export function getPlatformAdminCredentials() {
  const creds = parseCredentialsFile(join(repoRoot, ".local/platform/dev-admin-credentials.txt"));
  return { email: creds.Email, password: creds.Password };
}

export function getTenantCredentials(slug: string) {
  const creds = parseCredentialsFile(join(repoRoot, `.local/tenants/${slug}/demo-credentials.txt`));
  const adminMatch = creds.TenantAdmin.match(/(.+) \/ (.+)/)!;
  const employeeMatch = creds.Employee.match(/(.+) \/ (.+)/)!;
  return {
    url: creds.URL,
    tenantAdmin: { email: adminMatch[1], password: adminMatch[2] },
    employee: { email: employeeMatch[1], password: employeeMatch[2] },
  };
}
