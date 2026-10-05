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

// prop-types is a library for typechecking of props.
import PropTypes from "prop-types";

// @mui material components
import ListItem from "@mui/material/ListItem";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import Icon from "@mui/material/Icon";

// Material Dashboard 2 React components
import MDBox from "components/MDBox";

// Custom styles for the SidenavCollapse
import {
  collapseItem,
  collapseIconBox,
  collapseIcon,
  collapseText,
} from "examples/Sidenav/styles/sidenavCollapse";
import sidenavTintsNeedingDarkText from "examples/Sidenav/sidenavTintContrast";

// Material Dashboard 2 React context
import { useMaterialUIController } from "context";

function SidenavCollapse({ icon, name, active = false, ...rest }) {
  const [controller] = useMaterialUIController();
  const { miniSidenav, whiteSidenav, sidenavTint, darkMode, sidenavColor } = controller;
  const tintIsLightBackground = sidenavTint && sidenavTintsNeedingDarkText.includes(sidenavTint);
  // When the chosen "Sidenav Colors" accent is the same hue as the current
  // "Sidenav Style" tint (e.g. both "steel"), the active pill's fill is
  // identical to the sidenav's own background and the highlight all but
  // disappears — collapseItem adds a visible edge in that case.
  const accentMatchesTint = Boolean(sidenavTint) && sidenavTint === sidenavColor;
  // The active pill is filled with the accent, so a light accent needs dark
  // text and icons on it.
  const accentNeedsDarkText = sidenavTintsNeedingDarkText.includes(sidenavColor);

  return (
    <ListItem component="li">
      <MDBox
        {...rest}
        sx={(theme) =>
          collapseItem(theme, {
            active,
            whiteSidenav,
            tintIsLightBackground,
            darkMode,
            sidenavColor,
            accentMatchesTint,
            accentNeedsDarkText,
          })
        }
      >
        <ListItemIcon
          sx={(theme) =>
            collapseIconBox(theme, {
              whiteSidenav,
              tintIsLightBackground,
              darkMode,
              active,
              accentNeedsDarkText,
            })
          }
        >
          {typeof icon === "string" ? (
            <Icon sx={(theme) => collapseIcon(theme, { active })}>{icon}</Icon>
          ) : (
            icon
          )}
        </ListItemIcon>

        <ListItemText
          primary={name}
          sx={(theme) =>
            collapseText(theme, {
              miniSidenav,
              whiteSidenav,
              active,
            })
          }
        />
      </MDBox>
    </ListItem>
  );
}

// Typechecking props for the SidenavCollapse
SidenavCollapse.propTypes = {
  icon: PropTypes.node.isRequired,
  name: PropTypes.string.isRequired,
  active: PropTypes.bool,
};

export default SidenavCollapse;
