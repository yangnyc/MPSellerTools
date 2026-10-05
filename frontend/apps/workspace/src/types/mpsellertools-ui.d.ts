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
  export function setWhiteSidenav(dispatch: any, value: boolean): void;
  export function setSidenavColor(dispatch: any, value: string): void;
  export function setTransparentNavbar(dispatch: any, value: boolean): void;
  export function setFixedNavbar(dispatch: any, value: boolean): void;
  export function setOpenConfigurator(dispatch: any, value: boolean): void;
  export function setDirection(dispatch: any, value: string): void;
  export function setLayout(dispatch: any, value: string): void;
  export function setDarkMode(dispatch: any, value: boolean): void;
  export function setThemeName(dispatch: any, value: string): void;
  export function applyThemeSettings(dispatch: any, value: any): void;
  export const defaultThemeSettings: any;
}

declare module "assets/*" {
  const value: any;
  export default value;
}

// The page kit (frontend/packages/ui/src/examples/Kit) has named exports, so
// it gets its own declaration instead of the "examples/*" default-export one.
declare module "examples/Kit" {
  export type KitTone = "info" | "primary" | "success" | "warning" | "error" | "neutral";
  export function useKit(): {
    darkMode: boolean;
    themeName: string;
    c: Record<string, string>;
    tone: (name: KitTone) => { fg: string; bg: string; solid: string };
  };
  export const accentGradient: string;
  export function formatMoney(value: number): string;
  export function formatDateTime(iso: string): string;
  export function timeAgo(iso: string): string;
  export function roleLabel(role: string): string;
  export const IconTile: React.ComponentType<any>;
  export const Surface: React.ComponentType<any>;
  export const Section: React.ComponentType<any>;
  export const PageHeader: React.ComponentType<any>;
  export const Hero: React.ComponentType<any>;
  export const StatCard: React.ComponentType<any>;
  export const StatusPill: React.ComponentType<any>;
  export const FilterTabs: React.ComponentType<any>;
  export const StateBlock: React.ComponentType<any>;
  export const InlineAlert: React.ComponentType<any>;
  export const InitialsAvatar: React.ComponentType<any>;
  export const Identity: React.ComponentType<any>;
  export const DetailList: React.ComponentType<any>;
  export const KitDialog: React.ComponentType<any>;
  export const AppPage: React.ComponentType<any>;
}
