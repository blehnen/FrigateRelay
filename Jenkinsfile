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
                    // No args override: NuGet cache is workspace-local (--packages .nuget-cache).
                    // No external Docker volume required on the Jenkins agent host (OQ4).
                    //
                    // PRECONDITION (Phase 4 — Testcontainers): the Jenkins agent host must mount
                    // /var/run/docker.sock into this Docker SDK container so Testcontainers can
                    // launch sibling containers (Mosquitto for FrigateRelay.IntegrationTests).
                    // Configure with the agent's `args` directive:
                    //   args '-v /var/run/docker.sock:/var/run/docker.sock'
                    // Without this, the integration suite fails at container start with
                    // "Cannot connect to the Docker daemon at unix:///var/run/docker.sock".
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
            }

            post {
                always {
                    // Archive raw cobertura XML so it survives the workspace cleanup below.
                    // allowEmptyArchive: false — a missing XML means the coverage run silently
                    // failed; treat that as a pipeline error rather than swallowing it.
                    archiveArtifacts artifacts: 'coverage/**/coverage.cobertura.xml',
                                     allowEmptyArchive: false,
                                     fingerprint: true

                    // Publish coverage trends in Jenkins UI.
                    // Requires the modern "Coverage" plugin (jenkinsci/coverage-plugin).
                    // See: https://plugins.jenkins.io/coverage/
                    //
                    // Fallback for Jenkins instances still on the legacy Cobertura plugin:
                    // coberturaPublisher(coberturaReportFile: 'coverage/**/*.cobertura.xml')
                    recordCoverage(
                        tools: [[parser: 'COBERTURA', pattern: 'coverage/**/coverage.cobertura.xml']],
                        id:   'cobertura',
                        name: 'FrigateRelay Coverage'
                    )

                    // Remove workspace after archiving to keep agent disk clean.
                    cleanWs()
                }
            }
        }
    }
}
