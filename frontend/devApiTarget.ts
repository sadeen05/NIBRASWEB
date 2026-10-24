const DEFAULT_API_TARGET = 'http://localhost:5181';
const ACCEPTANCE_MODE = 'f1-acceptance';
const TEST_API_TARGETS = new Set([
  'http://localhost:5199',
  'http://127.0.0.1:5199',
  'http://[::1]:5199',
]);

export function resolveDevApiTarget(
  command: 'build' | 'serve',
  mode: string,
  testApiTarget: string | undefined,
  isPreview = false,
  clientApiBase: string | undefined = undefined,
): string {
  if (mode !== ACCEPTANCE_MODE && testApiTarget === undefined) {
    return DEFAULT_API_TARGET;
  }

  // Test routing is explicit, development-only, and restricted to literal
  // loopback origins. Reject rather than normalize ambiguous URLs.
  if (
    command !== 'serve' ||
    isPreview ||
    mode !== ACCEPTANCE_MODE ||
    testApiTarget === undefined ||
    !TEST_API_TARGETS.has(testApiTarget) ||
    (!!clientApiBase?.trim() && clientApiBase.trim() !== '/api')
  ) {
    throw new Error(
      'Test API proxy requires development mode f1-acceptance and NIBRAS_TEST_API_TARGET set to an HTTP loopback origin on port 5199, with VITE_API_BASE_URL absent or /api.',
    );
  }

  return testApiTarget;
}