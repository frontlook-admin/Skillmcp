import subprocess, json, threading, time, os, sys

PASS = 'PASS'
FAIL = 'FAIL'
pass_count = 0
fail_count = 0

def run_mcp(image, tool_name, args, wait=10):
    p = subprocess.Popen(
        ['docker', 'run', '--rm', '-i', '-v', 'G:\\Repos:/g/Repos', image],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL
    )
    results = {}
    def reader():
        for line in p.stdout:
            try:
                obj = json.loads(line)
                if 'id' in obj:
                    results[obj['id']] = obj
            except Exception:
                pass
    threading.Thread(target=reader, daemon=True).start()
    msgs = [
        {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {
            'protocolVersion': '2025-11-25', 'capabilities': {},
            'clientInfo': {'name': 'test', 'version': '1'}}},
        {'jsonrpc': '2.0', 'method': 'notifications/initialized', 'params': {}},
        {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call',
         'params': {'name': tool_name, 'arguments': args}},
    ]
    for m in msgs:
        p.stdin.write((json.dumps(m) + '\n').encode())
        p.stdin.flush()
        time.sleep(0.1)
    time.sleep(wait)
    p.terminate()
    return results.get(2, {}).get('result', {}).get('content', [{}])[0].get('text', 'MISSING')

def check(label, text, expected, negate=False):
    global pass_count, fail_count
    ok = (expected not in text) if negate else (expected in text)
    tag = PASS if ok else FAIL
    print('  [%s] %s' % (tag, label))
    if not ok:
        suffix = ' NOT' if negate else ''
        print('         expected%s : %r' % (suffix, expected))
        print('         got       : %r' % text[:300])
    if ok:
        pass_count += 1
    else:
        fail_count += 1

LOCAL   = 'skillmcp:local'
PROJECT = 'G:\\\\Repos\\\\frontlook-admin\\\\SkillMcp'
SINGLE  = 'G:\\Repos\\frontlook-admin\\SkillMcp'

# ──────────────────────────────────────────────────────────────────────────────
print('=' * 58)
print('  [Path translation]')
print('=' * 58)

print()
print('[1] detect_project_type — single backslash')
t = run_mcp(LOCAL, 'detect_project_type', {'targetProject': SINGLE})
check('resolves to /g/Repos/...', t, '/g/Repos/frontlook-admin/SkillMcp')
check('no double slash', t, '/g//Repos', negate=True)
check('detects AspNetCoreApi', t, 'AspNetCoreApi')

print()
print('[2] detect_project_type — double backslash (escaped)')
t = run_mcp(LOCAL, 'detect_project_type', {'targetProject': PROJECT})
check('resolves to /g/Repos/...', t, '/g/Repos/frontlook-admin/SkillMcp')
check('no double slash', t, '/g//Repos', negate=True)
check('detects AspNetCoreApi', t, 'AspNetCoreApi')

print()
print('[3] detect_project_type — null target defaults to /app')
t = run_mcp(LOCAL, 'detect_project_type', {})
check('defaults to /app', t, '/app')
check('returns Detected types', t, 'Detected types')

# ──────────────────────────────────────────────────────────────────────────────
print()
print('=' * 58)
print('  [Tool responses]')
print('=' * 58)

print()
print('[4] check_project_skills — dry run')
t = run_mcp(LOCAL, 'check_project_skills', {'targetProject': PROJECT})
check('detects AspNetCoreApi', t, 'AspNetCoreApi')
check('DRY-RUN mode header', t, 'DRY-RUN')
check('no ERROR', t, 'ERROR', negate=True)

print()
print('[5] setup_project_skills — incremental (skills already present)')
t = run_mcp(LOCAL, 'setup_project_skills', {'targetProject': PROJECT}, wait=30)
check('detects AspNetCoreApi', t, 'AspNetCoreApi')
check('no ERROR', t, 'ERROR', negate=True)
check('reports total installed', t, 'Total installed')
check('0 added (already present)', t, 'Added: 0')

# ──────────────────────────────────────────────────────────────────────────────
print()
print('=' * 58)
print('  [Filesystem]')
print('=' * 58)

skills_dir = r'G:\Repos\frontlook-admin\SkillMcp\skills'
skills = sorted(os.listdir(skills_dir)) if os.path.isdir(skills_dir) else []
skills_str = ','.join(skills)

print()
print('[6] skills/ folder — expected skill folders present')
check('git-commit', skills_str, 'git-commit')
check('conventional-commit', skills_str, 'conventional-commit')
check('csharp-async', skills_str, 'csharp-async')
check('aspnet-minimal-api-openapi', skills_str, 'aspnet-minimal-api-openapi')
check('dotnet-best-practices', skills_str, 'dotnet-best-practices')
check('skills.json manifest', skills_str, 'skills.json')
print('  Skills on disk (%d):' % len([s for s in skills if s != 'skills.json']))
for s in skills:
    print('    + ' + s)

print()
print('[7] skills.json — valid manifest')
manifest_path = os.path.join(skills_dir, 'skills.json')
try:
    with open(manifest_path) as f:
        manifest = json.load(f)
    check('detectedType = AspNetCoreApi', json.dumps(manifest), 'AspNetCoreApi')
    check('skills list present', json.dumps(manifest), '"skills"')
    check('sourcePath present', json.dumps(manifest), 'sourcePath')
except Exception as e:
    print('  [FAIL] could not read skills.json: ' + str(e))
    fail_count += 1

print()
print('=' * 58)
print('  Results: %d passed, %d failed' % (pass_count, fail_count))
print('=' * 58)
sys.exit(0 if fail_count == 0 else 1)
