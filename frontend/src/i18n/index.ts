import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

import { en } from './locales/en';
import { ar } from './locales/ar';

export const LANGUAGE_STORAGE_KEY = 'nibras.lang';

export const SUPPORTED_LANGUAGES = ['en', 'ar'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export function isRtlLanguage(language: string): boolean {
  return language.toLowerCase().startsWith('ar');
}

export function normalizeLanguage(language: string): SupportedLanguage {
  return isRtlLanguage(language) ? 'ar' : 'en';
}

export function applyDocumentLanguage(language: SupportedLanguage): void {
  if (typeof document === 'undefined') {
    return;
  }
  document.documentElement.lang = language;
  document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
}

function detectInitialLanguage(): SupportedLanguage {
  try {
    const stored = window.localStorage.getItem(LANGUAGE_STORAGE_KEY);
    if (stored) {
      return normalizeLanguage(stored);
    }
  } catch {
    // localStorage may be unavailable (private mode); fall through to the browser language.
  }

  const browserLanguage = typeof navigator !== 'undefined' ? navigator.language : 'en';
  return normalizeLanguage(browserLanguage);
}

const initialLanguage = detectInitialLanguage();

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    ar: { translation: ar },
  },
  lng: initialLanguage,
  fallbackLng: 'en',
  supportedLngs: SUPPORTED_LANGUAGES,
  interpolation: { escapeValue: false },
  returnNull: false,
});

applyDocumentLanguage(initialLanguage);

i18n.on('languageChanged', (language) => {
  const normalized = normalizeLanguage(language);
  applyDocumentLanguage(normalized);
  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, normalized);
  } catch {
    // Persisting the language is best-effort only.
  }
});

export default i18n;
