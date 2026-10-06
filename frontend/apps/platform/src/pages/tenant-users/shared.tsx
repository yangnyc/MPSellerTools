import Box from "@mui/material/Box";
import MDInput from "components/MDInput";
import { InlineAlert, useKit } from "examples/Kit";
import type { TenantUser, UnavailableTenant } from "../../api/types";

// A link shown in place of the email that would carry it, for the admin to copy.
export function LinkBox({ link }: { link: string }) {
  const { c } = useKit();

  return (
    <Box
      sx={{
        p: 1.5,
        borderRadius: "10px",
        fontFamily: "monospace",
        fontSize: "0.8125rem",
        wordBreak: "break-all",
        color: c.text,
        backgroundColor: c.surfaceAlt,
        border: `1px solid ${c.border}`,
      }}
    >
      {link}
    </Box>
  );
}

// Companies missing from the list, so an absent user is not read as "no such user".
export function UnavailableAlert({ unavailable }: { unavailable: UnavailableTenant[] }) {
  if (unavailable.length === 0) {
    return null;
  }

  return (
    <InlineAlert tone="warning" title="Users of some companies are not shown" sx={{ mb: 2.5 }}>
      <ul style={{ margin: "0.25rem 0 0", paddingLeft: "1.25rem" }}>
        {unavailable.map((tenant) => (
          <li key={tenant.tenantId}>
            {tenant.tenantName}: {tenant.reason}
          </li>
        ))}
      </ul>
    </InlineAlert>
  );
}

export function CompanySelect({ users, value, onChange }: { users: TenantUser[]; value: string; onChange: (tenantId: string) => void }) {
  const companies = [...new Map(users.map((u) => [u.tenantId, u.tenantName])).entries()];

  return (
    <MDInput
      select
      label="Company"
      size="small"
      SelectProps={{ native: true }}
      value={value}
      onChange={(e: React.ChangeEvent<HTMLInputElement>) => onChange(e.target.value)}
    >
      <option value="all">Any company</option>
      {companies.map(([id, name]) => (
        <option key={id} value={id}>
          {name}
        </option>
      ))}
    </MDInput>
  );
}
