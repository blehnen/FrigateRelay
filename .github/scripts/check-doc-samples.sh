#!/usr/bin/env bash
# check-doc-samples.sh
#
# Extracts every fenced code block annotated with
#   ```csharp filename=<NAME>
# from docs/plugin-author-guide.md and byte-compares each block against
# samples/FrigateRelay.Samples.PluginGuide/<NAME>.
#
# Exit 0 — all blocks match their source files (no doc-rot).
# Exit 1 — one or more blocks drift from source; unified diffs printed.
#
# Environment overrides (for local testing against alternate paths):
#   DOC=<path>         default: docs/plugin-author-guide.md
#   SAMPLES_DIR=<dir>  default: samples/FrigateRelay.Samples.PluginGuide
set -euo pipefail

DOC=${DOC:-docs/plugin-author-guide.md}
SAMPLES_DIR=${SAMPLES_DIR:-samples/FrigateRelay.Samples.PluginGuide}

if [[ ! -f "$DOC" ]]; then
    echo "::error::Doc file not found: $DOC"
    exit 1
fi

if [[ ! -d "$SAMPLES_DIR" ]]; then
    echo "::error::Samples directory not found: $SAMPLES_DIR"
    exit 1
fi

python3 - "$DOC" "$SAMPLES_DIR" <<'PY'
import sys
import re
import pathlib
import difflib

doc_path = pathlib.Path(sys.argv[1])
samples_dir = pathlib.Path(sys.argv[2])

doc_text = doc_path.read_text(encoding="utf-8")

# Match fenced blocks: ```csharp filename=NAME (optional trailing content ignored)
# Capture everything up to the next bare ``` on its own line.
pat = re.compile(
    r"^```csharp\s+filename=(\S+)[^\n]*\n(.*?)^```",
    re.MULTILINE | re.DOTALL,
)

failures = 0
checked = 0

samples_root = samples_dir.resolve()

for match in pat.finditer(doc_text):
    filename = match.group(1)
    doc_body = match.group(2)
    checked += 1

    # Reject filenames that could escape the samples directory (CWE-22).
    # The fence filename is operator-controlled (anyone editing the doc),
    # so sanitize before any filesystem access. AUDIT-11 A1.
    parts = re.split(r"[\\/]", filename)
    if (
        "\x00" in filename
        or filename.startswith("/")
        or filename.startswith("\\")
        or any(p in ("..", "") for p in parts)
    ):
        print(
            f"::error file={doc_path}::"
            f"rejected unsafe fence filename: {filename!r}"
        )
        failures += 1
        continue

    sample_path = (samples_dir / filename).resolve()

    # Defense-in-depth: even with the textual checks above, confirm the
    # resolved path stays within samples_dir (catches symlink trickery).
    if samples_root not in sample_path.parents and sample_path != samples_root:
        print(
            f"::error file={doc_path}::"
            f"resolved sample path escapes samples dir: {sample_path}"
        )
        failures += 1
        continue

    if not sample_path.exists():
        print(
            f"::error file={doc_path}::"
            f"doc references missing sample file: {sample_path}"
        )
        failures += 1
        continue

    source_body = sample_path.read_text(encoding="utf-8")

    if doc_body == source_body:
        print(f"ok  {filename}")
        continue

    print(
        f"::error file={sample_path}::"
        f"doc/sample drift in {filename}"
    )
    diff_lines = list(
        difflib.unified_diff(
            source_body.splitlines(keepends=True),
            doc_body.splitlines(keepends=True),
            fromfile=str(sample_path),
            tofile=f"{doc_path} (fence: {filename})",
        )
    )
    sys.stdout.writelines(diff_lines)
    failures += 1

if checked == 0:
    print("::warning::No annotated fences found in doc — nothing to check.")
else:
    print(f"\n{checked} fence(s) checked, {failures} failure(s).")

sys.exit(failures)
PY
