// MP Seller Tools page kit — layout and display primitives.
// See tokens.js for what the kit is and where its colours come from.

import PropTypes from "prop-types";

import { Link as RouterLink } from "react-router-dom";

import Box from "@mui/material/Box";
import Dialog from "@mui/material/Dialog";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Skeleton from "@mui/material/Skeleton";

import { useKit } from "examples/Kit/tokens";
import { initialsOf } from "examples/Kit/format";

const tonePropType = PropTypes.oneOf(["info", "primary", "success", "warning", "error", "neutral"]);

const asArray = (sx) => (Array.isArray(sx) ? sx : [sx]);

// A square tinted tile holding one icon — the kit's recurring visual anchor.
export function IconTile({ icon, tone = "info", size = 40, gradient = false }) {
  const kit = useKit();
  const { fg, bg } = kit.tone(tone);

  return (
    <Box
      aria-hidden
      sx={{
        flexShrink: 0,
        width: size,
        height: size,
        display: "grid",
        placeItems: "center",
        borderRadius: `${Math.round(size * 0.3)}px`,
        color: gradient ? kit.onAccent : fg,
        background: gradient ? kit.accentGradient : bg,
      }}
    >
      <Icon sx={{ fontSize: `${Math.round(size * 0.52)}px !important` }}>{icon}</Icon>
    </Box>
  );
}

IconTile.propTypes = {
  icon: PropTypes.string.isRequired,
  tone: tonePropType,
  size: PropTypes.number,
  gradient: PropTypes.bool,
};

// The bordered card every page section sits on.
export function Surface({ children, sx = null, ...rest }) {
  const { c } = useKit();

  return (
    <Box
      {...rest}
      sx={[
        {
          minWidth: 0,
          backgroundColor: c.surface,
          border: `1px solid ${c.border}`,
          borderRadius: "16px",
          boxShadow: c.shadow,
        },
        ...asArray(sx),
      ]}
    >
      {children}
    </Box>
  );
}

Surface.propTypes = {
  children: PropTypes.node,
  sx: PropTypes.oneOfType([PropTypes.object, PropTypes.array, PropTypes.func]),
};

// A Surface with a titled header. `flush` drops the body padding so tables
// and lists can run edge to edge.
export function Section({ icon, tone = "info", title, subtitle, actions, children, flush = false, sx = null }) {
  const { c } = useKit();
  const hasHeader = Boolean(title || actions);

  return (
    <Surface sx={[{ overflow: "hidden" }, ...asArray(sx)]}>
      {hasHeader && (
        <Box
          sx={{
            display: "flex",
            flexWrap: "wrap",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 2,
            px: 3,
            py: 2.25,
            borderBottom: children ? `1px solid ${c.border}` : "none",
          }}
        >
          <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, minWidth: 0 }}>
            {icon && <IconTile icon={icon} tone={tone} size={36} />}
            <Box sx={{ minWidth: 0 }}>
              <Box component="h2" sx={{ fontSize: "1rem", fontWeight: 700, lineHeight: 1.4, color: c.text }}>
                {title}
              </Box>
              {subtitle && (
                <Box sx={{ fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted }}>{subtitle}</Box>
              )}
            </Box>
          </Box>
          {actions && <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1 }}>{actions}</Box>}
        </Box>
      )}
      {children && <Box sx={flush ? null : { p: 3 }}>{children}</Box>}
    </Surface>
  );
}

Section.propTypes = {
  icon: PropTypes.string,
  tone: tonePropType,
  title: PropTypes.node,
  subtitle: PropTypes.node,
  actions: PropTypes.node,
  children: PropTypes.node,
  flush: PropTypes.bool,
  sx: PropTypes.oneOfType([PropTypes.object, PropTypes.array, PropTypes.func]),
};

