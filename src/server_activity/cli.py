"""Command-line interface for player-hosted server activity reports."""

import argparse
from pathlib import Path

from .analysis import load_sessions, summarize_players, summarize_servers


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Podsumuj czas na TeamSpeak i aktywność serwerów gier z CSV."
    )
    parser.add_argument(
        "csv", nargs="?", default="data/sample_sessions.csv", help="Ścieżka do pliku CSV"
    )
    parser.add_argument(
        "--output",
        default="reports/server-summary.csv",
        help="Ścieżka zapisu podsumowania serwerów CSV",
    )
    parser.add_argument(
        "--player-output",
        default="reports/player-summary.csv",
        help="Ścieżka zapisu podsumowania graczy CSV",
    )
    args = parser.parse_args()

    sessions = load_sessions(args.csv)
    server_summary = summarize_servers(sessions)
    player_summary = summarize_players(sessions)

    print("Podsumowanie usług i serwerów:")
    print(server_summary.round(2).to_string(index=False))
    print("\nPodsumowanie graczy:")
    print(player_summary.round(2).to_string(index=False))

    for frame, path_arg in (
        (server_summary, args.output),
        (player_summary, args.player_output),
    ):
        output = Path(path_arg)
        output.parent.mkdir(parents=True, exist_ok=True)
        frame.to_csv(output, index=False)
        print(f"Zapisano raport: {output}")


if __name__ == "__main__":
    main()
