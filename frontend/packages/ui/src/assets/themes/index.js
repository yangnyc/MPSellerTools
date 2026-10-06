// The MUI theme for each named theme (context/themes.js) and mode.
//
// "ocean" is the base pair in assets/theme and assets/theme-dark, coloured
// with the Classy palette (#845EC2 #4B4453 #B0A8B9 #C34A36 #FF8066). The
// others are built on top of that pair by overriding its palette and the few
// component styles that have the base colours baked in — they share every
// size, spacing, and typography decision with the base.
import { createTheme } from "@mui/material/styles";

import theme from "assets/theme";
import { nativeOptionSelector } from "assets/theme/base/globals";
import themeDark from "assets/theme-dark";
import { defaultThemeName } from "context/themes";

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
// `gradients` adds to or replaces the palette's gradient pairs.
function flatTheme(base, { background, text, dark, accent, button, onButton, line, sidenav, gradients, ...rest }) {
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
        ...gradients,
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

const muiThemes = {
  ocean: { light: theme, dark: themeDark },
  // Navy Amber: Superdesign's "Stepwise" wizard design. In it the "gold"
  // sidenav colour is the design's amber.
  stepwise: {
    // Light: cream page, navy buttons with amber text.
    light: flatTheme(theme, {
      background: { default: "#FAF7F0", sidenav: "#FFFFFF", card: "#FFFFFF" },
      text: "#3D4F6B",
      dark: { main: "#0B1F3A", focus: "#081729" },
      accent: "#B07D0B",
      button: "#0B1F3A",
      onButton: "#F7C948",
      line: "#E8E2D3",
      sidenav: "#0B1F3A",
      gradients: { gold: { main: "#F7C948", state: "#F0B429" } },
    }),
    // Dark: navy page and cards, amber buttons with navy text.
    dark: flatTheme(themeDark, {
      background: { default: "#081729", sidenav: "#0B1F3A", card: "#102A4D" },
      text: "#CDD6E3",
      dark: { main: "#163661", focus: "#102A4D" },
      accent: "#F0B429",
      button: "#F0B429",
      onButton: "#081729",
      line: "rgba(244, 239, 227, 0.1)",
      sidenav: "#0B1F3A",
      gradients: { gold: { main: "#F7C948", state: "#F0B429" } },
    }),
  },
  // Matrix: a Unix terminal's phosphor green on black. In it the "mint"
  // sidenav colour is that green.
  matrix: {
    // Light: pale green page, black buttons with green text.
    light: flatTheme(theme, {
      background: { default: "#F2FFF4", sidenav: "#FFFFFF", card: "#FFFFFF" },
      text: "#0B4F1A",
      dark: { main: "#003B00", focus: "#001F00" },
      accent: "#008F11",
      button: "#0D0208",
      onButton: "#00FF41",
      line: "#C9EBCF",
      sidenav: "#0D0208",
      gradients: { mint: { main: "#00FF41", state: "#00C832" } },
    }),
    // Dark: black page and cards, green buttons with black text.
    dark: flatTheme(themeDark, {
      background: { default: "#000000", sidenav: "#000000", card: "#0A0F0A" },
      text: "#00D936",
      dark: { main: "#003B00", focus: "#001F00" },
      accent: "#00FF41",
      button: "#00FF41",
      onButton: "#000000",
      line: "rgba(0, 255, 65, 0.22)",
      sidenav: "#000000",
      optionText: "#00FF41",
      gradients: { mint: { main: "#00FF41", state: "#00C832" } },
    }),
  },
};

export default function muiThemeFor(themeName, darkMode) {
  const pair = muiThemes[themeName] ?? muiThemes[defaultThemeName];
  return darkMode ? pair.dark : pair.light;
}