// The title block at the top of every non-dashboard page.
export function PageHeader({ icon, title, subtitle, actions, backTo, backLabel = "Back" }) {
  const { c } = useKit();

  return (
    <Box sx={{ mb: 3 }}>
      {backTo && (
        <Box
          component={RouterLink}
          to={backTo}
          sx={{
            display: "inline-flex",
            alignItems: "center",
            gap: 0.5,
            mb: 1.5,
            fontSize: "0.8125rem",
            fontWeight: 500,
            color: c.muted,
            "&:hover": { color: c.accent },
          }}
        >
          <Icon sx={{ fontSize: "1rem !important" }}>arrow_back</Icon>
          {backLabel}
        </Box>
      )}
      <Box
        sx={{
          display: "flex",
          flexWrap: "wrap",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 2,
        }}
      >
        <Box sx={{ display: "flex", alignItems: "center", gap: 2, minWidth: 0 }}>
          {icon && <IconTile icon={icon} size={48} gradient />}
          <Box sx={{ minWidth: 0 }}>
            <Box
              component="h1"
              sx={{ fontSize: "1.5rem", fontWeight: 700, lineHeight: 1.3, letterSpacing: "-0.01em", color: c.text }}
            >
              {title}
            </Box>
            {subtitle && (
              <Box sx={{ mt: 0.25, fontSize: "0.875rem", lineHeight: 1.5, color: c.muted }}>{subtitle}</Box>
            )}
          </Box>
        </Box>
        {actions && <Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1 }}>{actions}</Box>}
      </Box>
    </Box>
  );
}

PageHeader.propTypes = {
  icon: PropTypes.string,
  title: PropTypes.node.isRequired,
  subtitle: PropTypes.node,
  actions: PropTypes.node,
  backTo: PropTypes.string,
  backLabel: PropTypes.string,
};

// The dark banner that opens each dashboard — in the default theme, the
// login pages' brand panel carried inside the app. Keeps its theme's own
// palette in light and dark mode.
export function Hero({ eyebrow, title, subtitle, actions, children }) {
  const { hero } = useKit();

  return (
    <Box
      sx={{
        position: "relative",
        overflow: "hidden",
        mb: 3,
        p: { xs: 3, md: 4 },
        borderRadius: "20px",
        color: "#fff",
        background: hero.background,
        border: `1px solid ${hero.border}`,
        "&::before": {
          content: '""',
          position: "absolute",
          inset: 0,
          backgroundImage: `linear-gradient(${hero.grid} 1px, transparent 1px), linear-gradient(90deg, ${hero.grid} 1px, transparent 1px)`,
          backgroundSize: "44px 44px",
          maskImage: "linear-gradient(90deg, transparent 20%, #000 100%)",
          WebkitMaskImage: "linear-gradient(90deg, transparent 20%, #000 100%)",
          pointerEvents: "none",
        },
      }}
    >
      <Box
        sx={{
          position: "relative",
          display: "flex",
          flexWrap: "wrap",
          alignItems: "flex-end",
          justifyContent: "space-between",
          gap: 3,
        }}
      >
        <Box sx={{ minWidth: 0, maxWidth: 640 }}>
          {eyebrow && (
            <Box
              sx={{
                mb: 1,
                fontSize: "0.75rem",
                fontWeight: 700,
                letterSpacing: "0.08em",
                textTransform: "uppercase",
                color: hero.eyebrow,
              }}
            >
              {eyebrow}
            </Box>
          )}
          <Box
            component="h1"
            sx={{ fontSize: { xs: "1.5rem", md: "1.875rem" }, fontWeight: 700, lineHeight: 1.2, color: "#fff" }}
          >
            {title}
          </Box>
          {subtitle && (
            <Box sx={{ mt: 1, fontSize: "0.9375rem", lineHeight: 1.6, color: hero.subtitle }}>
              {subtitle}
            </Box>
          )}
        </Box>
        {actions && <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1 }}>{actions}</Box>}
      </Box>
      {children && <Box sx={{ position: "relative", mt: 3 }}>{children}</Box>}
    </Box>
  );
}

Hero.propTypes = {
  eyebrow: PropTypes.node,
  title: PropTypes.node.isRequired,
  subtitle: PropTypes.node,
  actions: PropTypes.node,
  children: PropTypes.node,
};

