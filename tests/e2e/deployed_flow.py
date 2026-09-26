"""Exercise a disposable Jarvis deployment through its public HTTP API.

Uses real PostgreSQL, Temporal, object storage, antivirus, and Codex inference.
Pass --models to include model-backed chat, memory recall, and background tasks.
Never point this script at an owner containing personal data: it creates fixtures.
"""

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid


class Jarvis:
    def __init__(self, base_url):
        self.base_url = base_url.rstrip('/')
        self.token = os.environ.get('JARVIS_E2E_ACCESS_TOKEN')

    def request(self, method, path, data=None, expected=(200,), raw=None, content_type=None):
        headers = {}
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'
        if data is not None:
            raw = json.dumps(data).encode()
            content_type = 'application/json'
        if content_type:
            headers['Content-Type'] = content_type
        request = urllib.request.Request(self.base_url + path, data=raw, method=method, headers=headers)
        try:
            response = urllib.request.urlopen(request, timeout=240)
        except urllib.error.HTTPError as error:
            response = error
        body = response.read()
        if response.code not in expected:
            raise AssertionError(f'{method} {path}: HTTP {response.code}; {body.decode(errors="replace")[:1200]}')
        if not body:
            return None
        return json.loads(body) if 'json' in response.headers.get('Content-Type', '') else body

    def wait_for(self, path, predicate, timeout=120):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            value = self.request('GET', path)
            if predicate(value):
                return value
            time.sleep(1)
        raise AssertionError(f'Timed out waiting for {path}; last state: {value}')


def iso(instant):
    return instant.astimezone(dt.timezone.utc).isoformat()


