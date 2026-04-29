---
plan: 2.4
wave: 2
status: completed
---

# PLAN-2.4 Summary: GitHub issue/PR templates + docs.yml

## Tasks

| # | Description | Status | Commit |
|---|-------------|--------|--------|
| 1 | Issue templates (bug_report.yml, feature_request.yml, config.yml) | completed | bba935d |
| 2 | Pull request template (pull_request_template.md) | completed | e1a746a |
| 3 | docs.yml workflow (scaffold-smoke + samples-build) | completed | e7ba81f |

## Files created

- .github/ISSUE_TEMPLATE/bug_report.yml
- .github/ISSUE_TEMPLATE/feature_request.yml
- .github/ISSUE_TEMPLATE/config.yml
- .github/pull_request_template.md
- .github/workflows/docs.yml

## Verification results

- All files exist and pass YAML validation.
- All grep acceptance criteria pass.
- Solution build clean (dotnet build FrigateRelay.sln -c Release, 0 warnings, 0 errors).
- No secrets or hard-coded IPs in any delivered file.
- Split-CI invariant holds: no coverage/TRX in docs.yml.

## Caveats

- scaffold-smoke job will skip until PLAN-2.3 lands the template at templates/FrigateRelay.Plugins.Template/ (conditional via hashFiles guard).
- samples-build job is a no-op until PLAN-3.1 lands samples/FrigateRelay.Samples.PluginGuide/ (conditional via detect step output).
- config.yml security advisory URL uses owner/repo placeholder — needs real GitHub slug when remote is configured.
