// MP Seller Tools page kit — colour tokens.
//
// The kit (examples/Kit) is the set of page-level building blocks every
// signed-in page of both apps is composed from. It resolves every colour for
// the active named theme (context/themes.js) and its light or dark mode here,
// so pages never branch on the theme or on darkMode themselves.
//
// "ocean" uses the Classy palette (#845EC2 #4B4453 #B0A8B9 #C34A36 #FF8066)
// and tints and shades of it. The other themes are described at their blocks.
import { useMemo } from "react";

import { useMaterialUIController } from "context";
import { defaultThemeName } from "context/themes";

const kitThemes = {
  ocean: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F8F7FA",
        border: "#E3DEE8",
        borderStrong: "#CCC5D3",
        text: "#332E39",
        muted: "#7D7486",
        subtle: "#8F869A",
        hover: "rgba(75, 68, 83, 0.05)",
        accent: "#845EC2",
        shadow: "0 1px 2px rgba(42, 37, 47, 0.04), 0 12px 28px -18px rgba(42, 37, 47, 0.18)",
        navbar: "rgba(255, 255, 255, 0.82)",
      },
      dark: {
        surface: "#4B4453",
        surfaceAlt: "rgba(42, 37, 47, 0.45)",
        border: "rgba(176, 168, 185, 0.18)",
        borderStrong: "rgba(176, 168, 185, 0.32)",
        text: "#F5F2F8",
        muted: "#B0A8B9",
        subtle: "#A9A0B3",
        hover: "rgba(176, 168, 185, 0.08)",
        // #845EC2 lightened, to stay readable on the #4B4453 surface.
        accent: "#BFA6EA",
        shadow: "0 1px 2px rgba(20, 16, 24, 0.3), 0 12px 28px -18px rgba(20, 16, 24, 0.7)",
        navbar: "rgba(75, 68, 83, 0.82)",
      },
    },
    tones: {
      light: {
        info: { fg: "#6E4BAA", bg: "#EDE5F8", solid: "#845EC2" },
        primary: { fg: "#4B4453", bg: "#ECE9EF", solid: "#4B4453" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#B24A33", bg: "#FFE9E3", solid: "#FF8066" },
        error: { fg: "#9E3324", bg: "#F9E1DC", solid: "#C34A36" },
        neutral: { fg: "#4B4453", bg: "#F1EEF4", solid: "#7D7486" },
      },
      dark: {
        info: { fg: "#C9B4EF", bg: "rgba(132, 94, 194, 0.24)", solid: "#A07FD6" },
        primary: { fg: "#D8D2DE", bg: "rgba(176, 168, 185, 0.16)", solid: "#B0A8B9" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#FFB3A1", bg: "rgba(255, 128, 102, 0.16)", solid: "#FF8066" },
        error: { fg: "#F4AA9D", bg: "rgba(195, 74, 54, 0.28)", solid: "#D9624E" },
        neutral: { fg: "#D8D2DE", bg: "rgba(176, 168, 185, 0.16)", solid: "#B0A8B9" },
      },
    },
    // The fills below are fixed in both modes.
    accentGradient: "linear-gradient(135deg, #845EC2 0%, #4B4453 100%)",
    onAccent: "#fff",
    hero: {
      background: [
        "radial-gradient(36rem 22rem at 8% 0%, rgba(132, 94, 194, 0.34), transparent 60%)",
        "radial-gradient(32rem 22rem at 96% 100%, rgba(255, 128, 102, 0.2), transparent 60%)",
        "linear-gradient(155deg, #2A252F 0%, #3A3440 50%, #4B4453 100%)",
      ].join(", "),
      border: "rgba(176, 168, 185, 0.18)",
      grid: "rgba(176, 168, 185, 0.09)",
      eyebrow: "#FF8066",
      subtitle: "rgba(241, 238, 244, 0.85)",
    },
    avatarFills: [
      "linear-gradient(135deg, #845EC2, #4B4453)",
      "linear-gradient(135deg, #C34A36, #4B4453)",
      "linear-gradient(135deg, #A07FD6, #845EC2)",
      "linear-gradient(135deg, #E8674C, #C34A36)",
      "linear-gradient(135deg, #7D7486, #4B4453)",
    ],
    onAvatar: "#fff",
  },
  // "stepwise" (Navy Amber) follows Superdesign's "Stepwise" wizard design:
  // navy surfaces, cream text, and amber as the only accent. The design is
  // dark only; the light mode here is its colours on a cream page.
  stepwise: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#FAF7F0",
        border: "#E8E2D3",
        borderStrong: "#D3CAB4",
        text: "#0B1F3A",
        muted: "#5A6B85",
        subtle: "#7C8DA8",
        hover: "rgba(11, 31, 58, 0.04)",
        accent: "#B07D0B",
        shadow: "0 1px 2px rgba(8, 23, 41, 0.05), 0 12px 28px -18px rgba(8, 23, 41, 0.2)",
        navbar: "rgba(255, 255, 255, 0.86)",
      },
      dark: {
        surface: "#102A4D",
        surfaceAlt: "rgba(8, 23, 41, 0.5)",
        border: "rgba(244, 239, 227, 0.1)",
        borderStrong: "rgba(244, 239, 227, 0.2)",
        text: "#F4EFE3",
        muted: "#9FB0C8",
        subtle: "#6F84A3",
        hover: "rgba(244, 239, 227, 0.05)",
        accent: "#F0B429",
        shadow: "0 1px 2px rgba(0, 0, 0, 0.25), 0 16px 32px -20px rgba(0, 0, 0, 0.6)",
        navbar: "rgba(11, 31, 58, 0.86)",
      },
    },
    tones: {
      light: {
        info: { fg: "#8A6208", bg: "#FBF0D0", solid: "#F0B429" },
        primary: { fg: "#0B1F3A", bg: "#E4E9F1", solid: "#102A4D" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#9A4D0C", bg: "#FBE6D2", solid: "#D9772B" },
        error: { fg: "#B91C1C", bg: "#FEE2E2", solid: "#DC2626" },
        neutral: { fg: "#3D4F6B", bg: "#EEF1F6", solid: "#6F84A3" },
      },
      dark: {
        info: { fg: "#F7D479", bg: "rgba(240, 180, 41, 0.1)", solid: "#F0B429" },
        primary: { fg: "#CDD6E3", bg: "rgba(205, 214, 227, 0.1)", solid: "#CDD6E3" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#F7A05E", bg: "rgba(247, 160, 94, 0.14)", solid: "#E8853A" },
        error: { fg: "#FCA5A5", bg: "rgba(248, 113, 113, 0.16)", solid: "#F87171" },
        neutral: { fg: "#CDD6E3", bg: "rgba(159, 176, 200, 0.14)", solid: "#9FB0C8" },
      },
    },
    accentGradient: "linear-gradient(180deg, #F7C948 0%, #F0B429 100%)",
    onAccent: "#081729",
    hero: {
      background: "linear-gradient(to right bottom, #163661, #081729)",
      border: "rgba(244, 239, 227, 0.15)",
      grid: "rgba(244, 239, 227, 0.06)",
      eyebrow: "#F0B429",
      subtitle: "rgba(205, 214, 227, 0.9)",
    },
    avatarFills: ["linear-gradient(180deg, #F7C948, #F0B429)", "#F7D479", "#CDD6E3", "#9FB0C8"],
    onAvatar: "#081729",
  },

  // "matrix" is a Unix terminal as in The Matrix: phosphor green (#00FF41,
  // #008F11, #003B00) on black, with a faint green glow instead of shadows.
  // Its light mode is the same greens on a pale page.
  matrix: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F2FFF4",
        border: "#C9EBCF",
        borderStrong: "#9CD6A6",
        text: "#003B00",
        muted: "#2F6B3A",
        subtle: "#5C9166",
        hover: "rgba(0, 59, 0, 0.05)",
        accent: "#008F11",
        shadow: "none",
        navbar: "rgba(255, 255, 255, 0.86)",
      },
      dark: {
        surface: "#050A05",
        surfaceAlt: "#000000",
        border: "rgba(0, 255, 65, 0.22)",
        borderStrong: "rgba(0, 255, 65, 0.45)",
        text: "#00FF41",
        muted: "#00B32D",
        subtle: "#008F11",
        hover: "rgba(0, 255, 65, 0.08)",
        accent: "#00FF41",
        shadow: "0 0 24px -8px rgba(0, 255, 65, 0.35)",
        navbar: "rgba(0, 0, 0, 0.86)",
      },
    },
    tones: {
      light: {
        info: { fg: "#006B0D", bg: "#DDF7E1", solid: "#008F11" },
        primary: { fg: "#003B00", bg: "#E3F2E5", solid: "#003B00" },
        success: { fg: "#006B0D", bg: "#DDF7E1", solid: "#008F11" },
        warning: { fg: "#7A5C00", bg: "#FBF3CF", solid: "#C9A400" },
        error: { fg: "#B3261E", bg: "#FBE3E1", solid: "#D93025" },
        neutral: { fg: "#2F6B3A", bg: "#EAF5EC", solid: "#6FA378" },
      },
      dark: {
        info: { fg: "#00FF41", bg: "rgba(0, 255, 65, 0.12)", solid: "#00FF41" },
        primary: { fg: "#7DFF9B", bg: "rgba(0, 255, 65, 0.08)", solid: "#7DFF9B" },
        success: { fg: "#00FF41", bg: "rgba(0, 255, 65, 0.12)", solid: "#00FF41" },
        warning: { fg: "#FFD60A", bg: "rgba(255, 214, 10, 0.12)", solid: "#FFD60A" },
        error: { fg: "#FF5F56", bg: "rgba(255, 95, 86, 0.14)", solid: "#FF5F56" },
        neutral: { fg: "#00B32D", bg: "rgba(0, 143, 17, 0.16)", solid: "#008F11" },
      },
    },
    accentGradient: "#00FF41",
    onAccent: "#000000",
    hero: {
      background: "#000000",
      border: "rgba(0, 255, 65, 0.5)",
      grid: "rgba(0, 255, 65, 0.1)",
      eyebrow: "#00FF41",
      subtitle: "rgba(0, 255, 65, 0.75)",
    },
    avatarFills: ["#00FF41", "#00C832", "#008F11", "#7DFF9B"],
    onAvatar: "#000000",
  },
};
// The default theme's accent fill, for callers outside a component.
export const { accentGradient } = kitThemes[defaultThemeName];

export function useKit() {
  const [{ darkMode, themeName }] = useMaterialUIController();

  return useMemo(() => {
    const kitTheme = kitThemes[themeName] ?? kitThemes[defaultThemeName];
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
