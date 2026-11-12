using ApiAggregation.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<WorldBankClient>(client =>
{
    client.BaseAddress = new Uri("https://api.worldbank.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

app.MapGet("/api/countries/{code}", (string code) =>
    Results.Problem(
        title: "Country lookup is not implemented yet",
        detail: $"Connect the World Bank client to look up {code}.",
        statusCode: StatusCodes.Status501NotImplemented));

app.Run();