// One headline number. `value` of null/undefined renders a loading skeleton;
// `progress` (0–1) draws a thin bar under the number; `to` makes the whole
// card a link.
export function StatCard({ icon, tone = "info", label, value, hint, progress, to }) {
  const kit = useKit();
  const { c } = kit;
  const linkProps = to ? { component: RouterLink, to } : {};

  return (
    <Surface
      {...linkProps}
      sx={{
        display: "block",
        height: "100%",
        p: 2.5,
        transition: "border-color 150ms ease, transform 150ms ease",
        ...(to && { "&:hover": { borderColor: kit.tone(tone).solid, transform: "translateY(-2px)" } }),
      }}
    >
      <Box sx={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 2 }}>
        <Box sx={{ minWidth: 0 }}>
          <Box sx={{ fontSize: "0.8125rem", fontWeight: 500, color: c.muted }}>{label}</Box>
          <Box sx={{ mt: 0.5, fontSize: "1.875rem", fontWeight: 700, lineHeight: 1.2, color: c.text }}>
            {value === null || value === undefined ? <Skeleton width={64} /> : value}
          </Box>
        </Box>
        <IconTile icon={icon} tone={tone} size={44} />
      </Box>
      {typeof progress === "number" && (
        <Box sx={{ mt: 1.5, height: 6, borderRadius: 3, backgroundColor: c.hover, overflow: "hidden" }}>
          <Box
            sx={{
              width: `${Math.round(Math.min(Math.max(progress, 0), 1) * 100)}%`,
              height: "100%",
              borderRadius: 3,
              backgroundColor: kit.tone(tone).solid,
              transition: "width 300ms ease",
            }}
          />
        </Box>
      )}
      {hint && <Box sx={{ mt: 1.25, fontSize: "0.8125rem", color: c.muted }}>{hint}</Box>}
    </Surface>
  );
}

StatCard.propTypes = {
  icon: PropTypes.string.isRequired,
  tone: tonePropType,
  label: PropTypes.node.isRequired,
  value: PropTypes.node,
  hint: PropTypes.node,
  progress: PropTypes.number,
  to: PropTypes.string,
};

// Soft status badge with a leading dot; `pulse` animates the dot for
// in-flight states (provisioning, running).
export function StatusPill({ tone = "neutral", label, pulse = false }) {
  const { fg, bg, solid } = useKit().tone(tone);

  return (
    <Box
      component="span"
      sx={{
        display: "inline-flex",
        alignItems: "center",
        gap: 0.75,
        px: 1.25,
        py: 0.375,
        borderRadius: "999px",
        fontSize: "0.75rem",
        fontWeight: 500,
        lineHeight: 1.5,
        whiteSpace: "nowrap",
        color: fg,
        backgroundColor: bg,
        "@keyframes kitPulse": { "0%, 100%": { opacity: 1 }, "50%": { opacity: 0.3 } },
      }}
    >
      <Box
        component="span"
        sx={{
          width: 6,
          height: 6,
          borderRadius: "50%",
          backgroundColor: solid,
          animation: pulse ? "kitPulse 1.4s ease-in-out infinite" : "none",
          "@media (prefers-reduced-motion: reduce)": { animation: "none" },
        }}
      />
      {label}
    </Box>
  );
}

StatusPill.propTypes = {
  tone: tonePropType,
  label: PropTypes.node.isRequired,
  pulse: PropTypes.bool,
};

// Segmented filter. Each option: { value, label, count? }.
export function FilterTabs({ value, onChange, options, label = "Filter" }) {
  const { c } = useKit();

  return (
    <Box
      role="group"
      aria-label={label}
      sx={{
        display: "inline-flex",
        flexWrap: "wrap",
        gap: 0.5,
        p: 0.5,
        borderRadius: "12px",
        backgroundColor: c.surfaceAlt,
        border: `1px solid ${c.border}`,
      }}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <Box
            key={String(option.value)}
            component="button"
            type="button"
            aria-pressed={active}
            onClick={() => onChange(option.value)}
            sx={{
              display: "inline-flex",
              alignItems: "center",
              gap: 0.75,
              px: 1.5,
              py: 0.625,
              border: "none",
              borderRadius: "8px",
              cursor: "pointer",
              fontFamily: "inherit",
              fontSize: "0.8125rem",
              fontWeight: 500,
              color: active ? c.text : c.muted,
              background: active ? c.surface : "none",
              boxShadow: active ? c.shadow : "none",
              transition: "background-color 120ms ease, color 120ms ease",
              "&:hover": { color: c.text },
              "&:focus-visible": { outline: `2px solid ${c.accent}`, outlineOffset: 1 },
            }}
          >
            {option.label}
            {typeof option.count === "number" && (
              <Box component="span" sx={{ fontSize: "0.75rem", color: c.subtle }}>
                {option.count}
              </Box>
            )}
          </Box>
        );
      })}
    </Box>
  );
}

