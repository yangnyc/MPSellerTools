// MP Seller Tools page kit — colour tokens.
//
// The kit (examples/Kit) is the set of page-level building blocks every
// signed-in page of both apps is composed from. It resolves every colour for
// the active named theme (context/themes.js) and its light or dark mode here,
// so pages never branch on the theme or on darkMode themselves.
//
// "ocean" is navy and steel blue with warm accents: navy #192755 and
// #0B1733, pale blue #ABC6D3, coral #F36152, crimson #B71443 and amber
// #FEB663, with tints and shades of them. The action colour #3B6695 is the
// palette's steel blue (#728FAD) darkened until links and white button text
// read on it. Green stays the colour of success; the palette has none. Its
// dark mode is Twitter's dim palette: page #15202B, cards #192734, hover
// #22303C, white text and #8899A6 secondary text, with the same steel blue
// for actions. The other themes are described at their blocks.
import { useMemo } from "react";

import { useMaterialUIController } from "context";
import { defaultThemeName } from "context/themes";

const kitThemes = {
  ocean: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F6F9FB",
        border: "#DCE6EE",
        borderStrong: "#C3D3DF",
        text: "#0B1733",
        muted: "#546B88",
        subtle: "#64799A",
        hover: "rgba(25, 39, 85, 0.05)",
        accent: "#3B6695",
        shadow: "0 1px 2px rgba(11, 23, 51, 0.04), 0 12px 28px -18px rgba(11, 23, 51, 0.18)",
        navbar: "rgba(255, 255, 255, 0.82)",
      },
      dark: {
        surface: "#192734",
        surfaceAlt: "rgba(21, 32, 43, 0.5)",
        border: "rgba(136, 153, 166, 0.2)",
        borderStrong: "rgba(136, 153, 166, 0.38)",
        text: "#FFFFFF",
        muted: "#8899A6",
        subtle: "#8899A6",
        hover: "#22303C",
        // #3B6695 lightened, to stay readable on the #192734 surface.
        accent: "#A6C3E3",
        shadow: "0 1px 2px rgba(0, 0, 0, 0.25), 0 12px 28px -18px rgba(0, 0, 0, 0.6)",
        navbar: "rgba(25, 39, 52, 0.82)",
      },
    },
    tones: {
      light: {
        info: { fg: "#2D5178", bg: "#E3EDF6", solid: "#3B6695" },
        primary: { fg: "#192755", bg: "#E6ECF3", solid: "#192755" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#A93425", bg: "#FDE8E4", solid: "#F36152" },
        error: { fg: "#8F0F34", bg: "#F9E0E7", solid: "#B71443" },
        neutral: { fg: "#192755", bg: "#EDF2F7", solid: "#546B88" },
      },
      dark: {
        info: { fg: "#B9D0EA", bg: "rgba(59, 102, 149, 0.24)", solid: "#4A78A9" },
        primary: { fg: "#D5DEE5", bg: "rgba(136, 153, 166, 0.18)", solid: "#8899A6" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#F8B6A6", bg: "rgba(243, 97, 82, 0.16)", solid: "#F36152" },
        error: { fg: "#F4A9BC", bg: "rgba(183, 20, 67, 0.28)", solid: "#CF3560" },
        neutral: { fg: "#D5DEE5", bg: "rgba(136, 153, 166, 0.18)", solid: "#8899A6" },
      },
    },
    // The fills below are the light mode's; `darkFills` replaces them in dark mode.
    accentGradient: "linear-gradient(135deg, #3B6695 0%, #192755 100%)",
    onAccent: "#fff",
    hero: {
      background: [
        "radial-gradient(36rem 22rem at 8% 0%, rgba(59, 102, 149, 0.34), transparent 60%)",
        "radial-gradient(32rem 22rem at 96% 100%, rgba(243, 97, 82, 0.2), transparent 60%)",
        "linear-gradient(155deg, #08112A 0%, #121F45 50%, #192755 100%)",
      ].join(", "),
      border: "rgba(171, 198, 211, 0.18)",
      grid: "rgba(171, 198, 211, 0.09)",
      eyebrow: "#FEB663",
      subtitle: "rgba(237, 242, 247, 0.85)",
    },
    avatarFills: [
      "linear-gradient(135deg, #3B6695, #192755)",
      "linear-gradient(135deg, #B71443, #192755)",
      "linear-gradient(135deg, #4A78A9, #3B6695)",
      "linear-gradient(135deg, #F36152, #B71443)",
      "linear-gradient(135deg, #546B88, #192755)",
    ],
    onAvatar: "#fff",
    darkFills: {
      accentGradient: "linear-gradient(135deg, #4A78A9 0%, #3B6695 100%)",
      hero: {
        background: [
          "radial-gradient(36rem 22rem at 8% 0%, rgba(59, 102, 149, 0.3), transparent 60%)",
          "linear-gradient(155deg, #10171F 0%, #15202B 50%, #22303C 100%)",
        ].join(", "),
        border: "rgba(136, 153, 166, 0.22)",
        grid: "rgba(136, 153, 166, 0.09)",
        eyebrow: "#FEB663",
        subtitle: "rgba(255, 255, 255, 0.85)",
      },
      avatarFills: [
        "linear-gradient(135deg, #4A78A9, #3B6695)",
        "linear-gradient(135deg, #B71443, #22303C)",
        "linear-gradient(135deg, #546B88, #22303C)",
        "linear-gradient(135deg, #F36152, #B71443)",
      ],
    },
  },
  // "sapphire" (Sapphire Ash) is Figma's "Sapphire ash morning" palette:
  // sapphire #35627A, rose #E5AEA9, brick #B46258, periwinkle #A6A9D0, ash
  // white #F5F5F5 and sage grey #8E9A98, with shades of them where text has
  // to read. Green and amber stay the colours of success and warning; the
  // palette has neither.
  sapphire: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F5F5F5",
        border: "#DDE2E1",
        borderStrong: "#C3CBC9",
        text: "#1F3B4A",
        muted: "#5B6B69",
        subtle: "#6B7876",
        hover: "rgba(53, 98, 122, 0.05)",
        accent: "#35627A",
        shadow: "0 1px 2px rgba(31, 59, 74, 0.04), 0 12px 28px -18px rgba(31, 59, 74, 0.18)",
        navbar: "rgba(255, 255, 255, 0.86)",
      },
      dark: {
        surface: "#1F3B4A",
        surfaceAlt: "rgba(20, 36, 45, 0.5)",
        border: "rgba(245, 245, 245, 0.1)",
        borderStrong: "rgba(245, 245, 245, 0.2)",
        text: "#F5F5F5",
        muted: "#B4C0BE",
        subtle: "#8E9A98",
        hover: "rgba(245, 245, 245, 0.05)",
        accent: "#E5AEA9",
        shadow: "0 1px 2px rgba(0, 0, 0, 0.25), 0 16px 32px -20px rgba(0, 0, 0, 0.6)",
        navbar: "rgba(27, 52, 65, 0.86)",
      },
    },
    tones: {
      light: {
        info: { fg: "#2A4E61", bg: "#E1EBF0", solid: "#35627A" },
        primary: { fg: "#4A4D7E", bg: "#ECEDF6", solid: "#6B6FA8" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#8F5510", bg: "#FBEBD3", solid: "#B7791F" },
        error: { fg: "#8F4038", bg: "#F9E7E5", solid: "#B46258" },
        neutral: { fg: "#4A5B63", bg: "#ECEFEE", solid: "#8E9A98" },
      },
      dark: {
        info: { fg: "#A9CBDD", bg: "rgba(53, 98, 122, 0.35)", solid: "#4F86A3" },
        primary: { fg: "#C9CBE6", bg: "rgba(166, 169, 208, 0.16)", solid: "#A6A9D0" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#E0A458", bg: "rgba(224, 164, 88, 0.14)", solid: "#E0A458" },
        error: { fg: "#EDB3AC", bg: "rgba(180, 98, 88, 0.26)", solid: "#C9776D" },
        neutral: { fg: "#CBD5D8", bg: "rgba(142, 154, 152, 0.18)", solid: "#8E9A98" },
      },
    },
    accentGradient: "linear-gradient(135deg, #35627A 0%, #264A5D 100%)",
    onAccent: "#fff",
    hero: {
      background: [
        "radial-gradient(32rem 22rem at 96% 100%, rgba(229, 174, 169, 0.22), transparent 60%)",
        "linear-gradient(155deg, #1F3B4A 0%, #2A4E61 50%, #35627A 100%)",
      ].join(", "),
      border: "rgba(245, 245, 245, 0.16)",
      grid: "rgba(245, 245, 245, 0.07)",
      eyebrow: "#E5AEA9",
      subtitle: "rgba(245, 245, 245, 0.85)",
    },
    avatarFills: ["#35627A", "#B46258", "#6B6FA8", "#5F6D6B"],
    onAvatar: "#fff",
  },
  // "astro" (Astro Novalite) is ColorMagic's palette of that name: steel
  // grey #1E1F2A, bright grey #3A3F4B, shuttle grey #5C6575, Bali Hai blue
  // #8A9DB2 and cream #F5E8C7, with tints and shades of them. The light
  // mode's link colour #4E6580 is Bali Hai darkened until it reads on white.
  // Green, amber and red stay the colours of success, warning and error; the
  // palette has none of them.
  astro: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#FAF6EA",
        border: "#E6E0D0",
        borderStrong: "#CFC8B5",
        text: "#1E1F2A",
        muted: "#5C6575",
        subtle: "#6B7587",
        hover: "rgba(30, 31, 42, 0.04)",
        accent: "#4E6580",
        shadow: "0 1px 2px rgba(30, 31, 42, 0.05), 0 12px 28px -18px rgba(30, 31, 42, 0.2)",
        navbar: "rgba(255, 255, 255, 0.86)",
      },
      dark: {
        surface: "#2C303B",
        surfaceAlt: "rgba(30, 31, 42, 0.5)",
        border: "rgba(245, 232, 199, 0.1)",
        borderStrong: "rgba(245, 232, 199, 0.2)",
        text: "#F5E8C7",
        muted: "#A9B7C8",
        subtle: "#8A9DB2",
        hover: "rgba(245, 232, 199, 0.05)",
        accent: "#F5E8C7",
        shadow: "0 1px 2px rgba(0, 0, 0, 0.25), 0 16px 32px -20px rgba(0, 0, 0, 0.6)",
        navbar: "rgba(37, 39, 51, 0.86)",
      },
    },
    tones: {
      light: {
        info: { fg: "#3F546C", bg: "#E6ECF2", solid: "#4E6580" },
        primary: { fg: "#1E1F2A", bg: "#E7E8EC", solid: "#3A3F4B" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#8F5510", bg: "#FBEBD3", solid: "#B7791F" },
        error: { fg: "#9E2B25", bg: "#FBE6E4", solid: "#C2413A" },
        neutral: { fg: "#5C6575", bg: "#EEF0F3", solid: "#8A9DB2" },
      },
      dark: {
        info: { fg: "#C3D0DE", bg: "rgba(138, 157, 178, 0.2)", solid: "#8A9DB2" },
        primary: { fg: "#F5E8C7", bg: "rgba(245, 232, 199, 0.12)", solid: "#F5E8C7" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#E0A458", bg: "rgba(224, 164, 88, 0.14)", solid: "#E0A458" },
        error: { fg: "#F9A8A1", bg: "rgba(244, 124, 115, 0.16)", solid: "#F47C73" },
        neutral: { fg: "#C9D2DD", bg: "rgba(138, 157, 178, 0.16)", solid: "#8A9DB2" },
      },
    },
    // The fills below are the light mode's; `darkFills` replaces them in dark mode.
    accentGradient: "linear-gradient(135deg, #3A3F4B 0%, #1E1F2A 100%)",
    onAccent: "#F5E8C7",
    hero: {
      background: [
        "radial-gradient(32rem 22rem at 96% 100%, rgba(245, 232, 199, 0.14), transparent 60%)",
        "linear-gradient(155deg, #1E1F2A 0%, #3A3F4B 55%, #5C6575 100%)",
      ].join(", "),
      border: "rgba(245, 232, 199, 0.16)",
      grid: "rgba(245, 232, 199, 0.07)",
      eyebrow: "#F5E8C7",
      subtitle: "rgba(245, 232, 199, 0.85)",
    },
    avatarFills: ["#3A3F4B", "#5C6575", "#4E6580", "#1E1F2A"],
    onAvatar: "#F5E8C7",
    darkFills: {
      accentGradient: "#F5E8C7",
      onAccent: "#1E1F2A",
      avatarFills: ["#F5E8C7", "#8A9DB2", "#C9D2DD", "#A9B7C8"],
      onAvatar: "#1E1F2A",
    },
  },
};
// The default theme's accent fill, for callers outside a component.
export const { accentGradient } = kitThemes[defaultThemeName];

export function useKit() {
  const [{ darkMode, themeName }] = useMaterialUIController();

  return useMemo(() => {
    const named = kitThemes[themeName] ?? kitThemes[defaultThemeName];
    const kitTheme = darkMode ? { ...named, ...named.darkFills } : named;
    const mode = darkMode ? "dark" : "light";
    return {
      darkMode,
      themeName,
      c: kitTheme.palettes[mode],
      tone: (name) => kitTheme.tones[mode][name] ?? kitTheme.tones[mode].neutral,
      accentGradient: kitTheme.accentGradient,
      onAccent: kitTheme.onAccent,
      hero: kitTheme.hero,
      avatarFills: kitTheme.avatarFills,
      onAvatar: kitTheme.onAvatar,
    };
  }, [darkMode, themeName]);
}
