using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace ApiAggregation.Tests
{
  internal static class TestJwt
  {
    private const String Issuer = "api-aggregation-tests";
    private const String Audience = "api-aggregation";
    private const String BearerSettings = "Authentication:Schemes:Bearer";
    private static readonly Byte[] signingKey = RandomNumberGenerator.GetBytes(32);

    public static IReadOnlyDictionary<String, String?> Configuration { get; } =
      new Dictionary<String, String?>
      {
        [$"{BearerSettings}:ValidIssuer"] = Issuer,
        [$"{BearerSettings}:ValidAudience"] = Audience,
        [$"{BearerSettings}:SigningKeys:0:Issuer"] = Issuer,
        [$"{BearerSettings}:SigningKeys:0:Value"] = Convert.ToBase64String(signingKey)
      };

    public static String CreateToken(
      String issuer = Issuer, String audience = Audience, Boolean expired = false, Boolean wrongKey = false)
    {
      var now = DateTime.UtcNow;
      var key = new SymmetricSecurityKey(wrongKey ? RandomNumberGenerator.GetBytes(32) : signingKey);
      var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        notBefore: now.AddMinutes(-30),
        // Expire well beyond the handler's clock tolerance to exercise lifetime validation.
        expires: expired ? now.AddMinutes(-15) : now.AddMinutes(15),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

      return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static HttpRequestMessage CreateRequest(String path, String token)
    {
      var request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
      return request;
    }
  }
}
