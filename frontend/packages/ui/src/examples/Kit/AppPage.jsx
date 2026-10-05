// MP Seller Tools page kit — the frame around every signed-in page: the top
// bar (breadcrumb trail, appearance controls, account menu), the content
// column, and the footer. Auth state stays in each app (see context/index.jsx),
// so the signed-in user and the logout handler come in as props.

import { useState } from "react";

import PropTypes from "prop-types";

import { Link as RouterLink, useLocation } from "react-router-dom";

import Box from "@mui/material/Box";
import Divider from "@mui/material/Divider";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Tooltip from "@mui/material/Tooltip";

import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import Footer from "examples/Footer";
import { InitialsAvatar } from "examples/Kit/primitives";
import { roleLabel } from "examples/Kit/format";
import { useKit } from "examples/Kit/tokens";

import { useMaterialUIController, setMiniSidenav, setOpenConfigurator } from "context";

// Record ids in a path (/tenants/<guid>) read as "Details" in the trail.
const crumbLabel = (segment) => (/^[0-9a-f-]{16,}$/i.test(segment) ? "Details" : segment.replace(/-/g, " "));

function AppNavbar({ user, onLogout, consoleName, profileHref }) {
  const { c } = useKit();
  const [controller, dispatch] = useMaterialUIController();
  const { miniSidenav, openConfigurator, fixedNavbar } = controller;
  const [menuAnchor, setMenuAnchor] = useState(null);
  const segments = useLocation().pathname.split("/").filter(Boolean);

  const closeMenu = () => setMenuAnchor(null);

  const barButtonSx = {
    width: 36,
    height: 36,
    borderRadius: "10px",
    color: c.muted,
    border: `1px solid ${c.border}`,
    "&:hover": { color: c.text, backgroundColor: c.hover },
  };

  const menuItemSx = { gap: 1.25, minHeight: 40, borderRadius: "8px", fontSize: "0.875rem", color: c.text };

  return (
    <Box
      component="header"
      sx={{
        position: fixedNavbar ? "sticky" : "static",
        top: 12,
        zIndex: 1100,
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: 2,
        minHeight: 60,
        mb: 3,
        px: { xs: 1.5, md: 2 },
        borderRadius: "16px",
        backgroundColor: c.navbar,
        backdropFilter: "saturate(180%) blur(12px)",
        border: `1px solid ${c.border}`,
        boxShadow: c.shadow,
      }}
    >
      <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, minWidth: 0 }}>
        <IconButton
          aria-label={miniSidenav ? "Open navigation" : "Close navigation"}
          onClick={() => setMiniSidenav(dispatch, !miniSidenav)}
          sx={{ ...barButtonSx, display: { xs: "inline-flex", xl: "none" } }}
        >
          <Icon fontSize="small">{miniSidenav ? "menu" : "menu_open"}</Icon>
        </IconButton>
        <Box
          component="nav"
          aria-label="Breadcrumb"
          sx={{ display: "flex", alignItems: "center", gap: 0.75, minWidth: 0, fontSize: "0.875rem" }}
        >
          <Box
            component={RouterLink}
            to="/dashboard"
            sx={{ display: { xs: "none", sm: "inline" }, whiteSpace: "nowrap", color: c.muted, "&:hover": { color: c.accent } }}
          >
            {consoleName}
          </Box>
          {segments.map((segment, index) => {
            const isLast = index === segments.length - 1;
            return (
              <Box key={`${index}-${segment}`} sx={{ display: "flex", alignItems: "center", gap: 0.75, minWidth: 0 }}>
                <Box
                  component="span"
                  aria-hidden
                  sx={{ display: { xs: index === 0 ? "none" : "inline", sm: "inline" }, color: c.subtle }}
                >
                  /
                </Box>
                <Box
                  {...(isLast
                    ? { "aria-current": "page" }
                    : { component: RouterLink, to: `/${segments.slice(0, index + 1).join("/")}` })}
                  sx={{
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                    textTransform: "capitalize",
                    fontWeight: isLast ? 700 : 400,
                    color: isLast ? c.text : c.muted,
                    ...(!isLast && { "&:hover": { color: c.accent } }),
                  }}
                >
                  {crumbLabel(segment)}
                </Box>
              </Box>
            );
          })}
        </Box>
      </Box>

      <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
        <Tooltip title="Display settings">
          <IconButton
            aria-label="Display settings"
            onClick={() => setOpenConfigurator(dispatch, !openConfigurator)}
            sx={barButtonSx}
          >
            <Icon fontSize="small">settings</Icon>
          </IconButton>
        </Tooltip>

        <Box
          component="button"
          type="button"
          aria-label="Account menu"
          aria-haspopup="menu"
          aria-expanded={Boolean(menuAnchor)}
          onClick={(event) => setMenuAnchor(event.currentTarget)}
          sx={{
            display: "flex",
            alignItems: "center",
            gap: 1,
            p: 0.5,
            pr: { xs: 0.5, md: 1 },
            border: `1px solid ${c.border}`,
            borderRadius: "999px",
            cursor: "pointer",
            fontFamily: "inherit",
            textAlign: "left",
            background: "none",
            "&:hover": { backgroundColor: c.hover },
            "&:focus-visible": { outline: `2px solid ${c.accent}`, outlineOffset: 2 },
          }}
        >
          <InitialsAvatar name={user?.displayName || user?.email} size={32} />
          <Box sx={{ display: { xs: "none", md: "block" }, maxWidth: 160, lineHeight: 1.25 }}>
            <Box
              sx={{
                overflow: "hidden",
                textOverflow: "ellipsis",
                whiteSpace: "nowrap",
                fontSize: "0.8125rem",
                fontWeight: 700,
                color: c.text,
              }}
            >
              {user?.displayName || user?.email}
            </Box>
            <Box sx={{ fontSize: "0.6875rem", color: c.muted }}>{roleLabel(user?.roles?.[0] ?? "")}</Box>
          </Box>
          <Icon sx={{ display: { xs: "none", md: "inline-block" }, fontSize: "1.125rem !important", color: c.subtle }}>
            expand_more
          </Icon>
        </Box>
        <Menu
          anchorEl={menuAnchor}
          open={Boolean(menuAnchor)}
          onClose={closeMenu}
          anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
          transformOrigin={{ vertical: "top", horizontal: "right" }}
          PaperProps={{
            sx: {
              mt: 1,
              minWidth: 240,
              p: 0.5,
              borderRadius: "14px",
              backgroundColor: c.surface,
              backgroundImage: "none",
              border: `1px solid ${c.border}`,
              boxShadow: c.shadow,
            },
          }}
        >
          <Box sx={{ px: 1.5, py: 1 }}>
            <Box sx={{ fontSize: "0.875rem", fontWeight: 700, color: c.text }}>{user?.displayName}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted, overflowWrap: "anywhere" }}>{user?.email}</Box>
          </Box>
          <Divider sx={{ my: 0.5 }} />
          <MenuItem component={RouterLink} to={profileHref} onClick={closeMenu} sx={menuItemSx}>
            <Icon fontSize="small">person</Icon>
            Profile
          </MenuItem>
          <MenuItem
            onClick={() => {
              closeMenu();
              onLogout();
            }}
            sx={menuItemSx}
          >
            <Icon fontSize="small">logout</Icon>
            Sign out
          </MenuItem>
        </Menu>
      </Box>
    </Box>
  );
}

AppNavbar.propTypes = {
  user: PropTypes.object,
  onLogout: PropTypes.func.isRequired,
  consoleName: PropTypes.string.isRequired,
  profileHref: PropTypes.string.isRequired,
};

export function AppPage({ user, onLogout, consoleName, profileHref = "/profile", children }) {
  return (
    <DashboardLayout>
      <AppNavbar user={user} onLogout={onLogout} consoleName={consoleName} profileHref={profileHref} />
      <Box component="main" sx={{ minHeight: "calc(100vh - 220px)", pb: 3 }}>
        {children}
      </Box>
      <Footer />
    </DashboardLayout>
  );
}

AppPage.propTypes = {
  user: PropTypes.object,
  onLogout: PropTypes.func.isRequired,
  consoleName: PropTypes.string.isRequired,
  profileHref: PropTypes.string,
  children: PropTypes.node,
};
