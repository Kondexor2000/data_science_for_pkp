pipeline {
    agent any

    stages {
        stage('Verify Python') {
            steps {
                sh 'python -m pip install ".[dev]"'
                sh 'python -m compileall -q src'
                sh 'server-activity data/sample_sessions.csv'
                sh 'test -s reports/server-summary.csv'
                sh 'test -s reports/player-summary.csv'
            }
        }

        stage('Test C# and frontend') {
            steps {
                sh 'dotnet test webapi.tests/ServerActivity.Api.Tests.csproj --configuration Release'
                dir('frontend') {
                    sh 'npm ci'
                    sh 'npm test'
                    sh 'npm run build'
                }
            }
        }

        stage('Build and smoke-test Docker') {
            steps {
                sh 'docker build -t server-activity-analysis .'
                sh 'mkdir -p reports/docker'
                sh 'docker run --rm -v "$WORKSPACE/reports/docker:/app/reports" server-activity-analysis'
                sh 'test -s reports/docker/server-summary.csv'
                sh 'test -s reports/docker/player-summary.csv'
            }
        }

        stage('Build C# and frontend images') {
            steps {
                sh 'docker compose build'
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'reports/**/*.csv', allowEmptyArchive: true
        }
    }
}