// Which sidenav swatches, when used as the *whole* sidenav's background
// (Configurator's "Sidenav Style" row) or as the active nav item's pill
// (Configurator's "Sidenav Colors" row), are light enough to need dark text/
// icons/divider instead of the white ones every other sidenav background
// (dark, or the other tints) uses.
//
// Each theme marks its own in context/themes.js, from the WCAG contrast of
// the swatch's "main" stop against white text.
import { sidenavSwatchesNeedingDarkText } from "context/themes";

export default sidenavSwatchesNeedingDarkText;
