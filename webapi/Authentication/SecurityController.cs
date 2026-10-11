using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ServerActivity.Api.Authentication;

[ApiController]
[Authorize]
[Route("api/security")]
public sealed class SecurityController(
    AuthService authService,
    ISecurityBackupService securityBackup,
    ILogger<SecurityController> logger) : ControllerBase
{
    [HttpGet("login-history")]
    [ProducesResponseType(typeof(LoginHistoryResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetLoginHistory()
    {
        var login = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return login is null
            ? Unauthorized()
            : Ok(authService.GetLoginHistory(login).Select(entry => new LoginHistoryResponse(
                entry.IpAddress,
                entry.CreatedAt,
                entry.IsNewIp,
                entry.BackupStatus)));
    }

    [HttpGet("backup")]
    [Authorize(Roles = "Administrator")]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult DownloadBackup()
    {
        if (!securityBackup.HasBackup)
        {
            return NotFound(new { error = "Nie utworzono jeszcze zaszyfrowanego backupu." });
        }

        try
        {
            return File(securityBackup.GetDecryptedBackup(), "application/zip", "server-activity-backup.zip");
        }
        catch (SecureBackupException exception)
        {
            logger.LogError(exception, "Unable to decrypt the security backup.");
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Nie udało się odczytać backupu.",
                detail: "Plik backupu jest niedostępny lub nie można go zweryfikować.");
        }
    }

    /// <summary>One registration or login event recorded for the authenticated account.</summary>
    public sealed record LoginHistoryResponse(
        string? IpAddress,
        DateTimeOffset CreatedAt,
        bool IsNewIp,
        string BackupStatus);
}
