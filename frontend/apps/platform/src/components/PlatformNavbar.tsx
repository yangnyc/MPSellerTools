import { useState, type MouseEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import AppBar from "@mui/material/AppBar";
import Toolbar from "@mui/material/Toolbar";
import IconButton from "@mui/material/IconButton";
import InputBase from "@mui/material/InputBase";
import Menu from "@mui/material/Menu";
import Icon from "@mui/material/Icon";
import Button from "@mui/material/Button";
import Box from "@mui/material/Box";
import Breadcrumbs from "examples/Breadcrumbs";
import NotificationItem from "examples/Items/NotificationItem";
import { useMaterialUIController, setMiniSidenav, setOpenConfigurator } from "context";

// Colors lifted from Supabase's own navbar (https://www.navbar.gallery/navbar/supabase):
// near-black bar, muted gray secondary text/icons, and the brand green as the
// one CTA accent — everything else stays neutral so the green reads as "action".
const SB_BG = "#1c1c1c";
const SB_BORDER = "rgba(255,255,255,0.08)";
const SB_TEXT = "#ededed";
const SB_MUTED = "#a0a0a0";
const SB_GREEN = "#3ecf8e";
const SB_GREEN_HOVER = "#37b980";

const iconButtonSx = {
  border: `1px solid ${SB_BORDER}`,
  borderRadius: "8px",
  "&:hover": { backgroundColor: "rgba(255,255,255,0.06)" },
};

interface PlatformNavbarProps {
  onLogout?: () => void;
  profileHref?: string;
}

export default function PlatformNavbar({ onLogout, profileHref = "/profile" }: PlatformNavbarProps) {
  const [controller, dispatch] = useMaterialUIController();
  const { miniSidenav, openConfigurator } = controller;
  const [openMenu, setOpenMenu] = useState<HTMLElement | null>(null);
  const route = useLocation().pathname.split("/").slice(1);

  const handleMiniSidenav = () => setMiniSidenav(dispatch, !miniSidenav);
  const handleConfiguratorOpen = () => setOpenConfigurator(dispatch, !openConfigurator);
  const handleOpenMenu = (event: MouseEvent<HTMLButtonElement>) => setOpenMenu(event.currentTarget);
  const handleCloseMenu = () => setOpenMenu(null);

  return (
    <AppBar
      position="sticky"
      elevation={0}
      sx={{
        top: 0,
        mb: 3,
        backgroundColor: SB_BG,
        backgroundImage: "none",
        borderRadius: 0,
        borderBottom: `1px solid ${SB_BORDER}`,
      }}
    >
      <Toolbar
        sx={{
          minHeight: 64,
          px: { xs: 2, md: 3 },
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 2,
        }}
      >
        <Box sx={{ display: "flex", alignItems: "center", gap: 2, minWidth: 0 }}>
          <Box
            sx={{
              width: 28,
              height: 28,
              flexShrink: 0,
              borderRadius: "8px",
              backgroundColor: SB_GREEN,
              display: "grid",
              placeItems: "center",
            }}
          >
            <Icon sx={{ fontSize: 18, color: SB_BG }}>bolt</Icon>
          </Box>
          <Breadcrumbs icon="home" title={route[route.length - 1] || "dashboard"} route={route} light />
        </Box>

        <Box sx={{ display: "flex", alignItems: "center", gap: 1.25 }}>
          <Box
            sx={{
              display: { xs: "none", md: "flex" },
              alignItems: "center",
              gap: 1,
              minWidth: 200,
              px: 1.5,
              py: 0.5,
              borderRadius: "8px",
              border: `1px solid ${SB_BORDER}`,
              backgroundColor: "rgba(255,255,255,0.06)",
            }}
          >
            <Icon sx={{ fontSize: 18, color: SB_MUTED }}>search</Icon>
            <InputBase
              type="search"
              placeholder="Search..."
              autoComplete="off"
              name="platform-search"
              sx={{
                flex: 1,
                fontSize: 14,
                color: SB_TEXT,
                "& input::placeholder": { color: SB_MUTED, opacity: 1 },
              }}
            />
          </Box>

          <IconButton component={Link} to={profileHref} size="small" sx={iconButtonSx}>
            <Icon sx={{ color: SB_MUTED }}>account_circle</Icon>
          </IconButton>

          <IconButton
            size="small"
            onClick={handleMiniSidenav}
            sx={{ ...iconButtonSx, display: { xs: "inline-flex", xl: "none" } }}
          >
            <Icon sx={{ color: SB_MUTED }}>{miniSidenav ? "menu_open" : "menu"}</Icon>
          </IconButton>

          <IconButton size="small" onClick={handleConfiguratorOpen} sx={iconButtonSx}>
            <Icon sx={{ color: SB_MUTED }}>settings</Icon>
          </IconButton>

          <IconButton
            size="small"
            onClick={handleOpenMenu}
            aria-controls="notification-menu"
            aria-haspopup="true"
            sx={iconButtonSx}
          >
            <Icon sx={{ color: SB_MUTED }}>notifications</Icon>
          </IconButton>
          <Menu anchorEl={openMenu} open={Boolean(openMenu)} onClose={handleCloseMenu} sx={{ mt: 2 }}>
            <NotificationItem icon={<Icon>info</Icon>} title="No new notifications" onClick={handleCloseMenu} />
          </Menu>

          <Button
            onClick={() => onLogout?.()}
            disableElevation
            sx={{
              backgroundColor: SB_GREEN,
              color: SB_BG,
              fontWeight: 600,
              textTransform: "none",
              borderRadius: "999px",
              px: 2.5,
              "&:hover": { backgroundColor: SB_GREEN_HOVER },
            }}
          >
            Log Out
          </Button>
        </Box>
      </Toolbar>
    </AppBar>
  );
}
