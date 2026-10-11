import pytest

from server_activity.analysis import load_sessions, summarize_players, summarize_servers


def write_csv(tmp_path, content):
    path = tmp_path / "sessions.csv"
    path.write_text(content, encoding="utf-8")
    return path


def test_load_sessions_normalizes_services_and_calculates_duration(tmp_path):
    path = write_csv(
        tmp_path,
        "service,server_name,game,player,connected_at,disconnected_at\n"
        "TeamSpeak,Voice,,Ala,2026-09-01T18:00:00+02:00,2026-09-01T19:30:00+02:00\n"
        "GAME,Survival,Minecraft,Bartek,2026-09-01T18:00:00+02:00,2026-09-01T20:00:00+02:00\n",
    )

    sessions = load_sessions(path)

    assert sessions["service"].tolist() == ["teamspeak", "game"]
    assert sessions["duration_minutes"].tolist() == [90, 120]
    assert str(sessions["connected_at"].dt.tz) == "UTC"


def test_summaries_report_server_and_player_metrics(tmp_path):
    path = write_csv(
        tmp_path,
        "service,server_name,game,player,connected_at,disconnected_at\n"
        "teamspeak,Voice,,Ala,2026-09-01T18:00:00Z,2026-09-01T19:00:00Z\n"
        "teamspeak,Voice,,Bartek,2026-09-01T18:00:00Z,2026-09-01T18:30:00Z\n"
        "game,Survival,Minecraft,Ala,2026-09-01T18:00:00Z,2026-09-01T20:00:00Z\n",
    )
    sessions = load_sessions(path)

    servers = summarize_servers(sessions)
    voice = servers.loc[servers["service"] == "teamspeak"].iloc[0]
    players = summarize_players(sessions)
    ala_voice = players.loc[
        (players["service"] == "teamspeak") & (players["player"] == "Ala")
    ].iloc[0]

    assert voice["sessions"] == 2
    assert voice["unique_players"] == 2
    assert voice["total_hours"] == pytest.approx(1.5)
    assert ala_voice["total_hours"] == pytest.approx(1)
    assert "unique_players" not in players.columns


@pytest.mark.parametrize(
    ("row", "message"),
    [
        (
            "voice,Server,,Ala,2026-09-01T18:00:00Z,2026-09-01T19:00:00Z",
            "service musi mieć wartość",
        ),
        (
            "game,Server,,Ala,2026-09-01T18:00:00Z,2026-09-01T19:00:00Z",
            "kolumna game nie może być pusta",
        ),
        (
            "game,Server,Minecraft,Ala,2026-09-01T19:00:00Z,2026-09-01T18:00:00Z",
            "musi być późniejszy",
        ),
    ],
)
def test_load_sessions_rejects_invalid_rows(tmp_path, row, message):
    path = write_csv(
        tmp_path,
        "service,server_name,game,player,connected_at,disconnected_at\n" + row + "\n",
    )

    with pytest.raises(ValueError, match=message):
        load_sessions(path)


def test_load_sessions_requires_all_columns(tmp_path):
    path = write_csv(tmp_path, "service,player\nteamspeak,Ala\n")

    with pytest.raises(ValueError, match="Brak wymaganych kolumn"):
        load_sessions(path)
