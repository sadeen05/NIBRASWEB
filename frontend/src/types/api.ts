/**
 * Shared API types. `ProblemDetails` mirrors the backend `GlobalExceptionHandler`
 * shape and also tolerates the ASP.NET Core automatic model-validation shape
 * (`errors`) whose exact form is not yet confirmed for this backend.
 */
export interface ProblemDetails {
  status?: number;
  title?: string;
  detail?: string;
  errorCode?: string;
  traceId?: string;
  /** Present on ASP.NET Core automatic validation (400) responses. */
  errors?: Record<string, string[] | string>;
  [key: string]: unknown;
}
