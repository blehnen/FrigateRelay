# FrigateRelay v1.0.0 — Parity Report

> **Template** — to be replaced with the real reconciliation output once the
> 48-hour parity window per `docs/parity-window-checklist.md` has completed
> and the operator runs:
>
> ```bash
> dotnet run --project tools/FrigateRelay.MigrateConf -c Release -- \
>   reconcile \
>   --frigaterelay logs/frigaterelay-YYYYMMDD.log \
>   --legacy parity-window/legacy-actions.csv \
>   --output docs/parity-report.md
> ```

- **Window:** TBD → TBD
- **Legacy actions logged:** TBD
- **FrigateRelay would-be-actions logged:** TBD
- **Matched pairs:** TBD
- **Missed alerts (legacy but not FrigateRelay):** TBD
- **Spurious alerts (FrigateRelay but not legacy):** TBD

## Missed alerts

TBD — table of `(timestamp, camera, label, action)` rows.

## Spurious alerts

TBD — table of `(timestamp, camera, label, action)` rows.

## Sign-off

The operator MUST review this report before declaring v1.0.0 cutover. Per
ROADMAP Phase 12 success criteria, every discrepancy is either explained as
an intentional improvement OR fixed before the cutover.
