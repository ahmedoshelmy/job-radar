# 4. Deduplicate by fingerprint, then by canonical URL

Date: 2026-11-20

## Status

Accepted

## Context

The same opening shows up many times: every poll returns it again, and aggregators mirror company boards. Exact URL matching
fails in practice:

- Aggregators link to their own page, not the company board (`remotive.com/remote-jobs/...` vs `job-boards.greenhouse.io/...`).
- When they do link to the original, they add tracking parameters (`?utm_source=remotive&utm_medium=feed`).
- Titles and company names are spelled differently: `Sr. Back-end Engineer (.NET) - Remote` at `Nilewave Labs Inc.` is
  `Senior Backend Engineer (.NET)` at `Nilewave Labs`; German boards add `(m/w/d)`.

## Decision

Deduplication is a **Catalog domain rule**, not a database trick:

1. **Fingerprint** = `norm(company) | norm(title) | place`, where
   - company: accents folded, `&` → `and`, legal and generic suffixes dropped (`Inc`, `GmbH`, `Ltd`, `Labs`, …);
   - title: gender tags removed, `Sr.`/`Jr.` expanded, `back-end`/`front-end`/`full stack` unified, remote markers in brackets
     or after a dash dropped, punctuation collapsed (but `.NET`, `C#`, `C++` kept);
   - place: every remote job shares the bucket `remote`; other jobs use their first place (city), so the same title in two
     cities stays two openings.
2. If no fingerprint matches, a **canonical URL** (lowercased host, no `www`, no fragment, no `utm_*`/`gh_src`/`ref`/… parameters,
   no trailing slash) is tried, which catches a mirror that renamed the role but links to the original.
3. A match credits the new feed (`AlsoSeenIn`) and updates `LastSeenAt`; it never creates a row or an event.

The consumer runs one message at a time (`ConcurrentMessageLimit = 1`) so check-then-insert is race-free, a unique index on the
fingerprint is the backstop, and the EF Core inbox/outbox makes redelivery a no-op and the row and its `JobPostingAdded` event
commit atomically.

## Consequences

- Each normalisation step has a unit test with the example that motivated it.
- False merges are possible (two genuinely different "Backend Engineer" roles at one company, both remote). Accepted: for one
  user's radar a missed duplicate is noisier than a rare false merge, and the original link is kept.
- Changing the fingerprint formula changes identity; it needs a backfill migration, so the formula is versioned by ADR.
