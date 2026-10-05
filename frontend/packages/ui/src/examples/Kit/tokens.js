// MP Seller Tools page kit — colour tokens.
//
// The kit (examples/Kit) is the set of page-level building blocks every
// signed-in page of both apps is composed from. It resolves every colour for
// the active named theme (context/themes.js) and its light or dark mode here,
// so pages never branch on the theme or on darkMode themselves.
//
// "ocean" carries the login pages' navy / sky / indigo palette into the rest
// of the product. "noir" is the minimalist black and gold theme: flat fills,
// hairline borders, and no shadows or gradients.

import { useMemo } from "react";

import { useMaterialUIController } from "context";
import { defaultThemeName } from "context/themes";

const kitThemes = {
  ocean: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F8FAFC",
        border: "#E2E8F0",
        borderStrong: "#CBD5E1",
        text: "#0F172A",
        muted: "#64748B",
        subtle: "#94A3B8",
        hover: "rgba(15, 23, 42, 0.04)",
        accent: "#0284C7",
        shadow: "0 1px 2px rgba(15, 23, 42, 0.04), 0 12px 28px -18px rgba(15, 23, 42, 0.18)",
        navbar: "rgba(255, 255, 255, 0.82)",
      },
      dark: {
        surface: "#1E293B",
        surfaceAlt: "rgba(15, 23, 42, 0.45)",
        border: "rgba(148, 163, 184, 0.18)",
        borderStrong: "rgba(148, 163, 184, 0.32)",
        text: "#F1F5F9",
        muted: "#94A3B8",
        subtle: "#64748B",
        hover: "rgba(148, 163, 184, 0.08)",
        accent: "#38BDF8",
        shadow: "0 1px 2px rgba(2, 6, 23, 0.3), 0 12px 28px -18px rgba(2, 6, 23, 0.7)",
        navbar: "rgba(30, 41, 59, 0.82)",
      },
    },
    tones: {
      light: {
        info: { fg: "#0369A1", bg: "#E0F2FE", solid: "#0284C7" },
        primary: { fg: "#4338CA", bg: "#E0E7FF", solid: "#4F46E5" },
        success: { fg: "#047857", bg: "#D1FAE5", solid: "#059669" },
        warning: { fg: "#B45309", bg: "#FEF3C7", solid: "#D97706" },
        error: { fg: "#B91C1C", bg: "#FEE2E2", solid: "#DC2626" },
        neutral: { fg: "#475569", bg: "#F1F5F9", solid: "#64748B" },
      },
      dark: {
        info: { fg: "#7DD3FC", bg: "rgba(56, 189, 248, 0.14)", solid: "#38BDF8" },
        primary: { fg: "#A5B4FC", bg: "rgba(129, 140, 248, 0.16)", solid: "#818CF8" },
        success: { fg: "#6EE7B7", bg: "rgba(52, 211, 153, 0.14)", solid: "#34D399" },
        warning: { fg: "#FCD34D", bg: "rgba(251, 191, 36, 0.14)", solid: "#FBBF24" },
        error: { fg: "#FCA5A5", bg: "rgba(248, 113, 113, 0.16)", solid: "#F87171" },
        neutral: { fg: "#CBD5E1", bg: "rgba(148, 163, 184, 0.16)", solid: "#94A3B8" },
      },
    },
    // The fills below are fixed in both modes, like the login pages' brand panel.
    accentGradient: "linear-gradient(135deg, #0284C7 0%, #4338CA 100%)",
    onAccent: "#fff",
    hero: {
      background: [
        "radial-gradient(36rem 22rem at 8% 0%, rgba(56, 189, 248, 0.22), transparent 60%)",
        "radial-gradient(32rem 22rem at 96% 100%, rgba(99, 102, 241, 0.3), transparent 60%)",
        "linear-gradient(155deg, #0F172A 0%, #14213D 50%, #1E2A4A 100%)",
      ].join(", "),
      border: "rgba(148, 163, 184, 0.18)",
      grid: "rgba(148, 163, 184, 0.09)",
      eyebrow: "#7DD3FC",
      subtitle: "rgba(226, 232, 240, 0.85)",
    },
    avatarFills: [
      "linear-gradient(135deg, #0284C7, #4338CA)",
      "linear-gradient(135deg, #0D9488, #0369A1)",
      "linear-gradient(135deg, #7C3AED, #4338CA)",
      "linear-gradient(135deg, #D97706, #BE123C)",
      "linear-gradient(135deg, #059669, #0F766E)",
    ],
    onAvatar: "#fff",
  },

  noir: {
    palettes: {
      light: {
        surface: "#FFFFFF",
        surfaceAlt: "#F6F3EA",
        border: "#E6E0CF",
        borderStrong: "#CFC6AC",
        text: "#0B0B0B",
        muted: "#5F5A4C",
        subtle: "#8C8573",
        hover: "rgba(11, 11, 11, 0.04)",
        accent: "#8A6D12",
        shadow: "none",
        navbar: "rgba(255, 255, 255, 0.86)",
      },
      dark: {
        surface: "#121212",
        surfaceAlt: "#0D0D0D",
        border: "rgba(212, 175, 55, 0.16)",
        borderStrong: "rgba(212, 175, 55, 0.34)",
        text: "#F5F1E6",
        muted: "#A39B87",
        subtle: "#6F6959",
        hover: "rgba(212, 175, 55, 0.07)",
        accent: "#D4AF37",
        shadow: "none",
        navbar: "rgba(10, 10, 10, 0.86)",
      },
    },
    tones: {
      light: {
        info: { fg: "#7A5F0E", bg: "#F5EDD2", solid: "#B8911F" },
        primary: { fg: "#0B0B0B", bg: "#ECE8DC", solid: "#0B0B0B" },
        success: { fg: "#1F6B43", bg: "#E3F1E8", solid: "#2E8B57" },
        warning: { fg: "#8F5510", bg: "#F8EBD6", solid: "#C27A1A" },
        error: { fg: "#9E2B25", bg: "#F8E3E1", solid: "#C2413A" },
        neutral: { fg: "#4F4A3E", bg: "#EFECE3", solid: "#7A7464" },
      },
      dark: {
        info: { fg: "#E3C766", bg: "rgba(212, 175, 55, 0.12)", solid: "#D4AF37" },
        primary: { fg: "#F5F1E6", bg: "rgba(245, 241, 230, 0.08)", solid: "#F5F1E6" },
        success: { fg: "#86C9A0", bg: "rgba(134, 201, 160, 0.12)", solid: "#5FB381" },
        warning: { fg: "#E0A458", bg: "rgba(224, 164, 88, 0.12)", solid: "#D08A2E" },
        error: { fg: "#E58B84", bg: "rgba(229, 139, 132, 0.12)", solid: "#D4645B" },
        neutral: { fg: "#BDB6A4", bg: "rgba(189, 182, 164, 0.1)", solid: "#8C8573" },
      },
    },
    accentGradient: "#D4AF37",
    onAccent: "#0B0B0B",
    hero: {
      background: "#0A0A0A",
      border: "rgba(212, 175, 55, 0.45)",
      grid: "transparent",
      eyebrow: "#D4AF37",
      subtitle: "rgba(245, 241, 230, 0.75)",
    },
    avatarFills: ["#D4AF37", "#E3C766", "#B8911F", "#CFC6AC"],
    onAvatar: "#0B0B0B",
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
