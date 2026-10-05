// The MUI theme for each named theme (context/themes.js) and mode.
//
// "ocean" is the original pair in assets/theme and assets/theme-dark. "noir"
// (Noir Gold) is built on top of that pair by overriding its palette and the
// few component styles that have the original colours baked in — it shares
// every size, spacing, and typography decision with the original.

import { createTheme } from "@mui/material/styles";

import theme from "assets/theme";
import themeDark from "assets/theme-dark";
import { defaultThemeName } from "context/themes";

const gold = "#D4AF37";
const ink = "#0B0B0B";
const ivory = "#F5F1E6";

// Minimalist: buttons are flat, with no coloured glow under them.
const noColoredShadows = {
  primary: null,
  secondary: null,
  info: null,
  success: null,
  warning: null,
  error: null,
  light: null,
  dark: null,
};

const flat = (color) => ({ main: color, state: color });

// `accent` is the colour of focus rings and switches; `button` / `onButton`
// are the main action button's fill and its text.
function noirTheme(base, { background, text, dark, accent, button, onButton, line }) {
  return createTheme(base, {
    palette: {
      background,
      text: { main: text, focus: text },
      dark,
      primary: { main: button, focus: button, contrastText: onButton },
      info: { main: button, focus: button, contrastText: onButton },
      onColors: { primary: onButton, info: onButton },
      gradients: {
        primary: flat(button),
        info: flat(button),
        dark: flat("#0A0A0A"),
        gold: flat(gold),
      },
    },
    boxShadows: { colored: noColoredShadows },
    components: {
      MuiDrawer: {
        styleOverrides: { paper: { backgroundColor: background.sidenav, boxShadow: "none" } },
      },
      MuiCard: {
        styleOverrides: {
          root: { backgroundColor: background.card, border: `1px solid ${line}`, boxShadow: "none" },
        },
      },
      MuiTableContainer: {
        styleOverrides: { root: { backgroundColor: background.card, boxShadow: "none" } },
      },
      MuiTableCell: {
        styleOverrides: { root: { borderBottomColor: line } },
      },
      MuiMenu: {
        styleOverrides: {
          paper: {
            backgroundColor: `${background.card} !important`,
            border: `1px solid ${line}`,
            boxShadow: "none",
          },
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: { "&.Mui-focused": { "& .MuiOutlinedInput-notchedOutline": { borderColor: accent } } },
        },
      },
      MuiInputLabel: {
        styleOverrides: { root: { "&.Mui-focused": { color: accent } } },
      },
      MuiSwitch: {
        styleOverrides: {
          switchBase: {
            "&.Mui-checked": {
              "& .MuiSwitch-thumb": { borderColor: `${accent} !important` },
              "& + .MuiSwitch-track": {
                backgroundColor: `${accent} !important`,
                borderColor: `${accent} !important`,
              },
            },
          },
        },
      },
    },
  });
}

const muiThemes = {
  ocean: { light: theme, dark: themeDark },
  noir: {
    // Light: ivory page, black buttons with gold text.
    light: noirTheme(theme, {
      background: { default: "#FAF8F3", sidenav: "#FFFFFF", card: "#FFFFFF" },
      text: "#4F4A3E",
      dark: { main: ink, focus: "#000000" },
      accent: "#8A6D12",
      button: ink,
      onButton: "#E3C766",
      line: "#E6E0CF",
    }),
    // Dark: black page, gold buttons with black text.
    dark: noirTheme(themeDark, {
      background: { default: "#0A0A0A", sidenav: "#0A0A0A", card: "#121212" },
      text: `${ivory}cc`,
      dark: { main: "#1F1F1F", focus: "#121212" },
      accent: gold,
      button: gold,
      onButton: ink,
      line: "rgba(212, 175, 55, 0.16)",
    }),
  },
};

export default function muiThemeFor(themeName, darkMode) {
  const pair = muiThemes[themeName] ?? muiThemes[defaultThemeName];
  return darkMode ? pair.dark : pair.light;
}
