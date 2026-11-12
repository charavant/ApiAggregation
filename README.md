# ApiAggregation

An ASP.NET Core API that combines a World Bank country profile, Open-Meteo weather, and Sunrise-Sunset daylight data for the country's capital.

## Run the API

Install the **.NET 10 SDK**. The live API needs internet access to reach the providers. No API keys are needed.

From the directory containing `ApiAggregation.slnx`, run:

```powershell
dotnet run --project ApiAggregation.csproj --no-launch-profile -- --urls http://localhost:49374
```

In Visual Studio, you can also open `ApiAggregation.slnx`, set **ApiAggregation** as the startup project, and press **F5**. Use the HTTP address shown in the startup log if it differs from the examples.

## Try it in Postman

Select **GET**, paste only the URL, and click **Send**. Leave the request body empty.

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

## Run the tests

From the same directory, run:

```powershell
dotnet test ApiAggregation.slnx
```

Or open **Test → Test Explorer → Run All** in Visual Studio. Right-click a test and choose **Debug** to step through it. The first run may download NuGet packages. The tests start the app in memory and replace outbound HTTP responses, so you can run them without starting the API separately or contacting the providers.

The suite covers provider parsing, input validation, filtering and sorting, concurrent fetches, cache hits, retries and fallback, and statistics bucket boundaries and concurrent updates.

## Data sources

- [World Bank Country API](https://datahelpdesk.worldbank.org/knowledgebase/articles/898590-country-api-queries)
- [Open-Meteo Forecast API](https://open-meteo.com/en/docs)
- Daylight data by [Sunrise-Sunset.org](https://sunrise-sunset.org/)
