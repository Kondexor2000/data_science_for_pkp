"""Interfejs wiersza poleceń."""

import argparse
from pathlib import Path

from .analysis import compare_categories, load_trips, paired_comparison


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Porównaj przejazdy Piła → Poznań → Wrocław z przesiadką w Poznaniu z pliku CSV."
    )
    parser.add_argument("csv", nargs="?", default="data/sample_trips.csv", help="Ścieżka do pliku CSV")
    parser.add_argument("--output", default="reports/summary.csv", help="Ścieżka zapisu podsumowania CSV")
    args = parser.parse_args()

    trips = load_trips(args.csv)
    # Ograniczenie do trasy z przesiadką w Poznaniu: start z Piły, a dalszy odcinek prowadzi do Wrocławia.
    route = trips[
        trips["departure_station"].str.casefold().str.contains("piła", na=False)
        & (
            trips["arrival_station"].str.casefold().str.contains("poznań", na=False)
            | trips["arrival_station"].str.casefold().str.contains("wrocław", na=False)
        )
    ]
    if route.empty:
        raise SystemExit("Brak przejazdów na trasie Piła → Poznań → Wrocław z przesiadką w Poznaniu w podanym pliku.")

    summary = compare_categories(route)
    print("Relacja analizowana: Piła Główna → Poznań Główny → Wrocław Główny (przesiadka w Poznaniu).")
    print("W tej wersji każdy rekord odpowiada całemu połączeniu z przesiadką w Poznaniu.")
    print("\nPodsumowanie kategorii (średnie z dostępnych przejazdów):")
    print(summary.round(2).to_string(index=False))
    pairs = paired_comparison(route)
    if not pairs.empty:
        print("\nCzasy w dniach, w których dostępne były obie kategorie [min]:")
        print(pairs.round(1).to_string(index=False))

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    summary.to_csv(output, index=False)
    print(f"\nZapisano podsumowanie: {output}")


if __name__ == "__main__":
    main()
