/** Approval timestamps are UTC, including SQL values serialized without an offset. */
export function formatApprovalUtc(value: string | null, locale: string): string {
  if (!value) return '—';
  const instant = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : value + 'Z');
  if (Number.isNaN(instant.getTime())) return '—';
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium', timeStyle: 'medium', timeZone: 'UTC',
  }).format(instant) + ' UTC';
}