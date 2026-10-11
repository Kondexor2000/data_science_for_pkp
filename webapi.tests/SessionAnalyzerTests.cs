using ServerActivity.Api.Analysis;

namespace ServerActivity.Api.Tests;

public sealed class SessionAnalyzerTests
{
    private readonly SessionAnalyzer _analyzer = new();

    [Fact]
    public void AnalyzeCsvCalculatesServerAndPlayerSummaries()
    {
        var result = _analyzer.AnalyzeCsv(
            "service,server_name,game,player,connected_at,disconnected_at\n" +
            "TeamSpeak,Voice,,Ala,2026-09-01T18:00:00+02:00,2026-09-01T19:00:00+02:00\n" +
            "teamspeak,Voice,,Bartek,2026-09-01T18:00:00Z,2026-09-01T18:30:00Z\n" +
            "game,\"Survival, EU\",Minecraft,Ala,2026-09-01T18:00:00Z,2026-09-01T20:00:00Z\n");

        var voice = Assert.Single(result.Servers, summary => summary.Service == "teamspeak");
        Assert.Equal(2, voice.Sessions);
        Assert.Equal(2, voice.UniquePlayers);
        Assert.Equal(1.5, voice.TotalHours, 5);
        Assert.Equal(45, voice.MedianSessionMinutes);
        Assert.Equal("Survival, EU", Assert.Single(result.Servers,
            summary => summary.Service == "game").ServerName);
        Assert.Equal(1, Assert.Single(result.Players,
            summary => summary.Player == "Ala" && summary.Service == "teamspeak").Sessions);
    }

    [Theory]
    [InlineData("voice", "service musi mieć wartość")]
    [InlineData("game", "kolumna game nie może być pusta")]
    public void AnalyzeCsvRejectsInvalidSession(string service, string expectedMessage)
    {
        var csv = "service,server_name,game,player,connected_at,disconnected_at\n" +
                  $"{service},Server,,Ala,2026-09-01T18:00:00Z,2026-09-01T19:00:00Z\n";

        var exception = Assert.Throws<AnalysisValidationException>(() => _analyzer.AnalyzeCsv(csv));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyzeCsvRejectsSessionsWithInvalidTimestamps()
    {
        var csv = "service,server_name,game,player,connected_at,disconnected_at\n" +
                  "game,Server,Game,Ala,2026-09-01T19:00:00Z,2026-09-01T18:00:00Z\n";

        Assert.Throws<AnalysisValidationException>(() => _analyzer.AnalyzeCsv(csv));
    }

    [Fact]
    public void AnalyzeCsvRejectsDuplicateHeaders()
    {
        var csv = "service,service,game,player,connected_at,disconnected_at\n" +
                  "game,game,Minecraft,Ala,2026-09-01T18:00:00Z,2026-09-01T19:00:00Z\n";

        var exception = Assert.Throws<AnalysisValidationException>(() => _analyzer.AnalyzeCsv(csv));

        Assert.Contains("nie mogą się powtarzać", exception.Message);
    }
}
