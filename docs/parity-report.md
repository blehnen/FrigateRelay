# FrigateRelay v1.0.0 — Parity Report

## Methodology — live A/B (not log reconcile)

The original parity window plan (per `docs/parity-window-checklist.md` and the original `RELEASING.md` checklist) was a `DryRun: true` window with NDJSON file logging on FrigateRelay, followed by a post-hoc reconcile against a CSV export from the legacy `FrigateMQTTProcessingService` using `tools/FrigateRelay.MigrateConf reconcile`.

For this release the operator switched to **live A/B observation** instead. Both services ran simultaneously with `DryRun: false` for every action plugin (BlueIris + Pushover), so every event fired notifications **from both pipelines**. Parity was verified directly by receiving matched-pair Pushover notifications on the operator's phone in real time.

This is a higher-fidelity check than the log-reconcile path: any false negative or false positive in the new pipeline surfaces immediately as a missing or extra notification, with no CSV export, NDJSON sink, or reconcile pass required. The cost is duplicate Blue Iris triggers (handled within Blue Iris's own debounce window) and 2× Pushover credits used for the duration of the window — both acceptable for a one-time validation.

The `tools/FrigateRelay.MigrateConf reconcile` subcommand and the NDJSON file sink (`Logging:File:CompactJson: true`) remain available for any future operator who prefers the original log-reconcile methodology.

## Window summary

- **Window:** 2026-04-30 → 2026-05-01 (~24 hours of continuous live A/B against real Frigate traffic)
- **RC under test:** `v1.0.0-rc3` (`ghcr.io/blehnen/frigaterelay:1.0.0-rc3`), deployed to the operator's Unraid host alongside the legacy `FrigateMQTTProcessingService`
- **Cameras observed:** all production cameras (driveway, front door, garage, back yard, side yard, deck — 6+ cameras) exercised by real motion events plus deliberate walk-around traffic
- **Pipeline shape:** Frigate MQTT → FrigateRelay → Blue Iris HTTP trigger + FrigateSnapshot → CodeProject.AI YOLOv5 6.2 validator → Pushover (gated on validator pass)
- **Validator:** CodeProject.AI gate active per-action with `MinConfidence: 0.50` and `OnError: FailClosed`
- **Missed alerts (legacy fired, FrigateRelay did not):** 0
- **Spurious alerts (FrigateRelay fired, legacy did not):** 0
- **Behavioral improvements observed (intentional, not divergence):** the CodeProject.AI per-action validator suppressed several Frigate false positives that the legacy pipeline would have notified on. This is by design — the legacy service has no validation tier — and counts as an intentional improvement, not a regression.

## Sign-off

Operator confirms 1:1 parity across all cameras and zero unexplained divergence over the 24-hour live A/B window. Cleared for `v1.0.0` tag on 2026-05-01.
