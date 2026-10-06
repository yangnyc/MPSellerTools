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
    default: "#2E2933",
    sidenav: "#39333F",
    card: "#4B4453",
  },

  // `secondary` and `disabled`, and `action` below, are what MUI itself uses
  // for helper text and disabled controls; its defaults are black, for a
  // light page.
  text: {
    main: "#E3DEE8cc",
    focus: "#E3DEE8cc",
    secondary: "rgba(255, 255, 255, 0.7)",
    disabled: "rgba(255, 255, 255, 0.45)",
  },

  action: {
    disabled: "rgba(255, 255, 255, 0.45)",
    disabledBackground: "rgba(255, 255, 255, 0.12)",
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

  // Lighter than the light theme's, so it shows against the #4B4453 cards.
  primary: {
    main: "#6A6173",
    focus: "#5C5466",
  },

  secondary: {
    main: "#B0A8B9",
    focus: "#CCC5D3",
  },

  // #845EC2 and #C34A36 lightened, so outlined buttons read on the #4B4453 cards.
  info: {
    main: "#A98BDD",
    focus: "#845EC2",
  },

  success: {
    main: "#059669",
    focus: "#047857",
  },

  warning: {
    main: "#FF8066",
    focus: "#B24A33",
  },

  error: {
    main: "#EE8A77",
    focus: "#C34A36",
  },

  light: {
    main: "#F1EEF466",
    focus: "#F1EEF466",
  },

  dark: {
    main: "#5C5466",
    focus: "#4B4453",
  },

  grey: {
    100: "#F8F7FA",
    200: "#F1EEF4",
    300: "#E3DEE8",
    400: "#CCC5D3",
    500: "#B0A8B9",
    600: "#7D7486",
    700: "#4B4453",
    800: "#3A3440",
    900: "#332E39",
  },

  gradients: {
    primary: {
      main: "#6A6173",
      state: "#3A3440",
    },

    secondary: {
      main: "#B0A8B9",
      state: "#7D7486",
    },

    info: {
      main: "#A07FD6",
      state: "#845EC2",
    },

    // Dark enough for the white text on a success button.
    success: {
      main: "#0E9F6E",
      state: "#047857",
    },

    warning: {
      main: "#FF9C87",
      state: "#FF8066",
    },

    error: {
      main: "#D9624E",
      state: "#C34A36",
    },

    light: {
      main: "#F1EEF4",
      state: "#E3DEE8",
    },

    dark: {
      main: "#3A3440",
      state: "#332E39",
    },

    // Sidenav accent swatches — see the matching comment in the light theme's
    // colors.js. Kept identical here since the dark-mode sidenav background
    // ("#39333F") sits in the same tonal range as the light theme's dark
    // gradient, so the same muted set still reads cleanly against it.
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
      background: "#ECE9EF",
      text: "#3A3440",
    },

    secondary: {
      background: "#F1EEF4",
      text: "#4B4453",
    },

    info: {
      background: "#EDE5F8",
      text: "#6E4BAA",
    },

    success: {
      background: "#D1FAE5",
      text: "#047857",
    },

    warning: {
      background: "#FFE9E3",
      text: "#B24A33",
    },

    error: {
      background: "#F9E1DC",
      text: "#9E3324",
    },

    light: {
      background: "#ffffff",
      text: "#CCC5D3",
    },

    dark: {
      background: "#B0A8B9",
      text: "#332E39",
    },
  },

  coloredShadows: {
    primary: "#4B4453",
    secondary: "#7D7486",
    info: "#845EC2",
    success: "#059669",
    warning: "#FF8066",
    error: "#C34A36",
    light: "#CCC5D3",
    dark: "#332E39",
  },

  inputBorderColor: "#CCC5D3",

  tabs: {
    indicator: { boxShadow: "#ddd" },
  },
};

export default colors;
