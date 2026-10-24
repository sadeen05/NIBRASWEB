import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

// Only the Latin and Arabic subsets the product actually renders are imported.
// The full `400.css` entry point ships every subset (Cyrillic, Greek, Vietnamese,
// ...) and every legacy `.woff`, which tripled the font payload for no benefit.
import '@fontsource/inter/latin-400.css';
import '@fontsource/inter/latin-500.css';
import '@fontsource/inter/latin-600.css';
import '@fontsource/inter/latin-700.css';
import '@fontsource/tajawal/arabic-400.css';
import '@fontsource/tajawal/arabic-500.css';
import '@fontsource/tajawal/arabic-700.css';

import '@/i18n';
import '@/styles/global.css';
import App from '@/app/App';

const container = document.getElementById('root');

if (!container) {
  throw new Error('Root container #root was not found.');
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
