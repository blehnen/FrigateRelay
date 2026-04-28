## Summary

<!-- One paragraph: what changes and why. Link to relevant issue if applicable. -->

## Type of change

- [ ] Bug fix
- [ ] New feature / enhancement
- [ ] Build / CI / docs
- [ ] Refactor / chore

## Checklist

- [ ] Build is green on Linux: `dotnet build FrigateRelay.sln -c Release`
- [ ] Tests pass: `bash .github/scripts/run-tests.sh`
- [ ] No new `.Result` / `.Wait()` calls in source
- [ ] No hard-coded IPs/hostnames or secrets in committed files
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` if user-visible
- [ ] Plugin author? Followed `docs/plugin-author-guide.md` and used the scaffold (`dotnet new frigaterelay-plugin`)
- [ ] Phase commit? Message follows `shipyard(phase-N): ...` convention

## Notes for reviewer

<!-- Risk areas, manual tests run, anything you want extra eyes on. -->
