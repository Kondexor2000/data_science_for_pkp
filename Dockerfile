FROM python:3.12-slim

WORKDIR /app

COPY pyproject.toml README.md ./
COPY src ./src
RUN python -m pip install --no-cache-dir .

COPY data ./data
RUN mkdir -p reports

ENTRYPOINT ["train-analysis"]
CMD ["data/sample_trips.csv", "--output", "reports/summary.csv"]