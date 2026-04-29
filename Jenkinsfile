// Jenkinsfile
// FrigateRelay — coverage pipeline (Phase 2, D1)
//
// Scope: restore → build → coverage run per test project → archive cobertura XML.
// NOT a release/publish pipeline — that is Phase 10.
// Decision refs: D1 (Jenkins owns coverage), D2 (MTP dotnet run invocation).
//
// Agent: mcr.microsoft.com/dotnet/sdk:10.0 (no pre-installed .NET required on host).
// NuGet cache: workspace-local --packages .nuget-cache (OQ4 resolution — no named
//   Docker volume required; zero pre-provisioning, self-contained across agents).
// Coverage plugin: modern Coverage plugin (recordCoverage). Fallback line for legacy
//   Cobertura plugin is commented out immediately above recordCoverage (OQ3 resolution).

pipeline {
    agent none

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO               = '1'
        NUGET_XMLDOC_MODE           = 'skip'
    }

    triggers {
        // Weekly on Sunday at 02:00, in addition to push-triggered runs.
        cron('0 2 * * 0')
    }

    stages {
        stage('Build & Coverage') {
            agent {
                docker {
                    // Pin to a cloud template whose Remote File System Root matches the
                    // host bind-mount path, and whose agent image (jenkins-agent-with-docker)
                    // ships a Docker CLI. Required because `agent { docker { } }` runs the
                    // SDK image as a SIBLING on the chosen agent — not nested — so the
                    // agent's workspace path must exist on the Docker daemon's host. The
                    // shared/default cloud template doesn't satisfy either constraint.
                    label 'frigaterelay'
                    // Digest pin: bump manually when Dependabot bumps docker/Dockerfile (Jenkinsfile is not in the Dependabot docker watch).
                    // To update: docker pull mcr.microsoft.com/dotnet/sdk:10.0 && docker inspect mcr.microsoft.com/dotnet/sdk:10.0 --format '{{index .RepoDigests 0}}'
                    image 'mcr.microsoft.com/dotnet/sdk:10.0@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc'
                    // Mount the host's Docker socket into the SDK sibling so Testcontainers
                    // (FrigateRelay.IntegrationTests/Fixtures/MosquittoFixture) can launch
                    // its Mosquitto sidecar against the same daemon. Without this, the
                    // suite fails fast with "Docker is either not running or misconfigured
                    // (Parameter 'DockerEndpointAuthConfig')".
                    //
                    // --group-add: Jenkins's Docker Pipeline plugin runs the SDK sibling
                    // with -u $(id -u):$(id -g) inherited from the agent (uid 1000,
                    // jenkins). The host docker.sock is owned root:281 on Unraid, so
                    // uid 1000 has no access without supplementary group membership.
                    // 281 matches the agent template's "Extra group GIDs"; verify on the
                    // host with `stat -c '%g' /var/run/docker.sock`.
                    args '-v /var/run/docker.sock:/var/run/docker.sock --group-add 281'
                }
            }

            steps {
                sh 'dotnet restore FrigateRelay.sln --packages .nuget-cache'

                sh 'dotnet build FrigateRelay.sln -c Release --no-restore'

                // Delegates to the shared run-tests.sh script (Phase 3). The script
                // auto-discovers every tests/*.Tests/*.Tests.csproj and calls each via
                // `dotnet run` with MTP coverage flags. Output lands at
                //   coverage/<TestProjectName>/coverage.cobertura.xml
                // so adding a new test project requires no Jenkinsfile edit.
                sh 'bash .github/scripts/run-tests.sh --coverage'

                // Merge per-project cobertura XMLs into a single browsable HTML report
                // (and a unified Cobertura.xml) using ReportGenerator. Mirrors the
                // DotNetWorkQueue pipeline pattern. publishHTML in post-always picks
                // up coverage/report/index.html.
                //
                // Tool is installed workspace-locally (--tool-path) rather than -g so we
                // don't depend on $HOME being set inside the SDK container under the
                // -u 1000:1000 override. `|| true` handles the "already installed" case
                // when reportgenerator is cached from a prior run that wasn't cleanWs'd.
                withCredentials([string(credentialsId: 'reportgenerator-license', variable: 'REPORTGENERATOR_LICENSE')]) {
                    sh '''
                        dotnet tool install --tool-path .dotnet-tools dotnet-reportgenerator-globaltool || true
                        .dotnet-tools/reportgenerator \
                            -reports:"coverage/**/coverage.cobertura.xml" \
                            -targetdir:coverage/report \
                            -reporttypes:"Html;Cobertura;Badges" \
                            -assemblyfilters:"-FrigateRelay.TestHelpers;-FrigateRelay.MigrateConf" \
                            -license:"$REPORTGENERATOR_LICENSE"
                    '''
                }
                // -assemblyfilters: TestHelpers is shared test-utility code (CapturingLogger
                // and friends), MigrateConf is a one-shot migration console tool. Neither is
                // part of the runtime service surface, so neither belongs in the production
                // coverage metric. The filter applies to BOTH the merged HTML report and the
                // merged Cobertura.xml that Codecov ingests — single point of exclusion.

                // Upload the merged Cobertura.xml to Codecov for trend tracking.
                // codecov-token-frigaterelay is FrigateRelay-specific (separate from
                // DotNetWorkQueue's codecov-token so the projects don't clobber).
                // Upload failures are non-fatal — Codecov flakes shouldn't break CI.
                withCredentials([string(credentialsId: 'codecov-token-frigaterelay', variable: 'CODECOV_TOKEN')]) {
                    sh '''
                        curl -Os https://cli.codecov.io/latest/linux/codecov
                        chmod +x codecov
                        ./codecov upload-process --file coverage/report/Cobertura.xml --token "$CODECOV_TOKEN" || echo "Codecov upload failed (non-fatal)"
                    '''
                }
            }

            post {
                always {
                    // Archive raw per-project cobertura XML so it survives the workspace
                    // cleanup below. allowEmptyArchive: false — a missing XML means the
                    // coverage run silently failed; treat as a pipeline error.
                    archiveArtifacts artifacts: 'coverage/**/coverage.cobertura.xml',
                                     allowEmptyArchive: false,
                                     fingerprint: true

                    // Publish the merged HTML coverage report into the Jenkins UI via
                    // the HTML Publisher plugin (already installed). Replaces the
                    // recordCoverage step that needed the Coverage plugin.
                    publishHTML(target: [
                        allowMissing: false,
                        alwaysLinkToLastBuild: true,
                        keepAll: true,
                        reportDir: 'coverage/report',
                        reportFiles: 'index.html',
                        reportName: 'FrigateRelay Coverage'
                    ])

                    // Remove workspace after archiving to keep agent disk clean.
                    cleanWs()
                }
            }
        }
    }
}
