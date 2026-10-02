// Which sidenavColors accent, when used as the *whole* sidenav's background
// (Configurator's "Sidenav Style" row) or as the active nav item's pill
// (Configurator's "Sidenav Colors" row), is light enough to need dark text/
// icons/divider instead of the white ones every other sidenav background
// (dark, or the other tints) uses.
//
// Derived from WCAG relative luminance of each gradient's "main" stop vs.
// the theme's white/dark.main text colors — currently empty because every
// accent (steel, slate, teal, sage, amber, mauve; see colors.js) is dark
// enough to clear 4.5:1 against white text on its own. Kept as a named list
// rather than deleted so a future lighter accent has a documented place to
// register the exception instead of silently rendering illegibly.
const sidenavTintsNeedingDarkText = [];

export default sidenavTintsNeedingDarkText;
