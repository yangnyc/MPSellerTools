import type { ImportProductRow } from "../api/types";

// Splits CSV text into rows of cells. Handles quoted cells, doubled quotes
// inside them, commas and line breaks within quotes, and both line endings.
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;
  // A byte order mark, as Excel writes one, is not part of the first header.
  const source = text.replace(/^﻿/, "");

  for (let i = 0; i < source.length; i += 1) {
    const char = source[i];
    if (quoted) {
      if (char === '"' && source[i + 1] === '"') {
        cell += '"';
        i += 1;
      } else if (char === '"') {
        quoted = false;
      } else {
        cell += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ",") {
      row.push(cell);
      cell = "";
    } else if (char === "\n" || char === "\r") {
      if (char === "\r" && source[i + 1] === "\n") i += 1;
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
    } else {
      cell += char;
    }
  }
  if (cell !== "" || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }
  // Blank lines carry nothing.
  return rows.filter((cells) => cells.some((value) => value.trim() !== ""));
}

// The column names a products file may use, by the field they fill.
const COLUMNS: Record<keyof ImportProductRow, string[]> = {
  sku: ["sku"],
  name: ["name", "title", "product"],
  price: ["price"],
  stockQuantity: ["stock", "stockquantity", "stock quantity", "quantity", "on hand"],
};

// Reads a products file: a header row naming the SKU, name, price and stock
// columns in any order, then one product per row. Throws with a message for
// the person when the header is missing a column, or when a price or stock is
// not a number: that is a slip in the file, better fixed there than guessed at.
export function parseProductsCsv(text: string): ImportProductRow[] {
  const [header, ...lines] = parseCsv(text);
  if (!header) throw new Error("The file is empty.");

  const names = header.map((value) => value.trim().toLowerCase());
  const index = Object.fromEntries(
    Object.entries(COLUMNS).map(([field, accepted]) => [field, names.findIndex((value) => accepted.includes(value))])
  ) as Record<keyof ImportProductRow, number>;
  const missing = Object.entries(index).filter(([, at]) => at < 0).map(([field]) => COLUMNS[field as keyof ImportProductRow][0]);
  if (missing.length > 0) {
    throw new Error(`The first row must name these columns: SKU, Name, Price, Stock. Missing: ${missing.join(", ")}.`);
  }
  if (lines.length === 0) throw new Error("The file has a header but no products.");

  // `line` is the row's number in the file, counting the header as row 1.
  const number = (value: string | undefined, line: number, what: string, whole: boolean) => {
    const cleaned = (value ?? "").trim().replace(/^\$/, "");
    const parsed = Number(cleaned);
    if (cleaned === "" || !Number.isFinite(parsed) || (whole && !Number.isInteger(parsed))) {
      throw new Error(`Row ${line}: the ${what} "${(value ?? "").trim()}" is not a ${whole ? "whole number" : "number"}.`);
    }
    return parsed;
  };
  return lines.map((cells, i) => ({
    sku: (cells[index.sku] ?? "").trim(),
    name: (cells[index.name] ?? "").trim(),
    price: number(cells[index.price], i + 2, "price", false),
    stockQuantity: number(cells[index.stockQuantity], i + 2, "stock", true),
  }));
}
