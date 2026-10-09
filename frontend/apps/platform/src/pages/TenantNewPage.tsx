import { useState } from "react";
import { Link as RouterLink, useNavigate } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, PageHeader, Section, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { TenantsApi } from "../api/resources";

const STEPS = [
  { title: "A database is created", text: "Each company gets its own database, separate from every other." },
  { title: "The workspace starts", text: "A dedicated instance is launched on a port the system picks." },
  { title: "Readiness is verified", text: "The login URL is shown only once the instance responds." },
  { title: "The administrator is invited", text: "They set their own password from a single-use link." },
];

// What the server would normalise the name to; offered as a starting slug.
const slugify = (value: string) =>
  value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

export default function TenantNewPage() {
  const { logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();
  const navigate = useNavigate();

  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  // Until the slug field is edited by hand, it follows the company name.
  const [slugEdited, setSlugEdited] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const submit = async () => {
    setError(null);
    if (!name.trim() || !slug.trim() || !email.trim()) {
      setError("Company name, slug and administrator email are required.");
      return;
    }
    setSubmitting(true);
    try {
      const result = await TenantsApi.create({ name, slug, initialAdminEmail: email, initialAdminPassword: password || undefined });
      notify(`Company creation started. ${email.trim()} can sign in to it as its administrator once it is Active.`, "success");
      navigate(`/tenants/${result.tenantId}`);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Failed to create company.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <PageShell>
      <PageHeader
        icon="add_business"
        title="Create company"
        subtitle="The system assigns the port and local login URL automatically — no server details needed."
        backTo="/tenants"
        backLabel="All tenants"
      />

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
        <Section icon="apartment" title="Company details" subtitle="The administrator's account is made with the company; nobody is sent an invitation.">
          <Box
            component="form"
            noValidate
            onSubmit={(e: React.FormEvent) => {
              e.preventDefault();
              submit();
            }}
          >
            {error && <InlineAlert sx={{ mb: 2.5 }}>{error}</InlineAlert>}
            <Box sx={{ display: "grid", gap: 2.5 }}>
              <MDInput
                label="Company name"
                fullWidth
                value={name}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                  setName(e.target.value);
                  if (!slugEdited) setSlug(slugify(e.target.value));
                }}
              />
              <MDInput
                label="Slug"
                fullWidth
                helperText="Lowercase letters, digits, hyphens only"
                FormHelperTextProps={{ sx: { color: c.muted } }}
                value={slug}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                  setSlug(e.target.value);
                  setSlugEdited(true);
                }}
              />
              <MDInput
                label="Initial administrator email"
                type="email"
                fullWidth
                value={email}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEmail(e.target.value)}
              />
              <MDInput
                label="Administrator password"
                type="password"
                fullWidth
                autoComplete="new-password"
                value={password}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPassword(e.target.value)}
                helperText="What the administrator signs in to this company with: at least 12 characters, with upper case, lower case and a digit. Pass it on to them; it cannot be shown again. Leave it empty only when this email already signs in to another company here: it then keeps that password."
                FormHelperTextProps={{ sx: { color: c.muted } }}
              />
            </Box>
            <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1, mt: 3 }}>
              <MDButton component={RouterLink} to="/tenants" variant="text" color="secondary">
                Cancel
              </MDButton>
              <MDButton
                type="submit"
                variant="gradient"
                color="info"
                disabled={submitting}
                startIcon={<Icon>rocket_launch</Icon>}
              >
                {submitting ? "Creating…" : "Create company"}
              </MDButton>
            </Box>
          </Box>
        </Section>

        <Section icon="auto_awesome" tone="primary" title="What happens next" subtitle="Runs in the background after you submit.">
          <Box component="ol" sx={{ display: "grid", gap: 2, listStyle: "none" }}>
            {STEPS.map((step, index) => (
              <Box key={step.title} component="li" sx={{ display: "flex", alignItems: "flex-start", gap: 1.5 }}>
                <Box
                  aria-hidden
                  sx={{
                    flexShrink: 0,
                    width: 24,
                    height: 24,
                    display: "grid",
                    placeItems: "center",
                    borderRadius: "50%",
                    fontSize: "0.75rem",
                    fontWeight: 700,
                    color: c.text,
                    border: `1px solid ${c.borderStrong}`,
                  }}
                >
                  {index + 1}
                </Box>
                <Box sx={{ lineHeight: 1.45 }}>
                  <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{step.title}</Box>
                  <Box sx={{ fontSize: "0.8125rem", color: c.muted }}>{step.text}</Box>
                </Box>
              </Box>
            ))}
          </Box>
        </Section>
      </Box>
    </PageShell>
  );
}
