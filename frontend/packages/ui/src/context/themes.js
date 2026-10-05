// The named themes a user can pick in the Display Settings panel.
//
// A theme is a whole look: the page kit's colours (examples/Kit/tokens.js)
// and the MUI theme (assets/themes) are both keyed by these ids. The id is
// what gets saved on the user's profile, so it must stay lowercase a–z (the
// hosts validate it the same way as the sidenav swatch names).
//
// `preset` is the light/dark and sidenav choice applied when the theme is
// picked; the user can still change those afterwards.
export const defaultThemeName = "ocean";

export const themeOptions = [
  {
    id: "ocean",
    name: "Ocean",
    description: "Navy, sky and indigo. The original look.",
    swatch: ["#0F172A", "#0284C7", "#4338CA"],
    preset: { darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "steel" },
  },
  {
    id: "noir",
    name: "Noir Gold",
    description: "Minimalist black with gold accents.",
    swatch: ["#0A0A0A", "#D4AF37", "#F5F1E6"],
    preset: { darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "gold" },
  },
];

// A profile saved before themes had names, or naming a theme this build
// doesn't have, falls back to the default.
export const resolveThemeName = (name) =>
  themeOptions.some((option) => option.id === name) ? name : defaultThemeName;
