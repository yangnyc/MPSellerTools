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
//
// `sidenav` is the theme's own set of sidenav swatches, drawn from its
// palette. `colors` are the ones offered as "Sidenav Colors" (the fill of the
// active item) and `tints` the ones offered as "Sidenav Style" after Dark and
// White (the fill of the whole sidenav). A swatch is a gradient from `main`
// to `state`; `darkText` marks one too light for white text. Swatch names
// are saved on the profile with the theme, and no two themes share one.
export const defaultThemeName = "ocean";

export const themeOptions = [
  {
    id: "ocean",
    name: "Default",
    description: "Navy and steel blue, with coral.",
    swatch: ["#192755", "#3B6695", "#F36152"],
    preset: { darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "harbor" },
    sidenav: {
      swatches: {
        harbor: { main: "#3B6695", state: "#2E5279" },
        tide: { main: "#728FAD", state: "#5C7A99", darkText: true },
        mist: { main: "#ABC6D3", state: "#93B3C3", darkText: true },
        coral: { main: "#F36152", state: "#E04A3B", darkText: true },
        crimson: { main: "#B71443", state: "#980E36" },
        sunset: { main: "#FEB663", state: "#F5A247", darkText: true },
        indigo: { main: "#33426F", state: "#192755" },
      },
      colors: ["harbor", "tide", "mist", "coral", "crimson", "sunset"],
      tints: ["indigo", "harbor", "crimson", "mist"],
    },
  },
  {
    id: "sapphire",
    name: "Sapphire Ash",
    description: "Sapphire on ash white, with rose.",
    swatch: ["#35627A", "#E5AEA9", "#B46258"],
    preset: { darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "rose" },
    sidenav: {
      swatches: {
        rose: { main: "#E5AEA9", state: "#DB9A94", darkText: true },
        brick: { main: "#A9574D", state: "#8F4038" },
        periwinkle: { main: "#A6A9D0", state: "#9093C0", darkText: true },
        iris: { main: "#6B6FA8", state: "#585C92" },
        pewter: { main: "#8E9A98", state: "#788583", darkText: true },
        ink: { main: "#1F3B4A", state: "#14242D" },
      },
      colors: ["rose", "brick", "periwinkle", "iris", "pewter", "ink"],
      tints: ["ink", "iris", "brick", "rose"],
    },
  },
  {
    id: "astro",
    name: "Astro Novalite",
    description: "Night greys and slate blue, with cream.",
    swatch: ["#1E1F2A", "#8A9DB2", "#F5E8C7"],
    preset: { darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "cream" },
    sidenav: {
      swatches: {
        cream: { main: "#F5E8C7", state: "#E9D9B0", darkText: true },
        sand: { main: "#D9C08A", state: "#C7AB6E", darkText: true },
        frost: { main: "#C9D2DD", state: "#B3BFCC", darkText: true },
        balihai: { main: "#8A9DB2", state: "#74889E", darkText: true },
        tempest: { main: "#4E6580", state: "#3F546C" },
        shuttle: { main: "#5C6575", state: "#4A5261" },
        graphite: { main: "#3A3F4B", state: "#2C303B" },
      },
      colors: ["cream", "sand", "frost", "balihai", "tempest", "shuttle"],
      tints: ["graphite", "shuttle", "balihai", "cream"],
    },
  },
];

// A profile saved before themes had names, or naming a theme this build
// doesn't have, falls back to the default.
export const resolveThemeName = (name) =>
  themeOptions.some((option) => option.id === name) ? name : defaultThemeName;

const themeOption = (name) => themeOptions.find((option) => option.id === resolveThemeName(name));

// The theme's sidenav swatches as gradient pairs, for its MUI palette.
export const sidenavGradients = (themeName) =>
  Object.fromEntries(
    Object.entries(themeOption(themeName).sidenav.swatches).map(([name, { main, state }]) => [
      name,
      { main, state },
    ])
  );

// Every swatch that is too light for white text.
export const sidenavSwatchesNeedingDarkText = themeOptions.flatMap(({ sidenav }) =>
  Object.keys(sidenav.swatches).filter((name) => sidenav.swatches[name].darkText)
);

// A profile can hold a swatch the theme does not offer: one saved before
// themes had their own, or one from another theme. The colour then falls
// back to the theme's preset and the tint to none.
export function resolveSidenav(themeName, { sidenavColor, sidenavTint }) {
  const { preset, sidenav } = themeOption(themeName);
  return {
    sidenavColor: sidenav.colors.includes(sidenavColor) ? sidenavColor : preset.sidenavColor,
    sidenavTint: sidenav.tints.includes(sidenavTint) ? sidenavTint : null,
  };
}