def run(args):
    api = Jarvis(args.url)
    results = []
    run_id = uuid.uuid4().hex[:12]
    report_path = Path(args.report)
    report_path.parent.mkdir(parents=True, exist_ok=True)

    def record(name, action):
        started = time.monotonic()
        try:
            evidence = action()
            result = {'name': name, 'status': 'passed', 'evidence': evidence}
        except Exception as error:
            result = {'name': name, 'status': 'failed', 'error': str(error)}
        result['duration_seconds'] = round(time.monotonic() - started, 3)
        results.append(result)
        report_path.write_text(json.dumps({
            'run_id': run_id, 'url': args.url, 'updated_at': iso(dt.datetime.now(dt.timezone.utc)),
            'model_path': 'Codex CLI + ChatGPT OAuth', 'checks': results,
        }, indent=2) + '\n')
        print(f'{result["status"].upper()}: {name}', flush=True)
        if result['status'] == 'failed':
            print(result['error'], flush=True)

    def baseline():
        paths = ['conversations', 'tasks', 'memory', 'approvals', 'reminders', 'notifications',
                 'files', 'watches', 'audit', 'mcp-servers', 'integrations/credentials', 'integrations/connections']
        for path in paths:
            assert isinstance(api.request('GET', '/api/v1/' + path), list), path
        return {'routes': paths}

    def conversations():
        api.request('POST', '/api/v1/conversations', {'title': 'x' * 201}, expected=(400,))
        item = api.request('POST', '/api/v1/conversations', {'title': f'E2E {run_id}'}, expected=(201,))
        try:
            details = api.request('GET', f'/api/v1/conversations/{item["id"]}')
            assert details['title'] == f'E2E {run_id}' and details['messages'] == []
            assert any(value['id'] == item['id'] for value in api.request('GET', '/api/v1/conversations'))
            api.request('POST', f'/api/v1/conversations/{item["id"]}/messages', {'content': ''}, expected=(400,))
        finally:
            api.request('DELETE', f'/api/v1/conversations/{item["id"]}', expected=(204,))
        api.request('GET', f'/api/v1/conversations/{item["id"]}', expected=(404,))
        return {'conversation_id': item['id'], 'validated': ['create', 'list', 'get', 'validation', 'delete']}

    def memory():
        data = {'kind': 'preference', 'content': f'E2E {run_id}: meeting notes use concise bullets.',
                'importance': 0.8, 'confidence': 1, 'isPinned': False}
        item = api.request('POST', '/api/v1/memory', data, expected=(201,))
        try:
            assert api.request('GET', f'/api/v1/memory/{item["id"]}')['content'] == data['content']
            data['content'] = f'E2E {run_id}: meeting notes use numbered decisions with action owners.'
            updated = api.request('PUT', f'/api/v1/memory/{item["id"]}', data)
            assert updated['content'] == data['content']
            assert any(value['id'] == item['id'] for value in api.request('GET', '/api/v1/memory?kind=preference'))
        finally:
            api.request('DELETE', f'/api/v1/memory/{item["id"]}', expected=(204,))
        api.request('GET', f'/api/v1/memory/{item["id"]}', expected=(404,))
        return {'memory_id': item['id'], 'validated': ['create', 'list', 'edit', 'delete']}

    def credentials():
        provider = 'e2e-' + run_id
        dummy = 'synthetic-verification-token-' + run_id
        try:
            api.request('PUT', f'/api/v1/integrations/{provider}/credentials/token', {'value': dummy}, expected=(200, 204))
            metadata = api.request('GET', f'/api/v1/integrations/{provider}/credentials')
            assert dummy not in json.dumps(metadata), 'Credential value leaked in metadata'
            assert 'token' in metadata['secretNames']
            api.request('PUT', f'/api/v1/integrations/{provider}/credentials/token', {'value': dummy + '-rotated'}, expected=(200, 204))
            audit = api.request('GET', '/api/v1/audit')
            assert dummy not in json.dumps(audit), 'Credential value leaked in audit'
            api.request('DELETE', f'/api/v1/integrations/{provider}/credentials/token', expected=(204,))
        finally:
            api.request('DELETE', f'/api/v1/integrations/{provider}/credentials', expected=(204, 404))
        return {'provider': provider, 'validated': ['save', 'metadata-only', 'rotate', 'audit-redaction', 'delete']}

    def mcp_boundary():
        for endpoint in ['http://example.com/mcp', 'https://localhost/mcp', 'https://127.0.0.1/mcp',
                         'https://user:password@example.com/mcp']:
            api.request('POST', '/api/v1/mcp-servers', {'name': 'Verification', 'endpoint': endpoint,
                        'allowedTools': ['list_items']}, expected=(400,))
        return {'rejected_endpoint_count': 4}

    def reminder():
        due_at = dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=12)
        item = api.request('POST', '/api/v1/reminders', {'title': f'E2E reminder {run_id}', 'dueAt': iso(due_at)}, expected=(201,))
        finished = api.wait_for(f'/api/v1/reminders/{item["id"]}', lambda value: value['status'] == 'completed', timeout=90)
        notifications = api.wait_for('/api/v1/notifications', lambda values: any(value.get('sourceId') == item['id'] for value in values))
        notification = next(value for value in notifications if value.get('sourceId') == item['id'])
        assert notification['type'] == 'reminder.due'
        api.request('POST', f'/api/v1/notifications/{notification["id"]}/read', expected=(200, 204))
        assert next(value for value in api.request('GET', '/api/v1/notifications') if value['id'] == notification['id'])['readAt']
        return {'reminder_id': item['id'], 'notification_id': notification['id'], 'completed_at': finished['completedAt']}

    def cancellation():
        due_at = dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=1)
        item = api.request('POST', '/api/v1/reminders', {'title': f'E2E cancelled {run_id}', 'dueAt': iso(due_at)}, expected=(201,))
        cancelled = api.request('DELETE', f'/api/v1/reminders/{item["id"]}')
        assert cancelled['status'] == 'cancelled'
        return {'reminder_id': item['id'], 'status': cancelled['status']}

    def upload():
        content = f'Verification invoice {run_id}. Total 123.45 EUR. Renewal 2027-10-01.'.encode()
        boundary = 'JarvisVerification' + run_id
        raw = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="verification-{run_id}.txt"\r\n'
               'Content-Type: text/plain\r\n\r\n').encode() + content + f'\r\n--{boundary}--\r\n'.encode()
        item = api.request('POST', '/api/v1/files', raw=raw, content_type='multipart/form-data; boundary=' + boundary, expected=(201,))
        try:
            assert api.request('GET', f'/api/v1/files/{item["id"]}/content') == content
            files = api.wait_for('/api/v1/files', lambda values: any(value['id'] == item['id'] and
                                 value.get('processingStatus') == 'ready' for value in values), timeout=90)
            hits = api.request('GET', '/api/v1/files/search?query=' + urllib.parse.quote(run_id))
            assert any(value['fileId'] == item['id'] for value in hits), 'Extracted text was not searchable'
            return {'file_id': item['id'], 'validated': ['scan', 'upload', 'download', 'extract', 'search']}
        finally:
            api.request('DELETE', f'/api/v1/files/{item["id"]}', expected=(204,))

    def chat():
        item = api.request('POST', '/api/v1/conversations', {'title': f'E2E Codex {run_id}'}, expected=(201,))
        expected = 'VERIFIED_' + run_id
        response = api.request('POST', f'/api/v1/conversations/{item["id"]}/messages',
                               {'content': 'Reply with exactly this text: ' + expected})
        assert expected in response['content'], response
        details = api.request('GET', f'/api/v1/conversations/{item["id"]}')
        assert any(value['role'] == 'user' for value in details['messages'])
        assert any(expected in value['content'] and value['role'] == 'assistant' for value in details['messages'])
        return {'conversation_id': item['id'], 'assistant_content': response['content']}

    def recall():
        data = {'kind': 'preference', 'content': f'The user prefers meeting notes as concise bullets with decisions and action owners. E2E {run_id}.',
                'importance': 0.8, 'confidence': 1, 'isPinned': False}
        memory = api.request('POST', '/api/v1/memory', data, expected=(201,))
        try:
            conversation = api.request('POST', '/api/v1/conversations', {'title': f'E2E memory {run_id}'}, expected=(201,))
            response = api.request('POST', f'/api/v1/conversations/{conversation["id"]}/messages',
                                   {'content': 'How do I prefer my meeting notes formatted?'})
            text = response['content'].lower()
            for phrase in ['bullet', 'decision', 'action']:
                assert phrase in text, response
            return {'dataset_case': 'memory-recall-preference', 'conversation_id': conversation['id'], 'assistant_content': response['content']}
        finally:
            api.request('DELETE', f'/api/v1/memory/{memory["id"]}', expected=(204,))

    def task():
        expected = 'TASK_VERIFIED_' + run_id
        item = api.request('POST', '/api/v1/tasks', {'title': f'E2E task {run_id}',
                          'prompt': 'Reply with exactly this text: ' + expected}, expected=(201,))
        completed = api.wait_for(f'/api/v1/tasks/{item["id"]}', lambda value: value['status'] in
                                ['completed', 'failed', 'needs_approval'], timeout=240)
        assert completed['status'] == 'completed', completed
        messages = api.request('GET', f'/api/v1/tasks/{item["id"]}/messages')
        assert any(expected in value['content'] for value in messages), messages
        notifications = api.wait_for('/api/v1/notifications', lambda values: any(value.get('sourceId') == item['id'] for value in values))
        assert next(value for value in notifications if value.get('sourceId') == item['id'])['type'] == 'task.completed'
        return {'task_id': item['id'], 'summary': completed['summary']}

    for name, action in [('api-route-health', baseline), ('conversation-persistence', conversations),
                         ('memory-crud', memory), ('credential-boundary', credentials), ('mcp-endpoint-boundary', mcp_boundary),
                         ('temporal-reminder-and-notification', reminder), ('durable-cancellation', cancellation),
                         ('file-storage-scan-and-extraction', upload)]:
        record(name, action)
    if args.models:
        for name, action in [('codex-chat-and-persistence', chat), ('memory-recall-evaluation', recall), ('background-task-and-notification', task)]:
            record(name, action)
    return 1 if any(value['status'] == 'failed' for value in results) else 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--url', default='http://localhost:5082')
    parser.add_argument('--report', default='artifacts/verification/deployed-flow.json')
    parser.add_argument('--models', action='store_true')
    raise SystemExit(run(parser.parse_args()))
