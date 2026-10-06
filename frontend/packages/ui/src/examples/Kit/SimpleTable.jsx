// MP Seller Tools page kit — a plain table with optional row selection.
//
// examples/Tables/DataTable resets its page and search whenever it is handed
// new rows, which makes it a poor fit for ticking rows on and off. This one
// shows every row it is given and leaves filtering to the page.

import PropTypes from "prop-types";

import Box from "@mui/material/Box";
import Checkbox from "@mui/material/Checkbox";

import { useKit } from "examples/Kit/tokens";

export function SimpleTable({ columns, rows, getRowId, getRowLabel, selection = null, emptyMessage = "Nothing to show." }) {
  const { c } = useKit();

  const selectable = selection ? rows.filter((row) => !selection.isSelectable || selection.isSelectable(row)) : [];
  const selectedCount = selectable.filter((row) => selection.selected.has(getRowId(row))).length;
  const allSelected = selectable.length > 0 && selectedCount === selectable.length;

  const toggleAll = () => {
    const next = new Set(selection.selected);
    selectable.forEach((row) => (allSelected ? next.delete(getRowId(row)) : next.add(getRowId(row))));
    selection.onChange(next);
  };

  const toggleRow = (id) => {
    const next = new Set(selection.selected);
    if (!next.delete(id)) {
      next.add(id);
    }
    selection.onChange(next);
  };

  const cell = { px: 3, py: 1.5, borderBottom: `1px solid ${c.border}`, textAlign: "left", verticalAlign: "middle" };
  const headCell = {
    ...cell,
    py: 1.25,
    fontSize: "0.6875rem",
    fontWeight: 700,
    letterSpacing: "0.06em",
    textTransform: "uppercase",
    whiteSpace: "nowrap",
    color: c.muted,
    backgroundColor: c.surfaceAlt,
  };
  const checkCell = { width: 48, pr: 0 };

  return (
    <Box sx={{ overflowX: "auto" }}>
      <Box component="table" sx={{ width: "100%", borderCollapse: "collapse", fontSize: "0.875rem", color: c.text }}>
        <thead>
          <tr>
            {selection && (
              <Box component="th" scope="col" sx={{ ...headCell, ...checkCell }}>
                <Checkbox
                  size="small"
                  checked={allSelected}
                  indeterminate={selectedCount > 0 && !allSelected}
                  disabled={selectable.length === 0}
                  onChange={toggleAll}
                  slotProps={{ input: { "aria-label": "Select all" } }}
                />
              </Box>
            )}
            {columns.map((column) => (
              <Box component="th" scope="col" key={column.key} sx={{ ...headCell, textAlign: column.align ?? "left" }}>
                {column.header}
              </Box>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <Box
                component="td"
                colSpan={columns.length + (selection ? 1 : 0)}
                sx={{ ...cell, py: 5, textAlign: "center", color: c.muted }}
              >
                {emptyMessage}
              </Box>
            </tr>
          )}
          {rows.map((row) => {
            const id = getRowId(row);
            const canSelect = selection && (!selection.isSelectable || selection.isSelectable(row));
            const isSelected = Boolean(selection?.selected.has(id));

            return (
              <Box component="tr" key={id} sx={{ backgroundColor: isSelected ? c.hover : "transparent" }}>
                {selection && (
                  <Box component="td" sx={{ ...cell, ...checkCell }}>
                    <Checkbox
                      size="small"
                      checked={isSelected}
                      disabled={!canSelect}
                      onChange={() => toggleRow(id)}
                      slotProps={{ input: { "aria-label": `Select ${getRowLabel ? getRowLabel(row) : id}` } }}
                    />
                  </Box>
                )}
                {columns.map((column) => (
                  <Box component="td" key={column.key} sx={{ ...cell, textAlign: column.align ?? "left" }}>
                    {column.render(row)}
                  </Box>
                ))}
              </Box>
            );
          })}
        </tbody>
      </Box>
    </Box>
  );
}

SimpleTable.propTypes = {
  columns: PropTypes.arrayOf(
    PropTypes.shape({
      key: PropTypes.string.isRequired,
      header: PropTypes.node,
      align: PropTypes.oneOf(["left", "right", "center"]),
      render: PropTypes.func.isRequired,
    })
  ).isRequired,
  rows: PropTypes.arrayOf(PropTypes.object).isRequired,
  getRowId: PropTypes.func.isRequired,
  getRowLabel: PropTypes.func,
  selection: PropTypes.shape({
    selected: PropTypes.instanceOf(Set).isRequired,
    onChange: PropTypes.func.isRequired,
    isSelectable: PropTypes.func,
  }),
  emptyMessage: PropTypes.node,
};
