/**
=========================================================
* Material Dashboard 2 React - v2.1.0
=========================================================

* Product Page: https://www.creative-tim.com/product/material-dashboard-react
* Copyright 2022 Creative Tim (https://www.creative-tim.com)

Coded by www.creative-tim.com

 =========================================================

* The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
*/

import { Fragment, useEffect, useState } from "react";

// react-router-dom components
import { useLocation, NavLink } from "react-router-dom";

// prop-types is a library for typechecking of props.
import PropTypes from "prop-types";

// @mui material components
import List from "@mui/material/List";
import Collapse from "@mui/material/Collapse";
import Checkbox from "@mui/material/Checkbox";
import Divider from "@mui/material/Divider";
import Link from "@mui/material/Link";
import Icon from "@mui/material/Icon";

// Material Dashboard 2 React components
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";

// Material Dashboard 2 React example components
import SidenavCollapse from "examples/Sidenav/SidenavCollapse";

// Custom styles for the Sidenav
import SidenavRoot from "examples/Sidenav/SidenavRoot";
import sidenavLogoLabel from "examples/Sidenav/styles/sidenav";
import sidenavTintsNeedingDarkText from "examples/Sidenav/sidenavTintContrast";

// Material Dashboard 2 React context
import {
  useMaterialUIController,
  setMiniSidenav,
} from "context";


