/** Small locale-aware formatters shared by the land pages. */

export function formatDateTime(value: string | null, locale: string): string {
  if (!value) {
    return '—';
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }
  try {
    return parsed.toLocaleString(locale);
  } catch {
    return parsed.toISOString();
  }
}

/** A compact binary size; null renders as an em dash for legacy metadata rows. */
export function formatFileSize(bytes: number | null | undefined): string {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KiB`;
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
}

/** Null/undefined renders as an em dash: "not assessed" must never look like 0. */
export function formatDecimal(
  value: number | null | undefined,
  locale: string,
  maximumFractionDigits = 2,
): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—';
  }
  try {
    return value.toLocaleString(locale, { maximumFractionDigits });
  } catch {
    return String(value);
  }
}
