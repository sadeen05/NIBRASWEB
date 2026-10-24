/**
 * Saves an already-authorized blob under a local display name. The bearer token was carried by
 * the download request's header, never by a URL, so the object URL below is only a local handle
 * that is revoked shortly after the click.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // Revoking synchronously can cancel the download in some browsers.
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
