"""Walidacja i agregacja danych o przejazdach."""

from pathlib import Path

import pandas as pd

REQUIRED_COLUMNS = {
    "train_id", "category", "travel_date", "departure_station",
    "arrival_station", "distance_km", "departure_time", "arrival_time",
}


def load_trips(path: str | Path) -> pd.DataFrame:
    """Wczytaj i sprawdź CSV przejazdów. Czasy zapisujemy w lokalnej strefie rozkładu."""
    df = pd.read_csv(path)
    missing = REQUIRED_COLUMNS - set(df.columns)
    if missing:
        raise ValueError(f"Brak wymaganych kolumn: {', '.join(sorted(missing))}")
    if df.empty:
        raise ValueError("Plik nie zawiera żadnych przejazdów.")

    df["travel_date"] = pd.to_datetime(df["travel_date"], errors="raise").dt.date
    df["distance_km"] = pd.to_numeric(df["distance_km"], errors="raise")
    if (df["distance_km"] <= 0).any():
        raise ValueError("distance_km musi być większe od zera.")

    start = pd.to_datetime(df["travel_date"].astype(str) + " " + df["departure_time"], errors="raise")
    end = pd.to_datetime(df["travel_date"].astype(str) + " " + df["arrival_time"], errors="raise")
    # Obsługa przejazdów przez północ.
    end = end.where(end > start, end + pd.Timedelta(days=1))
    df["duration_minutes"] = (end - start).dt.total_seconds() / 60
    if (df["duration_minutes"] <= 0).any():
        raise ValueError("Czas przejazdu musi być dodatni.")
    df["average_speed_kmh"] = df["distance_km"] / (df["duration_minutes"] / 60)
    return df


def compare_categories(df: pd.DataFrame) -> pd.DataFrame:
    """Podsumuj przejazdy wg kategorii; różnica jest względem najszybszej średniej."""
    summary = (
        df.groupby("category", as_index=False)
        .agg(
            trips=("train_id", "count"),
            mean_duration_min=("duration_minutes", "mean"),
            median_duration_min=("duration_minutes", "median"),
            mean_speed_kmh=("average_speed_kmh", "mean"),
            median_speed_kmh=("average_speed_kmh", "median"),
        )
    )
    fastest = summary["mean_duration_min"].min()
    summary["slower_than_fastest_min"] = summary["mean_duration_min"] - fastest
    return summary.sort_values("mean_duration_min").reset_index(drop=True)


def paired_comparison(df: pd.DataFrame) -> pd.DataFrame:
    """Porównaj kategorie w obrębie dat, na których obie wykonały przejazd."""
    wide = df.pivot_table(
        index="travel_date", columns="category", values="duration_minutes", aggfunc="mean"
    ).dropna()
    if wide.shape[1] < 2 or wide.empty:
        return pd.DataFrame()
    return wide.reset_index()