function Sidenav({ color: _color = "info", brand = "", brandName, routes, pinnedGroups = [], onPinnedGroupsChange = null, ...rest }) {
  const [controller, dispatch] = useMaterialUIController();
  const { miniSidenav, whiteSidenav, sidenavTint, darkMode } = controller;
  const location = useLocation();
  const collapseName = location.pathname.split("/")[1];
  // Groups the user has opened or closed by hand; any other group is open
  // only while the current page is one of its sub-items.
  const [groupOpen, setGroupOpen] = useState({});

  // A pinned group stays open, and its arrow no longer closes it. Which
  // groups are pinned is the app's to keep (on the user's profile).
  const setGroupPinned = (key, pinned) => {
    const others = pinnedGroups.filter((item) => item !== key);
    // Unpinning closes the group; its arrow opens it again.
    setGroupOpen((groups) => ({ ...groups, [key]: pinned }));
    onPinnedGroupsChange(pinned ? [...others, key] : others);
  };
  const tintIsLightBackground = sidenavTint && sidenavTintsNeedingDarkText.includes(sidenavTint);

  // whiteSidenav is always a solid white background regardless of darkMode
  // (see SidenavRoot), so its text must always be dark. A sidenavTint is a
  // solid fill like whiteSidenav, but only some of the tint colors are light
  // enough to need dark text (see sidenavTintContrast.js).
  let textColor = "white";

  if (whiteSidenav || tintIsLightBackground) {
    textColor = "dark";
  }

  // MDTypographyRoot force-overrides color="dark" to white whenever the
  // app's global darkMode is on (sensible for the rest of the app, which
  // sits on a dark page background in dark mode) — but the sidenav can have
  // its own light background (whiteSidenav, or a light sidenavTint) even
  // while darkMode is on, so bypass that override with an explicit sx color
  // here instead of relying on the color prop alone.
  const textColorSx = textColor === "dark" ? { color: ({ palette }) => palette.dark.main } : undefined;

  // The MuiDivider-light class draws a white-based line (for dark
  // backgrounds); the default draws a dark-based line (for light
  // backgrounds) — see assets/theme(-dark)/components/divider.js. Follows
  // the same background reasoning as textColor above: whiteSidenav is
  // always a light background, and the default sidenav style is always a
  // dark background.
  const lightDivider = !whiteSidenav && !tintIsLightBackground;

  const closeSidenav = () => setMiniSidenav(dispatch, true);

  useEffect(() => {
    // A function that sets the mini state of the sidenav.
    function handleMiniSidenav() {
      setMiniSidenav(dispatch, window.innerWidth < 1200);
    }

    /**
     The event listener that's calling the handleMiniSidenav function when resizing the window.
    */
    window.addEventListener("resize", handleMiniSidenav);

    // Call the handleMiniSidenav function to set the state with the initial value.
    handleMiniSidenav();

    // Remove event listener on cleanup
    return () => window.removeEventListener("resize", handleMiniSidenav);
  }, [dispatch, location]);

  // Render all the routes from the routes.js (All the visible items on the Sidenav)
  const renderRoutes = routes.map(({ type, name, icon, title, noCollapse, key, href, route, collapse }) => {
    let returnValue;

    if (type === "collapse" && collapse) {
      // A group: a route with sub-items under `collapse` and no page of its own.
      // The current sub-item is the one whose route is the page or the closest
      // parent of it, so /tenants/42 still lights up the /tenants item while
      // /tenants/bulk lights up only its own.
      const current = collapse
        .filter((item) => location.pathname === item.route || location.pathname.startsWith(`${item.route}/`))
        .sort((a, b) => b.route.length - a.route.length)[0];
      const containsCurrent = Boolean(current);
      const pinned = pinnedGroups.includes(key);
      const open = pinned || (groupOpen[key] ?? containsCurrent);
      const toggle = () => setGroupOpen((groups) => ({ ...groups, [key]: !open }));
      const pinCheckbox = (
        <Checkbox
          size="small"
          checked={pinned}
          title={pinned ? "Unpin: let this menu close" : "Pin: keep this menu open"}
          // The checkbox sits inside the row that toggles the group.
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => event.stopPropagation()}
          onChange={(event) => setGroupPinned(key, event.target.checked)}
          slotProps={{ input: { "aria-label": `Keep ${name} open` } }}
          sx={{ p: 0.25 }}
        />
      );

      returnValue = (
        <Fragment key={key}>
          {pinned ? (
            // Not a button while pinned: there is nothing for the row to do,
            // and the checkbox inside it has to stay usable.
            <SidenavCollapse name={name} icon={icon} expandIcon="expand_less" trailing={pinCheckbox} inert />
          ) : (
            <SidenavCollapse
              name={name}
              icon={icon}
              active={containsCurrent && !open}
              expandIcon={open ? "expand_less" : "expand_more"}
              trailing={onPinnedGroupsChange ? pinCheckbox : null}
              role="button"
              tabIndex={0}
              aria-expanded={open}
              onClick={toggle}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  toggle();
                }
              }}
            />
          )}
          <Collapse in={open} unmountOnExit>
            <List disablePadding>
              {collapse.map((item) => (
                <NavLink key={item.key} to={item.route}>
                  <SidenavCollapse
                    name={item.name}
                    icon={item.icon}
                    active={item === current}
                    nested
                  />
                </NavLink>
              ))}
            </List>
          </Collapse>
        </Fragment>
      );
    } else if (type === "collapse") {
      returnValue = href ? (
        <Link
          href={href}
          key={key}
          target="_blank"
          rel="noreferrer"
          sx={{ textDecoration: "none" }}
        >
          <SidenavCollapse
            name={name}
            icon={icon}
            active={key === collapseName}
            noCollapse={noCollapse}
          />
        </Link>
      ) : (
        <NavLink key={key} to={route}>
          <SidenavCollapse name={name} icon={icon} active={key === collapseName} />
        </NavLink>
      );
    } else if (type === "title") {
      returnValue = (
        <MDTypography
          key={key}
          color={textColor}
          sx={textColorSx}
          display="block"
          variant="caption"
          fontWeight="bold"
          textTransform="uppercase"
          pl={3}
          mt={2}
          mb={1}
          ml={1}
        >
          {title}
        </MDTypography>
      );
    } else if (type === "divider") {
      // A solid hairline between groups of entries, inset like the entries
      // themselves. The theme's own divider fades out at both ends and is
      // too faint to read as a separator here, so its gradient is replaced.
      returnValue = (
        <Divider
          key={key}
          sx={({ palette, functions: { rgba, pxToRem } }) => {
            const onDarkBackground = darkMode ? palette.text.main : palette.white.main;
            return {
              height: pxToRem(1),
              margin: `${pxToRem(12)} ${pxToRem(16)}`,
              opacity: 1,
              backgroundImage: "none !important",
              backgroundColor: rgba(lightDivider ? onDarkBackground : palette.dark.main, lightDivider ? 0.45 : 0.25),
            };
          }}
        />
      );
    }
    return returnValue;
  });

  return (
    <SidenavRoot
      {...rest}
      variant="permanent"
      ownerState={{ whiteSidenav, sidenavTint, miniSidenav, darkMode }}
    >
      <MDBox pt={3} pb={1} px={4} textAlign="center">
        <MDBox
          display={{ xs: "block", xl: "none" }}
          position="absolute"
          top={0}
          right={0}
          p={1.625}
          onClick={closeSidenav}
          sx={{ cursor: "pointer" }}
        >
          <MDTypography variant="h6" color="secondary">
            <Icon sx={{ fontWeight: "bold" }}>close</Icon>
          </MDTypography>
        </MDBox>
        <MDBox component={NavLink} to="/" display="flex" alignItems="center">
          {brand && <MDBox component="img" src={brand} alt="Brand" width="2rem" />}
          <MDBox
            width={!brandName && "100%"}
            sx={(theme) => sidenavLogoLabel(theme, { miniSidenav })}
          >
            <MDTypography
              component="h6"
              variant="button"
              fontWeight="medium"
              color={textColor}
              sx={textColorSx}
            >
              {brandName}
            </MDTypography>
          </MDBox>
        </MDBox>
      </MDBox>
      <Divider className={lightDivider ? "MuiDivider-light" : undefined} />
      {/* Room under the last entry, so it clears the rounded corner and a phone's home bar. */}
      <List sx={{ pb: "calc(16px + env(safe-area-inset-bottom, 0px))" }}>{renderRoutes}</List>
    </SidenavRoot>
  );
}

// Typechecking props for the Sidenav
Sidenav.propTypes = {
  color: PropTypes.string,
  brand: PropTypes.string,
  brandName: PropTypes.string.isRequired,
  routes: PropTypes.arrayOf(PropTypes.object).isRequired,
  pinnedGroups: PropTypes.arrayOf(PropTypes.string),
  onPinnedGroupsChange: PropTypes.func,
};

export default Sidenav;
