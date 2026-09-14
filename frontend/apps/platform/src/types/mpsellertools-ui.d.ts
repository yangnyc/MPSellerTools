// The shared Creative-Tim-derived UI package (frontend/packages/ui) is plain
// JS/JSX ported from the upstream template (see docs/template-adaptation.md)
// and is not individually typed. These ambient declarations let application
// TypeScript code import it via the same bare specifiers the components use
// internally, resolved through the "components/*" etc. path aliases in
// tsconfig.app.json and vite.config.ts.
declare module "components/*" {
  const value: any;
  export default value;
}

declare module "examples/*" {
  const value: any;
  export default value;
}

declare module "context" {
  export const MaterialUIControllerProvider: any;
  export function useMaterialUIController(): [any, (action: any) => void];
  export function setMiniSidenav(dispatch: any, value: boolean): void;
  export function setTransparentSidenav(dispatch: any, value: boolean): void;
  export function setWhiteSidenav(dispatch: any, value: boolean): void;
  export function setSidenavColor(dispatch: any, value: string): void;
  export function setTransparentNavbar(dispatch: any, value: boolean): void;
  export function setFixedNavbar(dispatch: any, value: boolean): void;
  export function setOpenConfigurator(dispatch: any, value: boolean): void;
  export function setDirection(dispatch: any, value: string): void;
  export function setLayout(dispatch: any, value: string): void;
  export function setDarkMode(dispatch: any, value: boolean): void;
}

declare module "assets/*" {
  const value: any;
  export default value;
}
