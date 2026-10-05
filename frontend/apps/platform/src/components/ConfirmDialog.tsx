import MDButton from "components/MDButton";
import { KitDialog, useKit } from "examples/Kit";

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  confirmColor?: "error" | "info" | "success" | "warning";
  onConfirm: () => void;
  onCancel: () => void;
};

export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Confirm",
  confirmColor = "error",
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const { c } = useKit();

  return (
    <KitDialog
      open={open}
      onClose={onCancel}
      maxWidth="xs"
      icon={confirmColor === "error" || confirmColor === "warning" ? "warning_amber" : "help_outline"}
      tone={confirmColor}
      title={title}
      actions={
        <>
          <MDButton variant="text" color="secondary" onClick={onCancel}>
            Cancel
          </MDButton>
          <MDButton variant="gradient" color={confirmColor} onClick={onConfirm}>
            {confirmLabel}
          </MDButton>
        </>
      }
    >
      <p style={{ fontSize: "0.9375rem", lineHeight: 1.6, color: c.muted }}>{message}</p>
    </KitDialog>
  );
}
