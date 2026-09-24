using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CoffeePeek.Account.Infrastructure.Identity;

public class JWTTokenService(IOptions<JWTOptions> options) : IJWTTokenService
{
    private readonly JWTOptions _options = options.Value;

    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Credentials.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Gateway revocation compares iat with the revoked-at timestamp (RevokedUserKeys.IsTokenRevoked).
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.PreferredUsername, user.Username),
            new(JwtRegisteredClaimNames.EmailVerified, user.Credentials.EmailConfirmed.ToString().ToLower()),
        };
        claims.AddRange(user.Roles.Select(ur => new Claim(ClaimTypes.Role, ur.Name)));

        return CreateToken(claims, _options.AccessTokenLifetimeMinutes);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private string CreateToken(IEnumerable<Claim> claims, int lifetimeMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(lifetimeMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}