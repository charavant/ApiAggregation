using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ApiAggregation.Tests
{
  public sealed class JwtAuthenticationTests
  {
    [Theory]
    [InlineData("/countries/GR")]
    [InlineData("/countries?codes=GR,FR&sections=weather&sort=temperature&order=desc")]
    [InlineData("/stats")]
    public async Task PublicAndSecureRoutesReturnTheSameDataWhenAuthorized(String path)
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();

      using var publicResponse = await client.GetAsync("/api" + path);
      Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);

      var securePath = "/api/secure" + path;
      using var anonymous = await client.GetAsync(securePath);
      Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
      Assert.Contains(anonymous.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");

      using var request = TestJwt.CreateRequest(securePath, TestJwt.CreateToken());
      using var authenticated = await client.SendAsync(request);
      Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
      var publicBody = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
      var secureBody = await authenticated.Content.ReadFromJsonAsync<JsonElement>();
      Assert.True(JsonElement.DeepEquals(publicBody, secureBody));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("wrong audience")]
    [InlineData("wrong issuer")]
    [InlineData("wrong signature")]
    public async Task StatsRejectsInvalidBearerTokens(String failure)
    {
      using var factory = new ApiTestFactory();
      using var client = factory.CreateClient();
      var token = failure switch
      {
        "malformed" => "invalid",
        "expired" => TestJwt.CreateToken(expired: true),
        "wrong audience" => TestJwt.CreateToken(audience: "another-api"),
        "wrong issuer" => TestJwt.CreateToken(issuer: "another-issuer"),
        "wrong signature" => TestJwt.CreateToken(wrongKey: true),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
      };

      using var request = TestJwt.CreateRequest("/api/secure/stats", token);
      using var response = await client.SendAsync(request);

      Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
  }
}
