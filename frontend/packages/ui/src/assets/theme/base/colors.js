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
    default: "#F8FAFC",
  },

  text: {
    main: "#475569",
    focus: "#475569",
  },

  transparent: {
    main: "transparent",
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
    main: "#4F46E5",
    focus: "#4338CA",
  },

  secondary: {
    main: "#64748B",
    focus: "#475569",
  },

  info: {
    main: "#0284C7",
    focus: "#0369A1",
  },

  success: {
    main: "#059669",
    focus: "#047857",
  },

  warning: {
    main: "#D97706",
    focus: "#B45309",
  },

  error: {
    main: "#DC2626",
    focus: "#B91C1C",
  },

  light: {
    main: "#F1F5F9",
    focus: "#F1F5F9",
  },

  dark: {
    main: "#1E293B",
    focus: "#0F172A",
  },

  grey: {
    100: "#F8FAFC",
    200: "#F1F5F9",
    300: "#E2E8F0",
    400: "#CBD5E1",
    500: "#94A3B8",
    600: "#64748B",
    700: "#475569",
    800: "#334155",
    900: "#1E293B",
  },

  gradients: {
    primary: {
      main: "#6366F1",
      state: "#4338CA",
    },

    secondary: {
      main: "#94A3B8",
      state: "#64748B",
    },

    info: {
      main: "#38BDF8",
      state: "#0284C7",
    },

    success: {
      main: "#34D399",
      state: "#059669",
    },

    warning: {
      main: "#FBBF24",
      state: "#D97706",
    },

    error: {
      main: "#F87171",
      state: "#DC2626",
    },

    light: {
      main: "#F1F5F9",
      state: "#E2E8F0",
    },

    dark: {
      main: "#334155",
      state: "#1E293B",
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
      background: "#E0E7FF",
      text: "#4338CA",
    },

    secondary: {
      background: "#F1F5F9",
      text: "#475569",
    },

    info: {
      background: "#E0F2FE",
      text: "#0369A1",
    },

    success: {
      background: "#D1FAE5",
      text: "#047857",
    },

    warning: {
      background: "#FEF3C7",
      text: "#B45309",
    },

    error: {
      background: "#FEE2E2",
      text: "#B91C1C",
    },

    light: {
      background: "#ffffff",
      text: "#CBD5E1",
    },

    dark: {
      background: "#94A3B8",
      text: "#1E293B",
    },
  },

  coloredShadows: {
    primary: "#4F46E5",
    secondary: "#64748B",
    info: "#0284C7",
    success: "#059669",
    warning: "#D97706",
    error: "#DC2626",
    light: "#CBD5E1",
    dark: "#1E293B",
  },

  inputBorderColor: "#CBD5E1",

  tabs: {
    indicator: { boxShadow: "#ddd" },
  },
};

export default colors;
