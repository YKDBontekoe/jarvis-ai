"""Fault, backup/restore and latency checks on the disposable CI project only."""
import datetime as dt
import hashlib
import json
import os
import statistics
import subprocess
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from deployed_flow import Jarvis

api = Jarvis(os.environ.get('JARVIS_API_URL', 'http://localhost:5082'))
compose = ['docker', 'compose', '-p', 'jarvis-ci', '-f', 'tests/e2e/docker-compose.ci.yml']
checks = []

def run(*args, **kwargs): return subprocess.run([*compose, *args], check=True, **kwargs)
def query(database, sql):
    return subprocess.check_output([*compose, 'exec', '-T', 'postgres', 'psql', '-U', 'jarvis', '-d', database, '-At', '-c', sql], text=True).strip()

def record(name, action):
    started = time.monotonic()
    evidence = action()
    checks.append({'name': name, 'status': 'passed', 'seconds': round(time.monotonic() - started, 3), 'evidence': evidence})
    Path('artifacts/ci/resilience.json').write_text(json.dumps({'checks': checks}, indent=2) + '\n')
    print('PASS ' + name, flush=True)

def worker_recovery():
    run('stop', 'jarvis-worker')
    try:
        probe = subprocess.run([*compose, 'exec', '-T', 'jarvis-api', 'dotnet', 'Jarvis.Api.dll', 'deployment-probe'], timeout=60)
        assert probe.returncode != 0, 'Deployment probe accepted a stopped worker'
        reminder = api.request('POST', '/api/v1/reminders', {'title': 'Restart recovery fixture',
            'dueAt': (dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=3)).isoformat()}, expected=(201,))
        time.sleep(4)
    finally:
        run('start', 'jarvis-worker')
    api.wait_for('/api/v1/reminders/' + reminder['id'], lambda value: value['status'] == 'completed', timeout=90)
    run('restart', 'jarvis-worker')
    time.sleep(5)
    notifications = api.request('GET', '/api/v1/notifications')
    assert len([value for value in notifications if value.get('sourceId') == reminder['id']]) == 1
    return {'stoppedWorkerRejected': True, 'reminderRecovered': True, 'deliveries': 1}

def readiness():
    run('pause', 'postgres')
    try:
        api.request('GET', '/alive')
        api.request('GET', '/health', expected=(503,))
    finally:
        run('unpause', 'postgres')
    api.wait_for('/health', lambda value: value == b'Healthy', timeout=30)
    return {'databaseOutageRejected': True, 'livenessIndependent': True}

def fingerprint(database):
    tables = query(database, "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename").splitlines()
    rows = []
    for table in tables:
        identifier = table.replace('"', '""')
        digest = query(database, f'SELECT count(*)::text || \'|\' || coalesce(md5(string_agg(row::text, \'\' ORDER BY row::text)), \'empty\') FROM (SELECT to_jsonb(t) AS row FROM public."{identifier}" t) AS data')
        rows.append(table + ':' + digest)
    return hashlib.sha256('\n'.join(rows).encode()).hexdigest(), len(tables)

def restore():
    # Quiesce writes so a restored consistent snapshot can be compared byte-for-byte at the row level.
    run('stop', 'jarvis-api', 'jarvis-worker')
    try:
        before, tables = fingerprint('jarvis')
        with tempfile.TemporaryFile() as backup:
            run('exec', '-T', 'postgres', 'pg_dump', '-U', 'jarvis', '-d', 'jarvis', '-Fc', stdout=backup)
            assert backup.tell() > 0
            query('postgres', 'CREATE DATABASE jarvis_restore')
            try:
                backup.seek(0)
                run('exec', '-T', 'postgres', 'pg_restore', '--exit-on-error', '-U', 'jarvis', '-d', 'jarvis_restore', stdin=backup)
                after, restored_tables = fingerprint('jarvis_restore')
                assert (before, tables) == (after, restored_tables), 'Restored database differs from backup source'
            finally:
                query('postgres', 'DROP DATABASE jarvis_restore')
    finally:
        run('start', 'jarvis-worker', 'jarvis-api')
    return {'tablesVerified': tables, 'dataAndOwnerScopePreserved': True}

def performance():
    def sample(_):
        start = time.monotonic()
        api.request('GET', '/api/v1/conversations')
        return (time.monotonic() - start) * 1000
    api.request('GET', '/api/v1/conversations')
    with ThreadPoolExecutor(max_workers=4) as executor:
        times = sorted(executor.map(sample, range(40)))
    p95 = times[37]
    assert p95 < float(os.environ.get('JARVIS_API_P95_MAX_MS', '1000')), f'API p95 latency exceeded budget: {p95:.0f}ms'
    return {'samples': len(times), 'concurrency': 4, 'p50Ms': round(statistics.median(times)), 'p95Ms': round(p95)}

record('worker-outage-restart-and-delivery-deduplication', worker_recovery)
record('readiness-during-database-outage', readiness)
record('database-backup-and-restore', restore)
record('api-latency-budget', performance)
