import Box from "@mui/material/Box";
import { keyframes, type Theme } from "@mui/material/styles";
import type { SystemStyleObject } from "@mui/system";

const slide = keyframes`
  0% { left: -40%; }
  100% { left: 100%; }
`;

type Tone = "info" | "success" | "warning" | "error";

// A progress bar that stays inside its own track. The theme's LinearProgress lets its bar spill out
// (it is styled for bars sized by width), so this draws its own: a track that clips, and a bar inside it.
// With no `value` it shows that something is under way without saying how far it has got.
export default function ProgressBar({ value, tone = "info", label, height = 6, sx }: {
  value?: number;
  tone?: Tone;
  label: string;
  height?: number;
  sx?: SystemStyleObject<Theme>;
}) {
  const known = value !== undefined;
  const percent = known ? Math.min(100, Math.max(0, Math.round(value))) : undefined;
  return (
    <Box
      role="progressbar"
      aria-label={label}
      aria-valuemin={known ? 0 : undefined}
      aria-valuemax={known ? 100 : undefined}
      aria-valuenow={percent}
      sx={(theme) => ({
        position: "relative",
        width: "100%",
        height,
        borderRadius: height / 2,
        overflow: "hidden",
        backgroundColor: theme.palette.mode === "dark" ? "rgba(255, 255, 255, 0.14)" : "rgba(0, 0, 0, 0.1)",
        ...sx,
      })}
    >
      <Box
        sx={(theme) => ({
          position: "absolute",
          top: 0,
          bottom: 0,
          borderRadius: height / 2,
          backgroundColor: theme.palette[tone].main,
          ...(known
            ? { left: 0, width: `${percent}%`, transition: "width 0.4s ease" }
            : { width: "40%", animation: `${slide} 1.2s ease-in-out infinite` }),
        })}
      />
    </Box>
  );
}
