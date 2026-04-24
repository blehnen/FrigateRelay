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
                    image 'mcr.microsoft.com/dotnet/sdk:10.0'
                    // No args override: NuGet cache is workspace-local (--packages .nuget-cache).
                    // No external Docker volume required on the Jenkins agent host (OQ4).
                }
            }

            steps {
                sh 'dotnet restore FrigateRelay.sln --packages .nuget-cache'

                sh 'dotnet build FrigateRelay.sln -c Release --no-restore'

                // Abstractions test project — MTP coverage flags.
                // The -- separator passes arguments to the test executable, not to dotnet run.
                sh '''
                    dotnet run --project tests/FrigateRelay.Abstractions.Tests \
                        -c Release --no-build -- \
                        --coverage \
                        --coverage-output-format cobertura \
                        --coverage-output coverage/abstractions-tests/FrigateRelay.Abstractions.Tests.cobertura.xml
                '''

                // Host test project — MTP coverage flags.
                sh '''
                    dotnet run --project tests/FrigateRelay.Host.Tests \
                        -c Release --no-build -- \
                        --coverage \
                        --coverage-output-format cobertura \
                        --coverage-output coverage/host-tests/FrigateRelay.Host.Tests.cobertura.xml
                '''
            }

            post {
                always {
                    // Archive raw cobertura XML so it survives the workspace cleanup below.
                    // allowEmptyArchive: false — a missing XML means the coverage run silently
                    // failed; treat that as a pipeline error rather than swallowing it.
                    archiveArtifacts artifacts: 'coverage/**/*.cobertura.xml',
                                     allowEmptyArchive: false,
                                     fingerprint: true

                    // Publish coverage trends in Jenkins UI.
                    // Requires the modern "Coverage" plugin (jenkinsci/coverage-plugin).
                    // See: https://plugins.jenkins.io/coverage/
                    //
                    // Fallback for Jenkins instances still on the legacy Cobertura plugin:
                    // coberturaPublisher(coberturaReportFile: 'coverage/**/*.cobertura.xml')
                    recordCoverage(
                        tools: [[parser: 'COBERTURA', pattern: 'coverage/**/*.cobertura.xml']],
                        id:   'cobertura',
                        name: 'FrigateRelay Coverage'
                    )

                    // Remove workspace after archiving to keep agent disk clean.
                    cleanWs()
                }
            }
        }
    }

    post {
        failure {
            echo 'Pipeline failed. Check stage logs for details.'
        }
        success {
            echo 'Coverage pipeline completed successfully.'
        }
    }
}
