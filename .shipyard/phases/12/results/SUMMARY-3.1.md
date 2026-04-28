# SUMMARY-3.1: reconcile subcommand + parity-report.md template

**Builder:** builder-3-1
**Date:** 2026-04-28
**Status:** IN PROGRESS

---

## NDJSON Field Structure (F-1 resolution)

`@i` is a hex Murmur3 hash — NOT an action name. Confirmed via REVIEW-1.5 F-1.

Real NDJSON line shape (CompactJsonFormatter + LoggerMessage.Define):
```json
{"@t":"2026-04-29T12:00:05Z","@mt":"BlueIris DryRun would-execute for camera={Camera} label={Label} event_id={EventId}","@i":"<hex>","Camera":"DriveWayHD","Label":"person","EventId":"ev-1"}
```

Top-level properties: `Camera`, `Label`, `EventId` (Frigate event ID string).
Action discriminator: `@mt` substring — "BlueIris DryRun" vs "Pushover DryRun".

---

## Task 1: Implement Reconciler.cs + extend RunReconcile — TBD

## Task 2: Reconciler unit tests — TBD

## Task 3: docs/parity-report.md — TBD
