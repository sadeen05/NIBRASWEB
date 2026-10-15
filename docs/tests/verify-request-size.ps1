#requires -Version 5.1
<#
.SYNOPSIS
    Regression check for the application's request-body size limit.

.DESCRIPTION
    Sends a JSON request body larger than the configured Kestrel
    MaxRequestBodySize (1 MiB) to a read-only endpoint and asserts that the
    application responds with HTTP 413 Payload Too Large instead of 500.

    The request uses an explicit Content-Length and never chunked transfer
    encoding, so it exercises the real Kestrel request-size enforcement.

    This script requires an already-running application instance. It does not
    start the application and performs no database access.

    Safety: use only a disposable test database (for example
    NEBRASDB_Step3_Test on your local SQL Server instance) and the read-only login endpoint.
    The login endpoint is rate limited (auth policy, default 10/min), so avoid
    running this in a tight loop.

.PARAMETER BaseUrl
    Base URL of the running application. Default: http://localhost:5181

.PARAMETER Path
    Target request path. Default: /api/auth/login (read-only, creates no data).

.PARAMETER Bytes
    Exact size of the transmitted body in bytes. Default: 2097152 (2 MiB).

.PARAMETER ExpectedStatus
    Expected HTTP status code. Default: 413

.EXAMPLE
    ./verify-request-size.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:5181',
    [string] $Path = '/api/auth/login',
    [int]    $Bytes = 2097152,
    [int]    $ExpectedStatus = 413
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http | Out-Null

# Build a valid JSON body of exactly $Bytes ASCII bytes.
$prefix = '{"email":"request-size-check@example.test","password":"'
$suffix = '"}'
$padding = $Bytes - $prefix.Length - $suffix.Length
if ($padding -lt 0) {
    throw "Bytes ($Bytes) is too small to build a valid JSON body."
}
$body = $prefix + ('A' * $padding) + $suffix
$payload = [System.Text.Encoding]::UTF8.GetBytes($body)

if ($payload.Length -ne $Bytes) {
    throw "Built payload is $($payload.Length) bytes, expected $Bytes."
}

$content = [System.Net.Http.ByteArrayContent]::new($payload)
$content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/json')

$uri = ($BaseUrl.TrimEnd('/')) + $Path
$request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $uri)
$request.Content = $content
$request.Headers.ExpectContinue = $false

$declaredLength = $content.Headers.ContentLength
$isChunked = $content.Headers.TransferEncodingChunked

"Target            : $uri"
"Body bytes        : $($payload.Length)"
"Declared length   : $declaredLength"
"Chunked transfer  : " + [bool]$isChunked
"Expected status   : $ExpectedStatus"

if ($declaredLength -ne $Bytes -or [bool]$isChunked) {
    throw "Test setup is not using a fixed Content-Length; refusing to run."
}

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(60)
try {
    $response = $client.SendAsync($request).Result
    $actualStatus = [int]$response.StatusCode
    $responseText = $response.Content.ReadAsStringAsync().Result

    $errorCode = $null
    try {
        $errorCode = ($responseText | ConvertFrom-Json).errorCode
    }
    catch {
        $errorCode = $null
    }

    "Actual status     : $actualStatus $($response.StatusCode)"
    "errorCode         : $errorCode"

    if ($actualStatus -eq $ExpectedStatus) {
        "RESULT            : PASS (HTTP $actualStatus)"
        exit 0
    }

    "RESULT            : FAIL (expected HTTP $ExpectedStatus, got HTTP $actualStatus)"
    exit 1
}
catch {
    # No HTTP response observed (for example a connection reset).
    "Actual outcome    : transport exception, no HTTP response observed"
    "Exception         : $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    "RESULT            : FAIL (a connection reset is not an HTTP $ExpectedStatus)"
    exit 1
}
finally {
    $client.Dispose()
    $request.Dispose()
    $content.Dispose()
}
