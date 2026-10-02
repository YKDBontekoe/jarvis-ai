#!/usr/bin/env python3
"""Detect incompatible OpenAPI requests/responses. Baselines are reviewed source artifacts."""
import json
import os
import subprocess
import sys
from pathlib import Path

METHODS = {'get', 'put', 'post', 'delete', 'patch', 'head', 'options'}


def resolve(document, schema):
    while '$ref' in schema:
        reference = schema['$ref']
        if not reference.startswith('#/'): raise ValueError('External schema refs are unsupported')
        schema = document
        for part in reference[2:].split('/'):
            schema = schema[part.replace('~1', '/').replace('~0', '~')]
    return schema


def compare_schema(old_doc, new_doc, before, after, context, request, seen=None):
    seen = set() if seen is None else seen
    before, after = resolve(old_doc, before), resolve(new_doc, after)
    key = (id(before), id(after), request)
    if key in seen: return []
    seen.add(key)
    problems = []
    if before.get('type') != after.get('type') or before.get('format') != after.get('format'):
        problems.append(f'{context}: type or format changed')
    old_enum, new_enum = before.get('enum'), after.get('enum')
    if request and new_enum and (not old_enum or not set(old_enum) <= set(new_enum)):
        problems.append(f'{context}: request enum narrowed')
    if not request and old_enum and (not new_enum or not set(new_enum) <= set(old_enum)):
        problems.append(f'{context}: response enum expanded')
    old_required, new_required = set(before.get('required', [])), set(after.get('required', []))
    if request and new_required - old_required: problems.append(f'{context}: new required input')
    if not request and old_required - new_required: problems.append(f'{context}: required response became optional')
    for name, prop in before.get('properties', {}).items():
        next_prop = after.get('properties', {}).get(name)
        if next_prop is None: problems.append(f'{context}.{name}: property removed')
        else: problems.extend(compare_schema(old_doc, new_doc, prop, next_prop, f'{context}.{name}', request, seen))
    if 'items' in before:
        if 'items' not in after: problems.append(f'{context}: array items removed')
        else: problems.extend(compare_schema(old_doc, new_doc, before['items'], after['items'], context + '[]', request, seen))
    for bound in ('minimum', 'maximum', 'minLength', 'maxLength', 'minItems', 'maxItems', 'pattern', 'additionalProperties'):
        if request and before.get(bound) != after.get(bound) and bound in after:
            problems.append(f'{context}: input constraint changed: {bound}')
    for combinator in ('oneOf', 'anyOf', 'allOf'):
        if combinator in before or combinator in after:
            old_parts, new_parts = before.get(combinator, []), after.get(combinator, [])
            if len(old_parts) != len(new_parts): problems.append(f'{context}: {combinator} variants changed')
            for index, (left, right) in enumerate(zip(old_parts, new_parts)):
                problems.extend(compare_schema(old_doc, new_doc, left, right, f'{context}.{combinator}[{index}]', request, seen))
    return problems


def compare(before, after):
    problems = []
    for path, item in before['paths'].items():
        for method, operation in item.items():
            if method not in METHODS: continue
            current = after['paths'].get(path, {}).get(method)
            context = f'{method.upper()} {path}'
            if current is None:
                problems.append(f'{context}: operation removed'); continue
            old_params = {(p['in'], p['name']): p for p in item.get('parameters', []) + operation.get('parameters', [])}
            new_params = {(p['in'], p['name']): p for p in after['paths'][path].get('parameters', []) + current.get('parameters', [])}
            for key, parameter in new_params.items():
                prior = old_params.get(key)
                if parameter.get('required') and (prior is None or not prior.get('required')):
                    problems.append(f'{context}: new required parameter {key}')
                if prior: problems.extend(compare_schema(before, after, prior.get('schema', {}), parameter.get('schema', {}), f'{context} {key}', True))
            old_body, new_body = operation.get('requestBody', {}), current.get('requestBody', {})
            if new_body.get('required') and not old_body.get('required'): problems.append(f'{context}: request body became required')
            for kind, body in old_body.get('content', {}).items():
                successor = new_body.get('content', {}).get(kind)
                if successor is None: problems.append(f'{context}: request content type removed: {kind}')
                else: problems.extend(compare_schema(before, after, body.get('schema', {}), successor.get('schema', {}), context + ' request', True))
            for status, response in operation.get('responses', {}).items():
                if not status.startswith('2'): continue
                successor = current.get('responses', {}).get(status)
                if successor is None: problems.append(f'{context}: success status removed: {status}'); continue
                for kind, body in response.get('content', {}).items():
                    following = successor.get('content', {}).get(kind)
                    if following is None: problems.append(f'{context}: response content type removed: {kind}')
                    else: problems.extend(compare_schema(before, after, body.get('schema', {}), following.get('schema', {}), context + ' response', False))
    return problems


if __name__ == '__main__':
    baseline_path, candidate_path = sys.argv[1:3]
    baseline = Path(baseline_path).read_text()
    base = os.environ.get('BASE_SHA')
    if base and set(base) != {'0'}:
        result = subprocess.run(['git', 'show', f'{base}:{baseline_path}'], text=True, capture_output=True)
        if result.returncode == 0: baseline = result.stdout
    document = json.loads(baseline)
    if len(document['paths']) < 10: raise SystemExit('Contract baseline is empty or incomplete')
    issues = compare(document, json.loads(Path(candidate_path).read_text()))
    for issue in issues: print(issue)
    raise SystemExit(bool(issues))
