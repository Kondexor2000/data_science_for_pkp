using Microsoft.AspNetCore.Mvc;

namespace ServerActivity.Api.Authentication;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public IActionResult Register(CredentialsRequest request)
    {
        var login = request.Login?.Trim();
        if (string.IsNullOrWhiteSpace(login) ||
            login.Length > 64 ||
            login.Any(char.IsControl))
        {
            return BadRequest(new { error = "Login musi mieć od 1 do 64 znaków." });
        }

        if (request.Password is null || request.Password.Length < 8 || request.Password.Length > 256)
        {
            return BadRequest(new { error = "Hasło musi mieć od 8 do 256 znaków." });
        }

        return authService.Register(login, request.Password, GetClientIpAddress())
            ? StatusCode(StatusCodes.Status201Created, new { login })
            : Conflict(new { error = "Użytkownik o takim loginie już istnieje." });
    }

    [HttpPost("login")]
    public IActionResult Login(CredentialsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrEmpty(request.Password))
        {
            return Unauthorized(new { error = "Nieprawidłowy login lub hasło." });
        }

        var login = authService.Login(request.Login.Trim(), request.Password, GetClientIpAddress());
        return login is null
            ? Unauthorized(new { error = "Nieprawidłowy login lub hasło." })
            : Ok(new LoginResponse(
                login.Token,
                3600,
                login.IsAdministrator,
                login.SecurityWarning));
    }

    private string? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}

public sealed record CredentialsRequest(string Login, string Password);

/// <summary>Bearer token and security information returned after a successful login.</summary>
public sealed record LoginResponse(
    string Token,
    int ExpiresInSeconds,
    bool IsAdministrator,
    string? SecurityWarning);
