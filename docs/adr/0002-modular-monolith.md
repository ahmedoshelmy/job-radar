# 2. A modular monolith with message-based modules, plus one separate worker

Date: 2026-11-20

## Status

Accepted

## Context

The domain has three clear bounded contexts:

- **Catalog**: what openings exist (one row per opening, whatever feed reported it).
- **Matching**: how well each opening fits the user's profile, and telling the user.
- **Tracker**: the applications the user is pursuing and their history.

Fetching feeds is a different kind of work again: slow, I/O bound, rate limited, and it must keep its own schedule.

Running five microservices for one user would buy operational cost (five deployments, five databases, distributed tracing
just to debug a click) without any of the benefits (independent teams, independent scaling).

## Decision

Two deployables:

1. `JobRadar.Feeds.Worker`: the scheduler and board adapters. Its only output is the `JobPostingDiscovered` event.
2. `JobRadar.Api`: a modular monolith hosting Catalog, Matching and Tracker (plus a stats projection).

Inside the API each module is its own project with `Domain`, `Application` and `Infrastructure` folders, its own `DbContext`
and its own Postgres schema (`catalog`, `matching`, `tracker`, `stats`) with its own migrations history table.
Modules do not reference each other (an architecture test enforces it):

- They talk through integration events on the broker (`JobPostingAdded`, `JobMatched`, `ApplicationStageChanged`), each
  consumer on its own queue, so one event fans out to independent subscribers.
- Where a synchronous read is unavoidable (the Tracker needs a posting's title when you start an application; the postings
  list needs scores), the API composes them: the Tracker declares an `IPostingLookup` port and the host adapts the Catalog's
  read interface to it; `GET /api/postings` asks Catalog for rows and Matching for scores and joins in memory.

## Consequences

- One deployment, one database server, one trace per request, and still real boundaries.
- Extracting a module into its own service is a deployment change: move the project into a new host, keep the queues, swap the
  in-process composition for an HTTP call. No shared tables to untangle.
- The in-memory join caps the postings list (newest 300 by default). Fine for one person's radar; a read model would replace it
  if this ever served many users.
- Matching keeps its own snapshot of each posting (from the event) so a profile change can rescore everything without calling
  the Catalog. Duplication is the price of autonomy.
