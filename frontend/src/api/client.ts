import axios, { type AxiosInstance, type AxiosRequestConfig } from 'axios';

import { normalizeError } from '@/api/errors';

/**
 * Relative `/api` base path in development (the Vite dev server proxies it to
 * http://localhost:5181). `VITE_API_BASE_URL` can override it for other
 * deployments; no backend port is hard-coded in application code.
 */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL?.trim() || '/api';

type AccessTokenProvider = () => string | null;

let accessTokenProvider: AccessTokenProvider = () => null;
let unauthorizedHandler: (() => void) | null = null;

/** The auth layer supplies the current bearer token without creating an import cycle. */
export function setAccessTokenProvider(provider: AccessTokenProvider): void {
  accessTokenProvider = provider;
}

/** Invoked when the API rejects a request with 401 so the session can be cleared. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler;
}

/**
 * Every request fails after this many milliseconds. Without a timeout a lost or
 * stalled response leaves a pending UI action (e.g. "Save land") spinning
 * forever, because Axios waits indefinitely by default. A timed-out request is
 * surfaced as a `timeout` error (see `normalizeError`) so the user is told what
 * happened instead of watching a dead spinner.
 *
 * 60s is deliberately above the backend's own 30s EF/SQL command timeout: a
 * request the server is still working on must not be abandoned client-side,
 * otherwise a slow-but-successful save would be reported as a failure.
 */
export const REQUEST_TIMEOUT_MS = 60_000;

export const apiClient: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: REQUEST_TIMEOUT_MS,
  headers: { 'Content-Type': 'application/json' },
});

apiClient.interceptors.request.use((config) => {
  const token = accessTokenProvider();
  if (token) {
    config.headers.set('Authorization', `Bearer ${token}`);
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => {
    // Normalize 204/empty bodies so callers never try to parse an empty string.
    if (response.status === 204) {
      response.data = null;
    }
    return response;
  },
  (error: unknown) => {
    const normalized = normalizeError(error);
    if (normalized.kind === 'unauthorized') {
      unauthorizedHandler?.();
    }
    return Promise.reject(normalized);
  },
);

export async function getJson<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
  const response = await apiClient.get<T>(url, config);
  return response.data;
}

export async function postJson<T>(
  url: string,
  body?: unknown,
  config?: AxiosRequestConfig,
): Promise<T> {
  const response = await apiClient.post<T>(url, body, config);
  return response.data;
}

export async function putJson<T>(
  url: string,
  body?: unknown,
  config?: AxiosRequestConfig,
): Promise<T> {
  const response = await apiClient.put<T>(url, body, config);
  return response.data;
}

export async function deleteJson<T>(
  url: string,
  config?: AxiosRequestConfig,
): Promise<T> {
  const response = await apiClient.delete<T>(url, config);
  return response.data;
}
