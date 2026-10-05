/* PlayFab Game Manager-styled primitives. Each component renders a class from
   globals.css so the visual language stays consistent across pages. */

export function Card({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return <div className={`card ${className}`}>{children}</div>;
}

export function CardHeader({ title, action }: { title: string; action?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between mb-4">
      <h2 className="h3" style={{ margin: 0 }}>{title}</h2>
      {action}
    </div>
  );
}

export function StatCard({ label, value, sub, color = "primary" }: {
  label: string;
  value: string | number;
  sub?: string;
  color?: "primary" | "secondary" | "success" | "error" | "warning" | "surface";
}) {
  const valueColor =
    color === "primary"   ? "rgb(var(--color-primary-500))"   :
    color === "secondary" ? "rgb(var(--color-secondary-500))" :
    color === "success"   ? "rgb(var(--color-success-700))"   :
    color === "error"     ? "rgb(var(--color-error-700))"     :
    color === "warning"   ? "rgb(var(--color-warning-700))"   :
                            "rgb(var(--color-surface-900))";
  return (
    <div className="card">
      <div style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--color-surface-700))", textTransform: "uppercase", letterSpacing: "0.04em", marginBottom: 6 }}>{label}</div>
      <div style={{ fontSize: 28, fontWeight: 600, color: valueColor, lineHeight: 1.1 }}>{value}</div>
      {sub && <div style={{ fontSize: 12, color: "rgb(var(--color-surface-700))", marginTop: 4 }}>{sub}</div>}
    </div>
  );
}

const badgeVariant: Record<string, string> = {
  primary:   "variant-soft-primary",
  secondary: "variant-soft-secondary",
  success:   "variant-soft-success",
  error:     "variant-soft-error",
  warning:   "variant-soft-warning",
  surface:   "variant-soft-surface",
};

export function Badge({ label, color = "primary" }: {
  label: string;
  color?: "primary" | "secondary" | "success" | "error" | "warning" | "surface";
}) {
  return <span className={`badge ${badgeVariant[color]}`}>{label}</span>;
}

export function Table({ headers, children }: { headers: string[]; children: React.ReactNode }) {
  return (
    <div className="table-container">
      <table className="table table-interactive">
        <thead>
          <tr>{headers.map(h => <th key={h}>{h}</th>)}</tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export function Tr({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return <tr className={className}>{children}</tr>;
}

export function Td({ children, mono = false }: { children: React.ReactNode; mono?: boolean }) {
  return <td className={mono ? "font-mono" : ""}>{children}</td>;
}

const btnVariant: Record<string, string> = {
  primary:   "btn-primary",
  secondary: "btn-default",
  ghost:     "btn-ghost",
  danger:    "btn-danger",
};

export function Btn({
  children, onClick, variant = "primary", disabled = false, type = "button", size = "sm",
}: {
  children: React.ReactNode;
  onClick?: () => void;
  variant?: "primary" | "secondary" | "ghost" | "danger";
  disabled?: boolean;
  type?: "button" | "submit";
  size?: "sm" | "md";
}) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`btn ${size === "sm" ? "btn-sm" : ""} ${btnVariant[variant]}`}
    >
      {children}
    </button>
  );
}

export function Input({ label, className = "", ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--color-surface-700))" }}>{label}</label>}
      <input {...props} className={`input ${className}`} />
    </div>
  );
}

export function Select({ label, children, className = "", ...props }: React.SelectHTMLAttributes<HTMLSelectElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--color-surface-700))" }}>{label}</label>}
      <select {...props} className={`select ${className}`}>{children}</select>
    </div>
  );
}

export function Textarea({ label, className = "", ...props }: React.TextareaHTMLAttributes<HTMLTextAreaElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--color-surface-700))" }}>{label}</label>}
      <textarea {...props} className={`textarea ${className}`} />
    </div>
  );
}

export function Modal({ open, onClose, title, children }: {
  open: boolean;
  onClose: () => void;
  title: string;
  children: React.ReactNode;
}) {
  if (!open) return null;
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-box" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3 className="h3" style={{ margin: 0 }}>{title}</h3>
          <button onClick={onClose} className="pf-topbar-icon-btn" style={{ color: "rgb(var(--color-surface-700))" }} aria-label="Close">×</button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  );
}
