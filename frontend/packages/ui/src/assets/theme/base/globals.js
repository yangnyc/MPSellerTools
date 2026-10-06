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

// Material Dashboard 2 React Base Styles
import colors from "assets/theme/base/colors";

const { info, dark, white } = colors;

// The options of a native dropdown. MUI gives them the palette's paper colour
// (white in every theme here) with a class selector, so this has to be more
// specific than that to win.
export const nativeOptionSelector = ["option", "optgroup"]
  .map((tag) => `select.MuiNativeSelect-select:not([multiple]) ${tag}, select ${tag}`)
  .join(", ");

const globals = {
  html: {
    scrollBehavior: "smooth",
    overflowX: "hidden",
  },
  // An open dropdown's list, which the browser draws itself; see the dark theme's globals.
  [nativeOptionSelector]: {
    backgroundColor: white.main,
    color: dark.main,
  },
  "*, *::before, *::after": {
    margin: 0,
    padding: 0,
  },
  "a, a:link, a:visited": {
    textDecoration: "none !important",
  },
  "a.link, .link, a.link:link, .link:link, a.link:visited, .link:visited": {
    color: `${dark.main} !important`,
    transition: "color 150ms ease-in !important",
  },
  "a.link:hover, .link:hover, a.link:focus, .link:focus": {
    color: `${info.main} !important`,
  },
  // body: {
  //   overflow: "auto !important",
  // },
};

export default globals;