FilterTabs.propTypes = {
  value: PropTypes.any,
  onChange: PropTypes.func.isRequired,
  options: PropTypes.arrayOf(PropTypes.object).isRequired,
  label: PropTypes.string,
};

// Loading, empty, and error states for a section body.
export function StateBlock({ kind = "empty", icon, title, message, action }) {
  const { c } = useKit();

  if (kind === "loading") {
    return (
      <Box role="status" aria-label={title ?? "Loading"} sx={{ p: 3 }}>
        {[72, 88, 64, 80].map((width) => (
          <Skeleton key={width} height={28} width={`${width}%`} sx={{ mb: 1 }} />
        ))}
      </Box>
    );
  }

  const isError = kind === "error";

  return (
    <Box
      role={isError ? "alert" : undefined}
      sx={{ display: "flex", flexDirection: "column", alignItems: "center", textAlign: "center", px: 3, py: 6 }}
    >
      <IconTile icon={icon ?? (isError ? "error_outline" : "inbox")} tone={isError ? "error" : "neutral"} size={52} />
      <Box sx={{ mt: 2, fontSize: "1rem", fontWeight: 700, color: c.text }}>
        {title ?? (isError ? "Something went wrong" : "Nothing here yet")}
      </Box>
      {message && <Box sx={{ mt: 0.5, maxWidth: 420, fontSize: "0.875rem", color: c.muted }}>{message}</Box>}
      {action && <Box sx={{ mt: 2.5 }}>{action}</Box>}
    </Box>
  );
}

StateBlock.propTypes = {
  kind: PropTypes.oneOf(["loading", "empty", "error"]),
  icon: PropTypes.string,
  title: PropTypes.node,
  message: PropTypes.node,
  action: PropTypes.node,
};

// Inline message box for form errors, notices, and explanations.
export function InlineAlert({ tone = "error", icon, title, children, action, sx = null }) {
  const { fg, bg, solid } = useKit().tone(tone);
  const defaultIcons = { error: "error_outline", warning: "warning_amber", success: "check_circle", info: "info" };

  return (
    <Box
      role={tone === "error" ? "alert" : "status"}
      sx={[
        {
          display: "flex",
          alignItems: "flex-start",
          gap: 1.25,
          px: 1.75,
          py: 1.25,
          borderRadius: "12px",
          fontSize: "0.875rem",
          lineHeight: 1.5,
          color: fg,
          backgroundColor: bg,
          border: `1px solid ${solid}55`,
        },
        ...asArray(sx),
      ]}
    >
      <Icon sx={{ mt: "1px", fontSize: "1.25rem !important" }}>{icon ?? defaultIcons[tone] ?? "info"}</Icon>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        {title && <Box sx={{ fontWeight: 700 }}>{title}</Box>}
        {children}
      </Box>
      {action}
    </Box>
  );
}

InlineAlert.propTypes = {
  tone: tonePropType,
  icon: PropTypes.string,
  title: PropTypes.node,
  children: PropTypes.node,
  action: PropTypes.node,
  sx: PropTypes.oneOfType([PropTypes.object, PropTypes.array, PropTypes.func]),
};

// Initials on a fill picked from the name, so one person or company keeps
// the same colour everywhere.
export function InitialsAvatar({ name, size = 36, square = false }) {
  const { avatarFills, onAvatar } = useKit();
  const label = name ?? "";
  let hash = 0;
  for (let i = 0; i < label.length; i += 1) hash = (hash * 31 + label.charCodeAt(i)) % 997;

  return (
    <Box
      aria-hidden
      sx={{
        flexShrink: 0,
        width: size,
        height: size,
        display: "grid",
        placeItems: "center",
        borderRadius: square ? `${Math.round(size * 0.28)}px` : "50%",
        fontSize: `${Math.round(size * 0.38)}px`,
        fontWeight: 700,
        letterSpacing: "0.02em",
        color: onAvatar,
        background: avatarFills[hash % avatarFills.length],
      }}
    >
      {initialsOf(label)}
    </Box>
  );
}

