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
function collapseItem(theme, ownerState) {
  const { palette, transitions, breakpoints, boxShadows, borders, functions } = theme;
  const {
    active,
    whiteSidenav,
    tintIsLightBackground,
    darkMode,
    sidenavColor,
    accentMatchesTint,
    accentNeedsDarkText,
  } = ownerState;

  const { white, black, transparent, dark, grey, gradients } = palette;
  const activeColor = accentNeedsDarkText ? black.main : white.main;
  const inactiveColor = whiteSidenav || tintIsLightBackground ? dark.main : white.main;
  const { md } = boxShadows;
  const { borderWidth, borderRadius } = borders;
  const { pxToRem, rgba, linearGradient } = functions;

  return {
    background: active
      ? linearGradient(gradients[sidenavColor].main, gradients[sidenavColor].state)
      : transparent.main,
    color: active ? activeColor : inactiveColor,
    display: "flex",
    alignItems: "center",
    width: `calc(100% - ${pxToRem(32)})`,
    minWidth: 0,
    padding: `${pxToRem(8)} ${pxToRem(10)}`,
    margin: `${pxToRem(1.5)} ${pxToRem(16)}`,
    borderRadius: borderRadius.md,
    cursor: "pointer",
    userSelect: "none",
    whiteSpace: "nowrap",
    boxShadow: active && !whiteSidenav && !darkMode ? md : "none",
    // The accent pill's fill is an exact match for the sidenav's own
    // background when "Sidenav Colors" and "Sidenav Style" land on the same
    // tint (see accentMatchesTint in SidenavCollapse.jsx) — without this the
    // active item would carry no visible boundary at all.
    border: active && accentMatchesTint ? `${borderWidth[1]} solid ${activeColor}` : "none",
    [breakpoints.up("xl")]: {
      transition: transitions.create(["box-shadow", "background-color"], {
        easing: transitions.easing.easeInOut,
        duration: transitions.duration.shorter,
      }),
    },

    "&:hover, &:focus": {
      backgroundColor: () => {
        let backgroundValue;

        if (!active) {
          backgroundValue = rgba(whiteSidenav || tintIsLightBackground ? grey[400] : white.main, 0.2);
        }

        return backgroundValue;
      },
    },
  };
}

function collapseIconBox(theme, ownerState) {
  const { palette, transitions, borders, functions } = theme;
  const { whiteSidenav, tintIsLightBackground, active, accentNeedsDarkText } = ownerState;

  const { white, black, dark } = palette;
  const activeColor = accentNeedsDarkText ? black.main : white.main;
  const { borderRadius } = borders;
  const { pxToRem } = functions;

  const isLightBackground = whiteSidenav || tintIsLightBackground;

  return {
    minWidth: pxToRem(32),
    minHeight: pxToRem(32),
    color: active ? activeColor : isLightBackground ? dark.main : white.main,
    borderRadius: borderRadius.md,
    display: "grid",
    placeItems: "center",
    transition: transitions.create("margin", {
      easing: transitions.easing.easeInOut,
      duration: transitions.duration.standard,
    }),

    "& svg, svg g": {
      color: "inherit",
    },
  };
}

const collapseIcon = () => ({ color: "inherit" });

function collapseText(theme, ownerState) {
  const { typography, transitions, breakpoints, functions } = theme;
  const { miniSidenav, active } = ownerState;

  const { size, fontWeightRegular, fontWeightLight } = typography;
  const { pxToRem } = functions;

  return {
    marginLeft: pxToRem(10),

    [breakpoints.up("xl")]: {
      opacity: miniSidenav ? 0 : 1,
      maxWidth: miniSidenav ? 0 : "100%",
      marginLeft: miniSidenav ? 0 : pxToRem(10),
      transition: transitions.create(["opacity", "margin"], {
        easing: transitions.easing.easeInOut,
        duration: transitions.duration.standard,
      }),
    },

    // Room to shrink, so a long name wraps onto a second line instead of running under the pin and the arrow.
    minWidth: 0,

    "& span": {
      fontWeight: active ? fontWeightRegular : fontWeightLight,
      fontSize: size.sm,
      lineHeight: 1.25,
      whiteSpace: "normal",
    },
  };
}

export { collapseItem, collapseIconBox, collapseIcon, collapseText };
