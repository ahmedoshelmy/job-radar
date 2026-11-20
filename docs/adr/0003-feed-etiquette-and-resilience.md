# 3. Feed etiquette and HTTP resilience

Date: 2026-11-20

## Status

Accepted

## Context

Job Radar reads other people's public APIs: the job-board APIs that applicant-tracking systems publish for embedding an
employer's openings (Greenhouse, Lever, Ashby, Workable) and two remote-job aggregators (Remotive, Arbeitnow). None needs a key.
Their terms boil down to: credit the source, link back to the original posting, do not republish, do not hammer.

## Decision

- **Default to fixtures.** `Feeds:Mode=Fixtures` is the default everywhere (dev, CI, compose, tests). Live mode is opt-in.
- **Low frequency by design.** Every configured feed has its own interval: company boards once a day, Remotive every 12 hours
  (its terms allow a few calls a day), Arbeitnow daily. Intervals below one hour fail options validation at startup.
- **Conditional requests.** The worker stores each feed's `ETag` / `Last-Modified` and sends `If-None-Match` /
  `If-Modified-Since`; a `304` costs the owner almost nothing and is recorded as a successful run.
- **One pipeline per host** (`Microsoft.Extensions.Http.Resilience`, `SelectPipelineByAuthority`), from outside in:
  total timeout 90 s → retry (3, exponential with jitter, honours `Retry-After`) → circuit breaker (opens at 50% failures over
  2 minutes, stays open 10 minutes) → token-bucket rate limiter (1 request per 2 s per host) → per-attempt timeout 30 s.
  A slow or failing board never throttles the others.
- **Resumable.** Run state lives in a small JSON file written atomically (temp file + rename). After a restart the worker
  resumes the schedule instead of polling every feed again. A failing feed backs off 15 → 30 → 60 minutes … up to its interval.
- **Credit and link back.** Every posting keeps its `Source` and `AlsoSeenIn` list, the UI shows "via Greenhouse, also on
  Remotive", and every title links to the original posting. Postings are stored for the user's own matching only and never
  exposed as a feed of their own.
- **Identify politely.** A plain `User-Agent` names the software; no personal contact details are baked in.

## Consequences

- Tests never touch the network; the fixtures are invented companies in each board's real response shape.
- Live mode reacts within a day, not a minute. For job hunting that is the right trade.
- Operators who add boards must keep to the same intervals; the README lists each feed's terms.