InitialsAvatar.propTypes = {
  name: PropTypes.string,
  size: PropTypes.number,
  square: PropTypes.bool,
};

// Avatar with a name and an optional second line (email, slug, ...).
export function Identity({ name, secondary, size = 36, square = false }) {
  const { c } = useKit();

  return (
    <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, minWidth: 0 }}>
      <InitialsAvatar name={name} size={size} square={square} />
      <Box sx={{ minWidth: 0, lineHeight: 1.35 }}>
        <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{name}</Box>
        {secondary && <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{secondary}</Box>}
      </Box>
    </Box>
  );
}

Identity.propTypes = {
  name: PropTypes.string,
  secondary: PropTypes.node,
  size: PropTypes.number,
  square: PropTypes.bool,
};

// Label/value grid for read-only details. Each item: { label, value }.
export function DetailList({ items, columns = 2 }) {
  const { c } = useKit();

  return (
    <Box
      component="dl"
      sx={{
        display: "grid",
        gridTemplateColumns: { xs: "1fr", sm: `repeat(${columns}, minmax(0, 1fr))` },
        gap: 2.5,
      }}
    >
      {items.map((item) => (
        <Box key={item.label} sx={{ minWidth: 0 }}>
          <Box
            component="dt"
            sx={{
              mb: 0.5,
              fontSize: "0.6875rem",
              fontWeight: 700,
              letterSpacing: "0.06em",
              textTransform: "uppercase",
              color: c.subtle,
            }}
          >
            {item.label}
          </Box>
          <Box component="dd" sx={{ fontSize: "0.9375rem", color: c.text, overflowWrap: "anywhere" }}>
            {item.value}
          </Box>
        </Box>
      ))}
    </Box>
  );
}

DetailList.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  columns: PropTypes.number,
};

// Dialog with the kit's header (icon tile, title, subtitle) and a footer
// action bar. With `onSubmit` the body is a form, so Enter submits and the
// primary action should be a type="submit" button.
export function KitDialog({
  open,
  onClose,
  icon,
  tone = "info",
  title,
  subtitle,
  children,
  actions,
  maxWidth = "sm",
  onSubmit,
}) {
  const { c } = useKit();
  const formProps = onSubmit
    ? {
        component: "form",
        noValidate: true,
        onSubmit: (event) => {
          event.preventDefault();
          onSubmit();
        },
      }
    : {};

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth={maxWidth}
      PaperProps={{
        sx: {
          backgroundColor: c.surface,
          backgroundImage: "none",
          border: `1px solid ${c.border}`,
          borderRadius: "16px",
        },
      }}
    >
      <Box {...formProps}>
        <Box sx={{ display: "flex", alignItems: "flex-start", gap: 1.5, px: 3, pt: 3, pb: 2 }}>
          {icon && <IconTile icon={icon} tone={tone} size={40} />}
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Box component="h2" sx={{ fontSize: "1.125rem", fontWeight: 700, lineHeight: 1.4, color: c.text }}>
              {title}
            </Box>
            {subtitle && <Box sx={{ fontSize: "0.875rem", lineHeight: 1.5, color: c.muted }}>{subtitle}</Box>}
          </Box>
          <IconButton size="small" aria-label="Close" onClick={onClose} sx={{ color: c.muted }}>
            <Icon fontSize="small">close</Icon>
          </IconButton>
        </Box>
        {children && <Box sx={{ px: 3, pb: 3 }}>{children}</Box>}
        {actions && (
          <Box
            sx={{
              display: "flex",
              justifyContent: "flex-end",
              gap: 1,
              px: 3,
              py: 2,
              backgroundColor: c.surfaceAlt,
              borderTop: `1px solid ${c.border}`,
            }}
          >
            {actions}
          </Box>
        )}
      </Box>
    </Dialog>
  );
}

KitDialog.propTypes = {
  open: PropTypes.bool.isRequired,
  onClose: PropTypes.func.isRequired,
  icon: PropTypes.string,
  tone: tonePropType,
  title: PropTypes.node.isRequired,
  subtitle: PropTypes.node,
  children: PropTypes.node,
  actions: PropTypes.node,
  maxWidth: PropTypes.string,
  onSubmit: PropTypes.func,
};
