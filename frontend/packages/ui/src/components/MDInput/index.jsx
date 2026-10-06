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

import { forwardRef } from "react";

// prop-types is a library for typechecking of props
import PropTypes from "prop-types";

// Custom styles for MDInput
import MDInputRoot from "components/MDInput/MDInputRoot";

// MUI 9 replaced TextField's InputProps / inputProps / SelectProps /
// InputLabelProps / FormHelperTextProps with a single `slotProps`. Pages still
// pass the older names, so they are mapped onto it here, the one place every
// text field passes through.
const MDInput = forwardRef(
  (
    {
      error = false,
      success = false,
      disabled = false,
      InputProps,
      inputProps,
      SelectProps,
      InputLabelProps,
      FormHelperTextProps,
      slotProps,
      ...rest
    },
    ref
  ) => (
    <MDInputRoot
      {...rest}
      slotProps={{
        input: InputProps,
        htmlInput: inputProps,
        select: SelectProps,
        inputLabel: InputLabelProps,
        formHelperText: FormHelperTextProps,
        ...slotProps,
      }}
      ref={ref}
      ownerState={{ error, success, disabled }}
    />
  )
);

// Typechecking props for the MDInput
MDInput.propTypes = {
  error: PropTypes.bool,
  success: PropTypes.bool,
  disabled: PropTypes.bool,
};

export default MDInput;
