# Piła–Poznań–Wrocław: porównanie czasu przejazdu

Mały projekt Data Science w formie MVP. Sprawdza, czy pociąg regionalny A jedzie dłużej niż InterCity B na trasie z przesiadką w Poznaniu, licząc czas odjazd–przyjazd i średnią prędkość z podanej długości trasy.

## Ważne: dane demonstracyjne

`data/sample_trips.csv` zawiera **wymyślone dane przykładowe**, wyłącznie do pokazania działania kodu. Nie są to dane rozkładowe ani pomiary rzeczywistych kursów. Dla sensownego wniosku należy podstawić obserwacje z wielu dat, najlepiej rzeczywiste czasy odjazdu i przyjazdu. Różnica w czasie nie dowodzi sama w sobie, że typ pociągu jest jej przyczyną.

## Uruchomienie

Wymagany Python 3.10+.

```powershell
py -m venv .venv
.venv\Scripts\Activate.ps1
python -m pip install -e .
train-analysis
```

Albo bez instalacji pakietu:

```powershell
python -m pip install -r requirements.txt
$env:PYTHONPATH = "src"
python -m train_analysis.cli data/sample_trips.csv --output reports/summary.csv
```

## Docker i Jenkins

W tej wersji projekt analizuje trasę Piła → Poznań → Wrocław z przesiadką w Poznaniu. W każdym rekordzie CSV widać całość połączenia, a nie tylko pierwszy odcinek bezpośrednio do Poznania.

Obraz buduje aplikację wraz z przykładowymi danymi i uruchamia analizę po starcie:

```sh
docker build -t pila-poznan-wroclaw-transfer-analysis .
docker run --rm -v "$PWD/reports:/app/reports" pila-poznan-wroclaw-transfer-analysis
```

`Jenkinsfile` wykonuje kontrolę składni i analizę przykładowego CSV, a następnie buduje obraz i sprawdza, czy kontener wygenerował raport. Agent Jenkins musi mieć Python 3, `sh` oraz dostępny Docker z działającym daemonem. Raporty z `reports/` są archiwizowane jako artefakty.

## Dane wejściowe

CSV musi zawierać kolumny:

`train_id, category, travel_date, departure_station, arrival_station, distance_km, departure_time, arrival_time`

Datę zapisuj jako `YYYY-MM-DD`, czas jako `HH:MM` (czas lokalny stacji). Dla pierwszej wersji należy ujednolicić definicję trasy i dystans dla obu kategorii. Kod traktuje przyjazd wcześniejszy niż odjazd jako przejazd przez północ.

## Co robi MVP

- sprawdza wymagane kolumny, dodatnią odległość i poprawność czasu;
- wylicza czas przejazdu oraz prędkość średnią (km/h);
- filtruje trasę Piła–Poznań–Wrocław z przesiadką w Poznaniu;
- prezentuje liczbę kursów, średnią i medianę czasu/prędkości oraz różnicę czasu względem najszybszej kategorii;
- pokazuje zestawienie sparowane dla dat, w których występują obie kategorie;
- zapisuje podsumowanie do CSV.

## Dalsze kroki po MVP

1. Zasilić dane wiarygodnym źródłem (np. obserwacje lub oficjalny rozkład) i zapisać źródło, moment pobrania oraz opóźnienia.
2. Porównywać te same daty/godziny, kierunek i punkty zatrzymań; rozdzielić czas rozkładowy od rzeczywistego.
3. Zebrać większą próbę i raportować przedziały ufności, a nie tylko średnie. Uwzględnić postoje, opóźnienia, dzień tygodnia i wariant trasy.
4. Dodać automatyczne testy walidacji danych, wykresy oraz pipeline odświeżania po uzgodnieniu źródła.
