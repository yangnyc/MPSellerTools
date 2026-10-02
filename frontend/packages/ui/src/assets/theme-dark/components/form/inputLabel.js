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
import colors from "assets/theme-dark/base/colors";
import typography from "assets/theme-dark/base/typography";

const { text, info } = colors;
const { size } = typography;

const inputLabel = {
  styleOverrides: {
    root: {
      fontSize: size.sm,
      color: text.main,
      lineHeight: 0.9,

      "&.Mui-focused": {
        color: info.main,
      },

      // Shrunk/floated label: font-size and line-height are intentionally
      // left unset here so MUI's own default transform
      // (translate(14px, -9px) scale(0.75) on .MuiInputLabel-shrink) does
      // the resizing. Overriding fontSize/lineHeight per-state (as this used
      // to) fights that transform's fixed offset and makes the floated
      // label sit low enough to overlap the outlined input's border.
    },

    sizeSmall: {
      fontSize: size.xs,
      lineHeight: 1.625,
    },
  },
};

export default inputLabel;
