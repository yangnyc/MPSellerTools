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
 * The base colors for the Material Dashboard 2 PRO React.
 * You can add new color using this file.
 * You can customized the colors for the entire Material Dashboard 2 PRO React using thie file.
 */

const colors = {
  background: {
    default: "#F6F9FB",
  },

  text: {
    main: "#192755",
    focus: "#192755",
  },

  transparent: {
    // Written as rgba because MUI 9 parses every palette colour and rejects the keyword.
    main: "rgba(0, 0, 0, 0)",
  },

  white: {
    main: "#ffffff",
    focus: "#ffffff",
  },

  black: {
    light: "#000000",
    main: "#000000",
    focus: "#000000",
  },

  primary: {
    main: "#192755",
    focus: "#121F45",
  },

  secondary: {
    main: "#546B88",
    focus: "#192755",
  },

  info: {
    main: "#3B6695",
    focus: "#2D5178",
  },

  success: {
    main: "#059669",
    focus: "#047857",
  },

  // The coral (#F36152) darkened, so an outlined button's text reads on white.
  warning: {
    main: "#C63D2E",
    focus: "#A93425",
  },

  error: {
    main: "#B71443",
    focus: "#8F0F34",
  },

  light: {
    main: "#EDF2F7",
    focus: "#EDF2F7",
  },

  dark: {
    main: "#192755",
    focus: "#0B1733",
  },

  grey: {
    100: "#F6F9FB",
    200: "#EDF2F7",
    300: "#DCE6EE",
    400: "#C3D3DF",
    500: "#ABC6D3",
    600: "#546B88",
    700: "#192755",
    800: "#121F45",
    900: "#0B1733",
  },

  gradients: {
    primary: {
      main: "#33426F",
      state: "#121F45",
    },

    secondary: {
      main: "#ABC6D3",
      state: "#546B88",
    },

    info: {
      main: "#4A78A9",
      state: "#3B6695",
    },

    // Dark enough for the white text on a success button.
    success: {
      main: "#06805B",
      state: "#047857",
    },

    // The coral darkened, so the white text on a warning button reads.
    warning: {
      main: "#D2432F",
      state: "#C63D2E",
    },

    error: {
      main: "#CF3560",
      state: "#B71443",
    },

    light: {
      main: "#EDF2F7",
      state: "#DCE6EE",
    },

    dark: {
      main: "#121F45",
      state: "#0B1733",
    },

    // Sidenav accent swatches — muted tones tuned to sit well against the
    // sidenav's own backgrounds (the charcoal/navy gradient or white),
    // rather than the saturated primary/success/warning/error hues those
    // are reused for elsewhere (status badges, buttons) where a louder
    // color carries real meaning. Every "main" stop clears 4.5:1 contrast
    // against white text uniformly — amber was previously a lighter gold
    // that needed a dark-text carve-out (see sidenavTintContrast.js) and,
    // worse, was unreadable as the *active nav item* pill (white text on
    // "Sidenav Colors" always renders white regardless of accent), so it's
    // now a deeper gold that behaves like the other five in every Sidenav
    // Style (Dark, White, or a matching Sidenav Style tint).
    steel: {
      main: "#4C6E94",
      state: "#35506D",
    },

    slate: {
      main: "#5B6B82",
      state: "#404C5E",
    },

    teal: {
      main: "#1F7A70",
      state: "#15574F",
    },

    sage: {
      main: "#5F7A52",
      state: "#445A3A",
    },

    amber: {
      main: "#92400E",
      state: "#78350F",
    },

    mauve: {
      main: "#8D5F86",
      state: "#684761",
    },

    // The Noir Gold theme's accent. Unlike the six above it is too light for
    // white text, so it is registered in sidenavTintContrast.js.
    gold: {
      main: "#D4AF37",
      state: "#B8911F",
    },

    // Like gold, too light for white text.
    mint: {
      main: "#36D7C3",
      state: "#00A08E",
    },
  },

  socialMediaColors: {
    facebook: {
      main: "#3b5998",
      dark: "#344e86",
    },

    twitter: {
      main: "#55acee",
      dark: "#3ea1ec",
    },

    instagram: {
      main: "#125688",
      dark: "#0e456d",
    },

    linkedin: {
      main: "#0077b5",
      dark: "#00669c",
    },

    pinterest: {
      main: "#cc2127",
      dark: "#b21d22",
    },

    youtube: {
      main: "#e52d27",
      dark: "#d41f1a",
    },

    vimeo: {
      main: "#1ab7ea",
      dark: "#13a3d2",
    },

    slack: {
      main: "#3aaf85",
      dark: "#329874",
    },

    dribbble: {
      main: "#ea4c89",
      dark: "#e73177",
    },

    github: {
      main: "#24292e",
      dark: "#171a1d",
    },

    reddit: {
      main: "#ff4500",
      dark: "#e03d00",
    },

    tumblr: {
      main: "#35465c",
      dark: "#2a3749",
    },
  },

  badgeColors: {
    primary: {
      background: "#E6ECF3",
      text: "#121F45",
    },

    secondary: {
      background: "#EDF2F7",
      text: "#192755",
    },

    info: {
      background: "#E3EDF6",
      text: "#2D5178",
    },

    success: {
      background: "#D1FAE5",
      text: "#047857",
    },

    warning: {
      background: "#FDE8E4",
      text: "#A93425",
    },

    error: {
      background: "#F9E0E7",
      text: "#8F0F34",
    },

    light: {
      background: "#ffffff",
      text: "#C3D3DF",
    },

    dark: {
      background: "#ABC6D3",
      text: "#0B1733",
    },
  },

  coloredShadows: {
    primary: "#192755",
    secondary: "#546B88",
    info: "#3B6695",
    success: "#059669",
    warning: "#F36152",
    error: "#B71443",
    light: "#C3D3DF",
    dark: "#0B1733",
  },

  inputBorderColor: "#C3D3DF",

  tabs: {
    indicator: { boxShadow: "#ddd" },
  },
};

export default colors;
