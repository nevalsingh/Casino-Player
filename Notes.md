# Notes

## Running it

Everything runs in Docker. The Tester posts to `https://localhost:7120`, so the API container needs a cert your machine trusts. Export the dev cert once:

```
dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\ot-assessment.pfx" -p ot-dev-cert
dotnet dev-certs https --trust
```

Then:

```
docker compose up -d --build
dotnet run --project test/OT.Assessment.Tester -c Release
```

That starts SQL Server, RabbitMQ, Redis, the API and the Consumer, and creates the database from `DatabaseGenerate.sql`. Swagger is on https://localhost:7120/swagger and the RabbitMQ UI on http://localhost:15672 (guest/guest).

To run the API and Consumer from the IDE instead, use a local SQL Server with Windows auth (the connection strings in appsettings use SSPI), run `DatabaseGenerate.sql` against it, and start just `rabbitmq` and `redis` from the compose file.

Unit tests: `dotnet test`.

## Design

The API doesn't touch the database on the write path. It validates the wager, publishes it to RabbitMQ and returns 200 once the broker confirms it has the message. I'd normally return 202 here, but the Tester only treats 200 as a success. If the publish fails it returns 503.

The Consumer reads from the queue and writes in batches of up to 500 (or whatever has arrived after 200ms) using a table-valued parameter and one stored procedure call. That's where most of the throughput comes from. Messages are only acked after the batch commits.

Delivery is at-least-once, so the insert has to be idempotent. `WagerId` has a unique index and the proc skips wagers it already has. This matters more than I expected: the Tester sends some wagers more than once, so 7000 requests ended up as 6684 rows.

If a batch fails because of bad data, I retry the messages one at a time and send only the bad ones to the dead-letter queue. Anything else, like SQL being down, gets retried with backoff and nothing is acked until it works. A failed ack after a broker restart is just logged, because RabbitMQ will redeliver and the duplicate gets skipped.

Database:

- Provider, Game and Player are split out of the wager since they repeat a lot. `SessionData` isn't stored, nothing needs it.
- `CasinoWager` is clustered on an identity column so inserts append, with `WagerId` as a separate unique index.
- The player history query is covered by an index on `(AccountId, CreatedDateTimeUtc DESC)`.
- `Player.TotalAmountSpend` is kept up to date by the insert proc, so top spenders is a `TOP (n)` instead of summing every wager.
- Read committed snapshot is on so the GETs don't block behind the Consumer.
- Money is `DECIMAL(19,4)`, dates are stored as UTC.

I used Dapper with stored procedures rather than EF. The brief asks for procs, and passing a TVP is simple with Dapper.

No outbox, because the API never writes to the database, so there's nothing to keep in sync with the publish.

There's a Redis cache for top spenders (`CachedPlayerWagerReadRepository`, a decorator over the SQL repository) with a 10 second TTL. If Redis is down it falls back to SQL and `/health` reports Degraded, so the API still works without it.

## Results

Real Tester against the Docker stack on my laptop: 7000/7000 OK in about 28s, p50 43ms, p95 around 1s. The Consumer kept up at roughly 220 msg/s and the queue drained a few seconds after the run finished. Totals in `CasinoWager` and `Player` matched and the DLQ was empty. Top spenders took about 15ms, a history page about 95ms.

## If this were going to production

- Auth and rate limiting on the API
- Proper secrets instead of passwords in compose
- OpenTelemetry, integration tests with Testcontainers, CI
- Some way to inspect and replay the DLQ
