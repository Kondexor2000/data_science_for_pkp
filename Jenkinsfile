pipeline {
    agent any

    stages {
        stage('Verify Python') {
            steps {
                sh 'python -m pip install .'
                sh 'python -m compileall -q src'
                sh 'train-analysis data/sample_trips.csv --output reports/summary.csv'
                sh 'test -s reports/summary.csv'
            }
        }

        stage('Build and smoke-test Docker') {
            steps {
                sh 'docker build -t pila-poznan-train-analysis .'
                sh 'mkdir -p reports/docker'
                sh 'docker run --rm -v "$WORKSPACE/reports/docker:/app/reports" pila-poznan-train-analysis'
                sh 'test -s reports/docker/summary.csv'
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'reports/**/*.csv', allowEmptyArchive: true
        }
    }
}