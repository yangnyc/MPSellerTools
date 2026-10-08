// The MUI theme for each named theme (context/themes.js) and mode.
//
// "ocean" is the base pair in assets/theme and assets/theme-dark, coloured
// in navy and steel blue with coral, crimson and amber accents (see
// examples/Kit/tokens.js for the palette). The
// others are built on top of that pair by overriding its palette and the few
// component styles that have the base colours baked in — they share every
// size, spacing, and typography decision with the base.
import { createTheme } from "@mui/material/styles";

import theme from "assets/theme";
import { nativeOptionSelector } from "assets/theme/base/globals";
import themeDark from "assets/theme-dark";
import { defaultThemeName, sidenavGradients } from "context/themes";

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
// `sidenav` is the fill of the sidenav in its "Dark" style.
function flatTheme(themeName, base, { background, text, dark, accent, button, onButton, line, sidenav, ...rest }) {
  // The text of an open dropdown's list.
  const onDark = base === themeDark;
  const optionText = rest.optionText ?? (onDark ? "#FFFFFF" : dark.main);

  return createTheme(base, {
    palette: {
      background,
      text: { main: text, focus: text },
      dark,
      primary: { main: button, focus: button, contrastText: onButton },
      info: { main: button, focus: button, contrastText: onButton },
      // Amber and red, not the base pair's coral and brick; lighter on a dark page.
      warning: onDark ? { main: "#E0A458", focus: "#B7791F" } : { main: "#B7791F", focus: "#8F5510" },
      error: onDark ? { main: "#F47C73", focus: "#C2413A" } : { main: "#C2413A", focus: "#9E2B25" },
      onColors: { primary: onButton, info: onButton },
      gradients: {
        primary: flat(button),
        info: flat(button),
        warning: flat("#B7791F"),
        error: flat("#C2413A"),
        dark: flat(sidenav),
        ...sidenavGradients(themeName),
      },
    },
    boxShadows: { colored: noColoredShadows },
    components: {
      // An open dropdown's list, in this theme's colours rather than the base pair's.
      MuiCssBaseline: {
        styleOverrides: {
          [nativeOptionSelector]: { backgroundColor: background.card, color: optionText },
        },
      },
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

// The base pair with the default theme's sidenav swatches added.
const withSidenav = (base) => createTheme(base, { palette: { gradients: sidenavGradients(defaultThemeName) } });

const muiThemes = {
  ocean: { light: withSidenav(theme), dark: withSidenav(themeDark) },
  // Sapphire Ash: Figma's "Sapphire ash morning" palette.
  sapphire: {
    // Light: ash-white page, sapphire sidenav and buttons with white text.
    light: flatTheme("sapphire", theme, {
      background: { default: "#F5F5F5", sidenav: "#FFFFFF", card: "#FFFFFF" },
      text: "#4A5B63",
      dark: { main: "#1F3B4A", focus: "#162C38" },
      accent: "#35627A",
      button: "#35627A",
      onButton: "#FFFFFF",
      line: "#DDE2E1",
      sidenav: "#35627A",
    }),
    // Dark: deep sapphire page and cards, rose buttons with dark text.
    dark: flatTheme("sapphire", themeDark, {
      background: { default: "#14242D", sidenav: "#1B3441", card: "#1F3B4A" },
      text: "#CBD5D8",
      dark: { main: "#2A4E61", focus: "#1F3B4A" },
      accent: "#E5AEA9",
      button: "#E5AEA9",
      onButton: "#14242D",
      line: "rgba(245, 245, 245, 0.1)",
      sidenav: "#1B3441",
    }),
  },
  // Astro Novalite: ColorMagic's palette of that name.
  astro: {
    // Light: cream-white page, night-grey sidenav and buttons with cream text.
    light: flatTheme("astro", theme, {
      background: { default: "#FAF6EA", sidenav: "#FFFFFF", card: "#FFFFFF" },
      text: "#5C6575",
      dark: { main: "#1E1F2A", focus: "#14151D" },
      accent: "#4E6580",
      button: "#3A3F4B",
      onButton: "#F5E8C7",
      line: "#E6E0D0",
      sidenav: "#1E1F2A",
    }),
    // Dark: night-grey page and cards, cream buttons with dark text.
    dark: flatTheme("astro", themeDark, {
      background: { default: "#1E1F2A", sidenav: "#252733", card: "#2C303B" },
      text: "#C9D2DD",
      dark: { main: "#3A3F4B", focus: "#2C303B" },
      accent: "#F5E8C7",
      button: "#F5E8C7",
      onButton: "#1E1F2A",
      line: "rgba(245, 232, 199, 0.1)",
      sidenav: "#252733",
    }),
  },
};

export default function muiThemeFor(themeName, darkMode) {
  const pair = muiThemes[themeName] ?? muiThemes[defaultThemeName];
  return darkMode ? pair.dark : pair.light;
}
