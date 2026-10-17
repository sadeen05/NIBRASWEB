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

export function formatDecimal(value: number, locale: string, maximumFractionDigits = 2): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—';
  }
  try {
    return value.toLocaleString(locale, { maximumFractionDigits });
  } catch {
    return String(value);
  }
}
