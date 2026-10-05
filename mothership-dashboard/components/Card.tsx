export function Card({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={`card p-4 ${className}`}>
      {children}
    </div>
  );
}

export function CardHeader({ title, action }: { title: string; action?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between mb-4">
      <h2 className="h4">{title}</h2>
      {action}
    </div>
  );
}

export function StatCard({ label, value, sub, color = "primary" }: {
  label: string;
  value: string | number;
  sub?: string;
  color?: "primary" | "secondary" | "success" | "error" | "warning";
}) {
  return (
    <div className="card p-4">
      <p className="text-xs font-medium uppercase tracking-wider mb-1" style={{ color: "rgb(var(--color-surface-300))" }}>{label}</p>
      <p className="text-3xl font-bold" style={{ color: `rgb(var(--color-${color}-500))` }}>{value}</p>
      {sub && <p className="text-xs mt-1" style={{ color: "rgb(var(--color-surface-400))" }}>{sub}</p>}
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
  return (
    <span className={`badge ${badgeVariant[color]}`}>{label}</span>
  );
}

export function Table({ headers, children }: { headers: string[]; children: React.ReactNode }) {
  return (
    <div className="table-container">
      <table className="table table-interactive">
        <thead>
          <tr>
            {headers.map(h => <th key={h}>{h}</th>)}
          </tr>
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
  return (
    <td className={mono ? "font-mono" : ""}>{children}</td>
  );
}

const btnVariant: Record<string, string> = {
  primary:   "variant-filled-primary",
  secondary: "variant-filled-secondary",
  ghost:     "variant-ghost-surface",
  danger:    "variant-filled-error",
};

export function Btn({
  children, onClick, variant = "primary", disabled = false, type = "button"
}: {
  children: React.ReactNode;
  onClick?: () => void;
  variant?: "primary" | "secondary" | "ghost" | "danger";
  disabled?: boolean;
  type?: "button" | "submit";
}) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`btn btn-sm ${btnVariant[variant]}`}
    >
      {children}
    </button>
  );
}

export function Input({ label, className = "", ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label className="text-xs font-medium" style={{ color: "rgb(var(--color-surface-300))" }}>{label}</label>}
      <input {...props} className={`input ${className}`} />
    </div>
  );
}

export function Select({ label, children, className = "", ...props }: React.SelectHTMLAttributes<HTMLSelectElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label className="text-xs font-medium" style={{ color: "rgb(var(--color-surface-300))" }}>{label}</label>}
      <select {...props} className={`select ${className}`}>
        {children}
      </select>
    </div>
  );
}

export function Textarea({ label, className = "", ...props }: React.TextareaHTMLAttributes<HTMLTextAreaElement> & { label?: string }) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label className="text-xs font-medium" style={{ color: "rgb(var(--color-surface-300))" }}>{label}</label>}
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
      <div className="modal-box" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between p-4" style={{ borderBottom: "1px solid rgb(var(--color-surface-600))" }}>
          <h3 className="h4">{title}</h3>
          <button onClick={onClose} className="text-xl leading-none" style={{ color: "rgb(var(--color-surface-400))" }}>&times;</button>
        </div>
        <div className="p-4">{children}</div>
      </div>
    </div>
  );
}
