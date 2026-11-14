# ApiAggregation

An ASP.NET Core API that combines a World Bank country profile, Open-Meteo weather, and Sunrise-Sunset daylight data for the country's capital.

## Run the API

Install the **.NET 10 SDK**. The live API needs internet access to reach the three data providers. The providers require no API keys. From the directory containing `ApiAggregation.slnx`, start the API.

```powershell
dotnet run --project ApiAggregation.csproj --no-launch-profile -- --environment Development --urls http://localhost:49374
```

In Visual Studio, open `ApiAggregation.slnx`, select the **ApiAggregation** launch profile, and press **F5**. Use the HTTP address shown in the startup log if it differs from the examples.

## Demonstrate JWT authentication

Each route has a public version and a JWT-protected version. Both versions use the same query parameters, handlers, cache, and provider statistics.

| Public GET route | GET route requiring a valid bearer token |
| --- | --- |
| `/api/countries/{code}` | `/api/secure/countries/{code}` |
| `/api/countries` | `/api/secure/countries` |
| `/api/stats` | `/api/secure/stats` |

To try the protected routes, generate a local token from the directory containing `ApiAggregation.slnx` and copy the value printed in the terminal.

```powershell
if (-not (Test-Path appsettings.Development.json)) {
    '{}' | Set-Content appsettings.Development.json
}
dotnet user-jwts create --project ApiAggregation.csproj --name InterviewDemo --audience api-aggregation --valid-for 1h --output token
```

The tool keeps the signing key in local User Secrets and writes the issuer and audience to the ignored `appsettings.Development.json` file. Restart the API in Development to load those settings. Keep tokens and signing keys out of Git and shared Postman collections. [Development token documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn?view=aspnetcore-10.0)

In Postman, send these requests to `http://localhost:49374`.

| GET path | Postman authorization | Expected result |
| --- | --- | --- |
| `/api/stats` | **No Auth** | `200 OK` with statistics |
| `/api/secure/stats` | **No Auth** | `401 Unauthorized` with `WWW-Authenticate: Bearer` |
| `/api/secure/stats` | **Bearer Token** with `invalid` | `401 Unauthorized` |
| `/api/secure/stats` | **Bearer Token** with the generated JWT | `200 OK` with the same statistics |

Paste only the JWT into the **Token** field. Postman adds the `Bearer` prefix. The statistics routes also work during provider outages. The `CountryResponseUsesAllProvidersAndRepeatingItUsesTheCache` integration test demonstrates aggregation and caching with simulated upstream responses.

The ASP.NET Core JWT handler authenticates the caller by validating the token's signature, issuer, audience, and lifetime. The `/api/secure` route group requires an authenticated caller. The development token proves the flow on localhost. For a deployed API, configure a trusted identity provider as the Bearer scheme's authority, set the expected audience, and use HTTPS. [ASP.NET Core JWT bearer authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)

## Try it in Postman

Select **GET**, set **Authorization → No Auth** for these public URLs, and leave the request body empty.

| URL | Expected result |
| --- | --- |
| `http://localhost:49374/api/countries/GR` | Greece, Athens weather, and daylight data. |
| `http://localhost:49374/api/countries?codes=GR,FR,DE&sections=weather&sort=temperature&order=desc` | Three countries ordered from warmest to coolest, with weather only. |
| `http://localhost:49374/api/stats` | Outbound request counts and average durations for each provider. |
| `http://localhost:49374/api/countries/GRC` | `400` for a malformed country code. |
| `http://localhost:49374/api/countries/ZZ` | `404` for an unknown country. |

The list route takes up to five distinct two-letter country codes. It defaults to all sections and sorts by name ascending. You can select `country`, `weather`, and `daylight` with the `sections` query parameter, use `sort=name` or `sort=temperature`, and set `order=asc` or `order=desc`.

Each country response contains `countryCode`, `sections`, and `sectionStatuses`. A section can be `fresh`, `stale`, or `unavailable`. Country and weather stay fresh in memory for 24 hours. If a refresh fails, the app can reuse a country profile up to seven days old or weather up to 25 hours old. Daylight uses the capital's local date. The API returns `422` when a known country has no capital coordinates and `503` when none of the requested sections can be returned.

`/api/stats` counts the startup World Bank lookup, retries, and failed attempts. Cache hits leave the counters unchanged. Times are in milliseconds and measure the outbound attempt until response headers or failure. The buckets are `fast` below 100 ms, `average` from 100 through 200 ms, and `slow` above 200 ms. Restarting the app clears the cache and counters.

## Performance monitoring

The background worker checks once a minute. It compares each provider's average over the last five minutes with its average across earlier attempts since startup. When both periods have samples and the recent average is more than 50% higher, Serilog writes a warning with both averages and sample counts. Logs go to the console and to daily files under `logs/`. The file sink retains up to seven log files.

To demonstrate the threshold without waiting for a real provider slowdown, run the monitor test with detailed output. It shows the measured averages and the warning at 151 ms versus a 100 ms baseline. At exactly 150 ms, the worker skips the warning.

```powershell
dotnet test ApiAggregation.slnx --filter FullyQualifiedName~ProviderPerformanceMonitorTests --logger "console;verbosity=detailed"
```

## Run the tests

From the same directory, run:

```powershell
dotnet test ApiAggregation.slnx
```

Or open **Test → Test Explorer → Run All** in Visual Studio. Right-click a test and choose **Debug** to step through it. The first run may download NuGet packages. The tests start the app in memory and replace outbound HTTP responses, so you can run them without starting the API separately or contacting the providers.

The suite covers provider parsing, input validation, filtering and sorting, concurrent fetches, cache hits, retries and fallback, statistics buckets, and performance warnings. The JWT integration tests create signed tokens in code with a fresh in-memory key and exercise the real bearer handler. They compare anonymous and authenticated responses across all three route pairs and verify rejection of missing, malformed, expired, wrong-issuer, wrong-audience, and incorrectly signed tokens on protected routes. No local JWT setup is needed to run the suite.

## Data sources

- [World Bank Country API](https://datahelpdesk.worldbank.org/knowledgebase/articles/898590-country-api-queries)
- [Open-Meteo Forecast API](https://open-meteo.com/en/docs)
- Daylight data by [Sunrise-Sunset.org](https://sunrise-sunset.org/)
