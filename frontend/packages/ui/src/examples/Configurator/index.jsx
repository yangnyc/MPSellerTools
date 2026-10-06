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

import { useState, useEffect, useRef } from "react";

// @mui material components
import Divider from "@mui/material/Divider";
import Switch from "@mui/material/Switch";
import IconButton from "@mui/material/IconButton";
import Icon from "@mui/material/Icon";

// Material Dashboard 2 React components
import MDBox from "components/MDBox";
import MDInput from "components/MDInput";
import MDTypography from "components/MDTypography";

// Custom styles for the Configurator
import ConfiguratorRoot from "examples/Configurator/ConfiguratorRoot";

// Material Dashboard 2 React context
import {
  useMaterialUIController,
  setOpenConfigurator,
  setWhiteSidenav,
  setSidenavTint,
  setFixedNavbar,
  setSidenavColor,
  setDarkMode,
  setThemeName,
  themeOptions,
} from "context";

function Configurator() {
  const [controller, dispatch] = useMaterialUIController();
  const {
    openConfigurator,
    fixedNavbar,
    sidenavColor,
    whiteSidenav,
    sidenavTint,
    darkMode,
    themeName,
  } = controller;
  const [disabled, setDisabled] = useState(false);
  const configuratorRef = useRef(null);
  // "slate" and "sage" are no longer offered; their gradients stay in
  // colors.js so a profile that saved one still renders.
  const sidenavColors = ["steel", "teal", "amber", "mauve", "gold", "mint"];
  // A curated subset of sidenavColors that reads well as a full sidenav
  // background rather than a small accent chip, so the "Sidenav Style" row
  // shows the same number of swatches (6: Dark, White + 4 tints) as the
  // "Sidenav Colors" row above it.
  const sidenavTypeTints = ["steel", "teal", "amber", "mauve"];

  // Use the useEffect hook to change the button state for the sidenav type based on window size.
  useEffect(() => {
    // A function that sets the disabled state of the buttons for the sidenav type.
    function handleDisabled() {
      return window.innerWidth > 1200 ? setDisabled(false) : setDisabled(true);
    }

    // The event listener that's calling the handleDisabled function when resizing the window.
    window.addEventListener("resize", handleDisabled);

    // Call the handleDisabled function to set the state with the initial value.
    handleDisabled();

    // Remove event listener on cleanup
    return () => window.removeEventListener("resize", handleDisabled);
  }, []);

  // Close the panel on a click anywhere outside it. Only attached while open,
  // so the very click that opens the panel (via the toggle button elsewhere
  // in the app) can never also be seen as the "outside click" that closes it
  // — this effect isn't registered with the DOM until after that click has
  // already finished dispatching.
  useEffect(() => {
    if (!openConfigurator) {
      return undefined;
    }

    function handleClickOutside(event) {
      if (configuratorRef.current && !configuratorRef.current.contains(event.target)) {
        setOpenConfigurator(dispatch, false);
      }
    }

    document.addEventListener("mousedown", handleClickOutside);

    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [openConfigurator, dispatch]);

  const handleCloseConfigurator = () => setOpenConfigurator(dispatch, false);
  const handleWhiteSidenav = () => {
    setWhiteSidenav(dispatch, true);
    setSidenavTint(dispatch, null);
  };
  const handleDarkSidenav = () => {
    setWhiteSidenav(dispatch, false);
    setSidenavTint(dispatch, null);
  };
  const handleTintedSidenav = (color) => {
    setSidenavTint(dispatch, color);
    setWhiteSidenav(dispatch, false);
  };
  const handleFixedNavbar = () => setFixedNavbar(dispatch, !fixedNavbar);
  const handleDarkMode = () => setDarkMode(dispatch, !darkMode);

  // Background fill for a swatch button. Accent colors (steel, slate, ...)
  // and "dark" use the theme's gradient pairs; "white" is a flat fill.
  const getSwatchBackground = (variant, { functions: { linearGradient }, palette: { gradients, white } }) => {
    if (variant === "white") {
      return { backgroundImage: "none", background: white.main };
    }

    return { backgroundImage: linearGradient(gradients[variant].main, gradients[variant].state) };
  };

  // Shared 24px circular swatch style used for both the accent-color row
  // and the sidenav-type row, so the two look like one family of controls.
  const swatchStyles = (variant, isActive) => (theme) => {
    const {
      borders: { borderWidth },
      palette: { white, dark, background },
      transitions,
    } = theme;

    return {
      width: "24px",
      height: "24px",
      padding: 0,
      border: `${borderWidth[1]} solid ${darkMode ? background.sidenav : white.main}`,
      borderColor: isActive ? (darkMode ? white.main : dark.main) : "transparent",
      transition: transitions.create("border-color", {
        easing: transitions.easing.sharp,
        duration: transitions.duration.shorter,
      }),
      ...getSwatchBackground(variant, theme),

      "&:not(:last-child)": {
        mr: 1,
      },

      "&:hover, &:focus, &:active": {
        borderColor: darkMode ? white.main : dark.main,
      },
    };
  };

  const activeTheme = themeOptions.find((option) => option.id === themeName) ?? themeOptions[0];

  return (
    <ConfiguratorRoot ref={configuratorRef} variant="permanent" ownerState={{ openConfigurator }}>
      <MDBox
        display="flex"
        justifyContent="space-between"
        alignItems="baseline"
        pt={4}
        pb={0.5}
        px={3}
      >
        <MDBox>
          <MDTypography variant="h5">Display Settings</MDTypography>
          <MDTypography variant="body2" color="text">
            Personalize your workspace appearance.
          </MDTypography>
        </MDBox>

        <Icon
          sx={({ typography: { size }, palette: { dark, white } }) => ({
            fontSize: `${size.lg} !important`,
            color: darkMode ? white.main : dark.main,
            stroke: "currentColor",
            strokeWidth: "2px",
            cursor: "pointer",
            transform: "translateY(5px)",
          })}
          onClick={handleCloseConfigurator}
        >
          close
        </Icon>
      </MDBox>

      <Divider />

      <MDBox pt={0.5} pb={3} px={3}>
        <MDBox mb={3}>
          <MDTypography variant="h6">Theme</MDTypography>

          <MDBox mt={1.5}>
            <MDInput
              select
              fullWidth
              size="small"
              SelectProps={{ native: true }}
              inputProps={{ "aria-label": "Theme" }}
              value={themeName}
              onChange={(event) => setThemeName(dispatch, event.target.value)}
            >
              {themeOptions.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name}
                </option>
              ))}
            </MDInput>
          </MDBox>

          {/* The chosen theme's colours and description. */}
          <MDBox display="flex" alignItems="center" gap={1.5} mt={1.5} pl={0.5}>
            <MDBox display="flex" flexShrink={0}>
              {activeTheme.swatch.map((color) => (
                <MDBox
                  key={color}
                  sx={({ palette: { grey } }) => ({
                    width: 18,
                    height: 18,
                    ml: "-4px",
                    borderRadius: "50%",
                    backgroundColor: color,
                    border: `1px solid ${grey[500]}`,
                  })}
                />
              ))}
            </MDBox>
            <MDTypography variant="caption" color="text">
              {activeTheme.description}
            </MDTypography>
          </MDBox>
        </MDBox>

        <MDBox>
          <MDTypography variant="h6">Sidenav Colors</MDTypography>

          <MDBox mb={0.5}>
            {sidenavColors.map((color) => (
              <IconButton
                key={color}
                title={color}
                sx={swatchStyles(color, sidenavColor === color)}
                onClick={() => setSidenavColor(dispatch, color)}
              />
            ))}
          </MDBox>
        </MDBox>

        <MDBox mt={3}>
          <MDTypography variant="h6">Sidenav Style</MDTypography>

          <MDBox mb={0.5}>
            <IconButton
              title="Dark"
              disabled={disabled}
              sx={swatchStyles("dark", !whiteSidenav && !sidenavTint)}
              onClick={handleDarkSidenav}
            />
            <IconButton
              title="White"
              disabled={disabled}
              sx={swatchStyles("white", whiteSidenav)}
              onClick={handleWhiteSidenav}
            />
            {sidenavTypeTints.map((color) => (
              <IconButton
                key={`type-${color}`}
                title={color}
                disabled={disabled}
                sx={swatchStyles(color, sidenavTint === color)}
                onClick={() => handleTintedSidenav(color)}
              />
            ))}
          </MDBox>
        </MDBox>
        <MDBox
          display="flex"
          justifyContent="space-between"
          alignItems="center"
          mt={3}
          lineHeight={1}
        >
          <MDTypography variant="h6">Navbar Fixed</MDTypography>

          <Switch checked={fixedNavbar} onChange={handleFixedNavbar} />
        </MDBox>
        <Divider />
        <MDBox display="flex" justifyContent="space-between" alignItems="center" lineHeight={1}>
          <MDTypography variant="h6">Light / Dark</MDTypography>

          <Switch checked={darkMode} onChange={handleDarkMode} />
        </MDBox>
      </MDBox>
    </ConfiguratorRoot>
  );
}

export default Configurator;
