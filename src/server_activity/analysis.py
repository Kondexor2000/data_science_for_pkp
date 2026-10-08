"""Validation and aggregation of TeamSpeak and game-server sessions."""

from pathlib import Path

import pandas as pd

REQUIRED_COLUMNS = {
    "service",
    "server_name",
    "game",
    "player",
    "connected_at",
    "disconnected_at",
}
SERVER_GROUP = ["service", "server_name", "game"]
PLAYER_GROUP = [*SERVER_GROUP, "player"]


def load_sessions(path: str | Path) -> pd.DataFrame:
    """Load normalized session records from CSV and calculate their durations."""
    df = pd.read_csv(path)
    missing = REQUIRED_COLUMNS - set(df.columns)
    if missing:
        raise ValueError(f"Brak wymaganych kolumn: {', '.join(sorted(missing))}")
    if df.empty:
        raise ValueError("Plik nie zawiera żadnych sesji.")

    for column in ("service", "server_name", "game", "player"):
        df[column] = df[column].fillna("").astype(str).str.strip()

    for column in ("service", "server_name", "player"):
        if df[column].eq("").any():
            raise ValueError(f"Kolumna {column} nie może zawierać pustych wartości.")

    df["service"] = df["service"].str.casefold()
    invalid_services = sorted(set(df["service"]) - {"teamspeak", "game"})
    if invalid_services:
        raise ValueError(
            "service musi mieć wartość 'teamspeak' albo 'game'; "
            f"nieobsługiwane wartości: {', '.join(invalid_services)}."
        )

    invalid_games = df["service"].eq("game") & df["game"].eq("")
    if invalid_games.any():
        raise ValueError("Dla sesji game kolumna game nie może być pusta.")
    df.loc[df["service"].eq("teamspeak"), "game"] = ""

    df["connected_at"] = pd.to_datetime(df["connected_at"], errors="raise", utc=True)
    df["disconnected_at"] = pd.to_datetime(
        df["disconnected_at"], errors="raise", utc=True
    )
    df["duration_minutes"] = (
        df["disconnected_at"] - df["connected_at"]
    ).dt.total_seconds() / 60
    if (df["duration_minutes"] <= 0).any():
        raise ValueError("Czas zakończenia sesji musi być późniejszy niż czas połączenia.")
    return df


def _aggregate_sessions(df: pd.DataFrame, group_by: list[str]) -> pd.DataFrame:
    summary = (
        df.groupby(group_by, as_index=False, dropna=False)
        .agg(
            sessions=("duration_minutes", "size"),
            unique_players=("player", "nunique"),
            total_hours=("duration_minutes", lambda durations: durations.sum() / 60),
            mean_session_minutes=("duration_minutes", "mean"),
            median_session_minutes=("duration_minutes", "median"),
        )
        .sort_values(group_by)
        .reset_index(drop=True)
    )
    return summary


def summarize_servers(df: pd.DataFrame) -> pd.DataFrame:
    """Summarize session time and player counts per service and hosted server."""
    return _aggregate_sessions(df, SERVER_GROUP)


def summarize_players(df: pd.DataFrame) -> pd.DataFrame:
    """Summarize session time per player, service, and hosted server."""
    return _aggregate_sessions(df, PLAYER_GROUP).drop(columns="unique_players")
