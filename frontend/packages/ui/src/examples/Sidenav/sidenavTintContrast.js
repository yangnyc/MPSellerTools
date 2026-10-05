// Which sidenavColors accent, when used as the *whole* sidenav's background
// (Configurator's "Sidenav Style" row) or as the active nav item's pill
// (Configurator's "Sidenav Colors" row), is light enough to need dark text/
// icons/divider instead of the white ones every other sidenav background
// (dark, or the other tints) uses.
//
// Derived from WCAG relative luminance of each gradient's "main" stop vs.
// the theme's white/dark.main text colors. The original six accents (steel,
// slate, teal, sage, amber, mauve; see colors.js) are dark enough to clear
// 4.5:1 against white text on their own; "gold", the Noir Gold theme's
// accent, is not.
const sidenavTintsNeedingDarkText = ["gold"];

export default sidenavTintsNeedingDarkText;
