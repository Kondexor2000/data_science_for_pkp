using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ServerActivity.Api.LocalPlaytime;

[ApiController]
[Authorize]
[Route("api/local-playtime")]
public sealed class LocalPlaytimeController(LocalPlaytimeService service) : ControllerBase
{
    [HttpGet("processes")]
    public ActionResult<IReadOnlyList<RunningProcess>> GetRunningProcesses() =>
        Ok(service.GetRunningProcesses());

    [HttpGet("tracked-games")]
    public ActionResult<IReadOnlyList<TrackedGame>> GetTrackedGames() =>
        Ok(service.GetTrackedGames(User.Identity!.Name!));

    [HttpPost("tracked-games")]
    public IActionResult AddTrackedGame(AddTrackedGameRequest request)
    {
        var gameName = request.GameName?.Trim();
        var processName = NormalizeProcessName(request.ProcessName);
        if (string.IsNullOrWhiteSpace(gameName) || gameName.Length > 100 ||
            gameName.Any(char.IsControl))
        {
            return BadRequest(new { error = "Nazwa gry musi mieć od 1 do 100 znaków." });
        }

        if (processName is null)
        {
            return BadRequest(new { error = "Podaj poprawną nazwę procesu gry, bez ścieżki i rozszerzenia." });
        }

        var id = service.AddTrackedGame(User.Identity!.Name!, gameName, processName);
        return id is null
            ? Conflict(new { error = "Ten proces jest już monitorowany na tym koncie." })
            : Created("/api/local-playtime/tracked-games", new { id });
    }

    [HttpPost("tracked-games/{id:long}/users")]
    public IActionResult AddGameUser(long id, AddGameUserRequest request)
    {
        var userName = request.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 100 ||
            userName.Any(char.IsControl))
        {
            return BadRequest(new { error = "Nazwa użytkownika musi mieć od 1 do 100 znaków." });
        }

        var login = User.Identity!.Name!;
        var userId = service.AddGameUser(login, id, userName);
        if (userId is not null)
        {
            return Created($"/api/local-playtime/tracked-games/{id}/users", new { id = userId });
        }

        return service.OwnsTrackedGame(login, id)
            ? Conflict(new { error = "Ten użytkownik jest już przypisany do tej gry." })
            : NotFound(new { error = "Nie znaleziono monitorowanej gry." });
    }

    [HttpPut("tracked-games/{id:long}/users/{userId:long}/active")]
    public IActionResult SetActiveGameUser(long id, long userId) =>
        service.SetActiveGameUser(User.Identity!.Name!, id, userId)
            ? NoContent()
            : NotFound(new { error = "Nie znaleziono użytkownika przypisanego do tej gry." });

    [HttpDelete("tracked-games/{id:long}")]
    public IActionResult RemoveTrackedGame(long id) =>
        service.RemoveTrackedGame(User.Identity!.Name!, id)
            ? NoContent()
            : NotFound(new { error = "Nie znaleziono monitorowanej gry." });

    private static string? NormalizeProcessName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 128 ||
            name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':'))
        {
            return null;
        }

        var processName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
        return string.IsNullOrWhiteSpace(processName) ? null : processName;
    }
}

public sealed record AddTrackedGameRequest(string? GameName, string? ProcessName);
public sealed record AddGameUserRequest(string? UserName);
