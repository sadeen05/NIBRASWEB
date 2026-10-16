import { Spin } from 'antd';
import type { ReactNode } from 'react';

interface FullPageStateProps {
  variant: 'loading' | 'info' | 'error';
  title: string;
  description?: string;
  /** Optional prominent code, e.g. "403" or "404". */
  code?: string;
  action?: ReactNode;
}

/** Centered state surface used for loading, error and empty full-page states. */
export function FullPageState({ variant, title, description, code, action }: FullPageStateProps) {
  return (
    <div className="state-page" role="status" aria-live="polite">
      <div className="state-card">
        {variant === 'loading' ? (
          <Spin size="large" />
        ) : (
          code && <div className="state-card__code">{code}</div>
        )}
        <h1 style={{ marginTop: variant === 'loading' ? 20 : 8, marginBottom: 8 }}>{title}</h1>
        {description && (
          <p style={{ color: 'var(--nb-text-secondary)', margin: 0 }}>{description}</p>
        )}
        {action && <div style={{ marginTop: 24 }}>{action}</div>}
      </div>
    </div>
  );
}
