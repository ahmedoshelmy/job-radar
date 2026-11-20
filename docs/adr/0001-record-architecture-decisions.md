# 1. Record architecture decisions

Date: 2026-11-20

## Status

Accepted

## Context

Job Radar is small, but it makes several choices (deployables, messaging, dedupe) that a reader or interviewer will ask "why?" about.

## Decision

Keep lightweight Architecture Decision Records (Michael Nygard's format) in `docs/adr`, one file per decision, numbered,
never rewritten after acceptance (a later ADR supersedes an earlier one).

## Consequences

The reasoning lives next to the code and survives the author's memory. Writing one costs ten minutes.
