// The named themes a user can pick in the Display Settings panel.
//
// A theme is a whole look: the page kit's colours (examples/Kit/tokens.js)
// and the MUI theme (assets/themes) are both keyed by these ids. The id is
// what gets saved on the user's profile, so it must stay lowercase a–z (the
// hosts validate it the same way as the sidenav swatch names). "ocean" is
// older than its colours: it was renamed and recoloured, and keeping the id
// keeps every saved profile working.
//
// `preset` is the light/dark and sidenav choice applied when the theme is
// picked; the user can still change those afterwards.
export const defaultThemeName = "ocean";

export const themeOptions = [
  {
    id: "ocean",
    name: "Default",
    description: "Purple, plum and coral.",
    swatch: ["#4B4453", "#845EC2", "#FF8066"],
    preset: { darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "steel" },
  },
  {
    id: "stepwise",
    name: "Navy Amber",
    description: "Dark navy with one warm amber accent.",
    swatch: ["#081729", "#102A4D", "#F0B429"],
    preset: { darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "gold" },
  },
  {
    id: "matrix",
    name: "Matrix",
    description: "Green phosphor on black, like a Unix terminal.",
    swatch: ["#000000", "#008F11", "#00FF41"],
    preset: { darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "mint" },
  },
];

// A profile saved before themes had names, or naming a theme this build
// doesn't have, falls back to the default.
export const resolveThemeName = (name) =>
  themeOptions.some((option) => option.id === name) ? name : defaultThemeName;
