import axios from 'axios';

import i18n from '@/i18n';
import type { ProblemDetails } from '@/types/api';

export type ApiErrorKind =
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'validation'
  | 'conflict'
  | 'rateLimited'
  | 'server'
  /** 502/503/504: a proxy or gateway answered because the API is unreachable. */
  | 'unavailable'
  | 'network'
  | 'canceled'
  | 'unknown';

interface ApiErrorInit {
  kind: ApiErrorKind;
  message: string;
  status?: number | null;
  errorCode?: string | null;
  traceId?: string | null;
  title?: string | null;
  detail?: string | null;
  validationErrors?: Record<string, string[]> | null;
}

/**
 * Normalized API error. All failures from {@link apiClient} are rejected with an
 * `ApiError`, never a raw Axios error.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind;
  readonly status: number | null;
  readonly errorCode: string | null;
  readonly traceId: string | null;
  readonly title: string | null;
  /** The backend's own `detail`; null when the response carried none. */
  readonly detail: string | null;
  readonly validationErrors: Record<string, string[]> | null;

  constructor(init: ApiErrorInit) {
    super(init.message);
    this.name = 'ApiError';
    this.kind = init.kind;
    this.status = init.status ?? null;
    this.errorCode = init.errorCode ?? null;
    this.traceId = init.traceId ?? null;
    this.title = init.title ?? null;
    this.detail = init.detail ?? null;
    this.validationErrors = init.validationErrors ?? null;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/**
 * Resolves user-visible copy through the shared i18n instance. The English
 * fallback is only used if the key is missing, so a translation gap can never
 * surface a raw i18n key to the user.
 */
function translate(key: string, fallback: string): string {
  const value: unknown = i18n.t(key);
  return typeof value === 'string' && value.length > 0 && value !== key ? value : fallback;
}

function translateStatus(key: string, status: number, fallback: string): string {
  const value: unknown = i18n.t(key, { status });
  return typeof value === 'string' && value.length > 0 && value !== key ? value : fallback;
}

function kindFromStatus(status: number): ApiErrorKind {
  switch (status) {
    case 400:
      return 'validation';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'conflict';
    case 429:
      return 'rateLimited';
    case 502:
    case 503:
    case 504:
      return 'unavailable';
    default:
      return status >= 500 ? 'server' : 'unknown';
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function extractProblem(data: unknown): ProblemDetails | null {
  return isRecord(data) ? (data as ProblemDetails) : null;
}

/** Flattens the ASP.NET Core `errors` dictionary into a stable string-array map. */
function extractValidationErrors(
  problem: ProblemDetails | null,
): Record<string, string[]> | null {
  if (!problem || !isRecord(problem.errors)) {
    return null;
  }

  const result: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(problem.errors)) {
    if (Array.isArray(messages)) {
      result[field] = messages.filter((entry): entry is string => typeof entry === 'string');
    } else if (typeof messages === 'string') {
      result[field] = [messages];
    }
  }

  return Object.keys(result).length > 0 ? result : null;
}

/** The backend returns a bare `{ message }` body for some rejections (401, 409). */
function extractMessage(problem: ProblemDetails | null): string | null {
  return problem ? asString(problem.message) : null;
}

/**
 * First non-empty entry of the automatic-validation `errors` dictionary. ASP.NET
 * Core's generic `title` says only "One or more validation errors occurred.",
 * so the specific server message is preferred when one is present.
 */
function firstValidationMessage(
  validationErrors: Record<string, string[]> | null,
): string | null {
  if (!validationErrors) {
    return null;
  }

  for (const messages of Object.values(validationErrors)) {
    const first = messages.find((message) => message.trim().length > 0);
    if (first) {
      return first;
    }
  }

  return null;
}

/**
 * Last-resort, localized copy for responses that carry no usable backend
 * message (for example an empty body from the Vite proxy). 502/503/504 are
 * reported as unreachable; every other status keeps its own wording so a
 * failure is never mislabelled as a connectivity problem.
 */
function fallbackMessage(kind: ApiErrorKind, status: number, hasValidationErrors: boolean): string {
  if (hasValidationErrors) {
    return translate('apiErrors.validation', 'One or more fields are invalid.');
  }

  switch (kind) {
    case 'network':
    case 'unavailable':
      return translate(
        'apiErrors.network',
        'Cannot reach the server. Make sure the backend is running and try again.',
      );
    case 'rateLimited':
      return translate(
        'apiErrors.rateLimited',
        'Too many requests. Please wait a moment and try again.',
      );
    case 'server':
      return translate('apiErrors.server', 'The server encountered an error. Please try again later.');
    default:
      return translateStatus('apiErrors.requestFailed', status, `Request failed (status ${status}).`);
  }
}

/**
 * Converts any thrown value into an {@link ApiError}. Handles absent responses
 * (network failures), cancellations, 204-style empty bodies, the backend
 * ProblemDetails contract, the bare `{ message }` shape and the automatic
 * model-validation `errors` dictionary. Backend text always wins; localized
 * copy is only used when the response has nothing usable to show.
 */
export function normalizeError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (axios.isAxiosError(error)) {
    const { response, code } = error;

    if (!response) {
      const canceled = code === 'ERR_CANCELED';
      return new ApiError({
        kind: canceled ? 'canceled' : 'network',
        message: canceled
          ? translate('apiErrors.requestCanceled', 'The request was canceled.')
          : translate(
              'apiErrors.network',
              'Cannot reach the server. Make sure the backend is running and try again.',
            ),
      });
    }

    const status = response.status;
    const problem = extractProblem(response.data);
    const validationErrors = extractValidationErrors(problem);
    const kind = kindFromStatus(status);

    const title = asString(problem?.title);
    const detail = asString(problem?.detail);
    const backendMessage = extractMessage(problem);
    const serverValidationMessage = firstValidationMessage(validationErrors);

    return new ApiError({
      kind,
      message:
        detail ??
        backendMessage ??
        serverValidationMessage ??
        title ??
        fallbackMessage(kind, status, validationErrors !== null),
      status,
      errorCode: asString(problem?.errorCode),
      traceId: asString(problem?.traceId),
      title,
      detail,
      validationErrors,
    });
  }

  if (error instanceof Error) {
    return new ApiError({ kind: 'unknown', message: error.message });
  }

  return new ApiError({
    kind: 'unknown',
    message: translate('apiErrors.unexpected', 'An unexpected error occurred.'),
  });
}
