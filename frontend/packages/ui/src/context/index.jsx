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

/**
  This file is used for controlling the global states of the components,
  you can customize the states for the different components here.
*/

import { createContext, useContext, useReducer, useMemo } from "react";

// prop-types is a library for typechecking of props
import PropTypes from "prop-types";

import { defaultThemeName, themeOptions, resolveThemeName, resolveSidenav } from "context/themes";

// Material Dashboard 2 React main context
// NOTE: authentication state is intentionally NOT part of this shared UI context.
// Each app (platform/workspace) owns its own cookie-session-based auth state
// (see MPSellerTools.PlatformHost / MPSellerTools.TenantHost + Increment 2).
const MaterialUI = createContext();

// Setting custom name for the context which is visible on react dev tools
MaterialUI.displayName = "MaterialUIContext";

// Material Dashboard 2 React reducer
function reducer(state, action) {
  switch (action.type) {
    case "MINI_SIDENAV": {
      return { ...state, miniSidenav: action.value };
    }
    case "WHITE_SIDENAV": {
      return { ...state, whiteSidenav: action.value };
    }
    case "SIDENAV_TINT": {
      return { ...state, sidenavTint: action.value };
    }
    case "SIDENAV_COLOR": {
      return { ...state, sidenavColor: action.value };
    }
    case "TRANSPARENT_NAVBAR": {
      return { ...state, transparentNavbar: action.value };
    }
    case "FIXED_NAVBAR": {
      return { ...state, fixedNavbar: action.value };
    }
    case "OPEN_CONFIGURATOR": {
      return { ...state, openConfigurator: action.value };
    }
    case "DIRECTION": {
      return { ...state, direction: action.value };
    }
    case "LAYOUT": {
      return { ...state, layout: action.value };
    }
    case "DARKMODE": {
      return { ...state, darkMode: action.value };
    }
    // Switches to a named theme and applies its light/dark and sidenav preset.
    case "THEME_NAME": {
      const themeName = resolveThemeName(action.value);
      const { preset } = themeOptions.find((option) => option.id === themeName);
      return { ...state, ...preset, themeName };
    }
    // Applies a saved per-user theme (see each app's ThemeSync) in one step.
    case "THEME_SETTINGS": {
      const { darkMode, whiteSidenav, fixedNavbar } = action.value;
      const themeName = resolveThemeName(action.value.themeName);
      return {
        ...state,
        themeName,
        darkMode,
        whiteSidenav,
        // Only the swatches this theme offers.
        ...resolveSidenav(themeName, action.value),
        fixedNavbar,
      };
    }
    default: {
      throw new Error(`Unhandled action type: ${action.type}`);
    }
  }
}

// The theme a user sees until they change something. Theme choices are stored
// only on the signed-in user's profile in the database (see each app's
// ThemeSync) — nothing is kept in the browser.
const defaultThemeSettings = {
  themeName: defaultThemeName,
  darkMode: false,
  whiteSidenav: false,
  sidenavTint: null,
  sidenavColor: "harbor",
  fixedNavbar: true,
};

// Material Dashboard 2 React context provider
function MaterialUIControllerProvider({ children }) {
  const initialState = {
    // sidenavTint is non-null when the sidenav background itself is tinted
    // with one of the theme's sidenav swatches (see Configurator's
    // "Sidenav Style" row), mutually exclusive with whiteSidenav.
    ...defaultThemeSettings,
    miniSidenav: false,
    transparentNavbar: true,
    openConfigurator: false,
    direction: "ltr",
    layout: "dashboard",
  };

  const [controller, dispatch] = useReducer(reducer, initialState);

  const value = useMemo(() => [controller, dispatch], [controller, dispatch]);

  return <MaterialUI.Provider value={value}>{children}</MaterialUI.Provider>;
}

// Material Dashboard 2 React custom hook for using context
function useMaterialUIController() {
  const context = useContext(MaterialUI);

  if (!context) {
    throw new Error(
      "useMaterialUIController should be used inside the MaterialUIControllerProvider."
    );
  }

  return context;
}

// Typechecking props for the MaterialUIControllerProvider
MaterialUIControllerProvider.propTypes = {
  children: PropTypes.node.isRequired,
};

// Context module functions
const setMiniSidenav = (dispatch, value) => dispatch({ type: "MINI_SIDENAV", value });
const setWhiteSidenav = (dispatch, value) => dispatch({ type: "WHITE_SIDENAV", value });
const setSidenavTint = (dispatch, value) => dispatch({ type: "SIDENAV_TINT", value });
const setSidenavColor = (dispatch, value) => dispatch({ type: "SIDENAV_COLOR", value });
const setTransparentNavbar = (dispatch, value) => dispatch({ type: "TRANSPARENT_NAVBAR", value });
const setFixedNavbar = (dispatch, value) => dispatch({ type: "FIXED_NAVBAR", value });
const setOpenConfigurator = (dispatch, value) => dispatch({ type: "OPEN_CONFIGURATOR", value });
const setDirection = (dispatch, value) => dispatch({ type: "DIRECTION", value });
const setLayout = (dispatch, value) => dispatch({ type: "LAYOUT", value });
const setDarkMode = (dispatch, value) => dispatch({ type: "DARKMODE", value });
const setThemeName = (dispatch, value) => dispatch({ type: "THEME_NAME", value });
const applyThemeSettings = (dispatch, value) => dispatch({ type: "THEME_SETTINGS", value });

export {
  MaterialUIControllerProvider,
  useMaterialUIController,
  setMiniSidenav,
  setWhiteSidenav,
  setSidenavTint,
  setSidenavColor,
  setTransparentNavbar,
  setFixedNavbar,
  setOpenConfigurator,
  setDirection,
  setLayout,
  setDarkMode,
  setThemeName,
  applyThemeSettings,
  defaultThemeSettings,
  themeOptions,
};
