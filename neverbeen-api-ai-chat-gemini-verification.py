#!/usr/bin/env python3
"""Verifies neverbeen-api-ai-chat-gemini.patch.

  A  the patch applies cleanly to the neverbeen-api checkout (main, 7c92247 or later)
  B  every .cs file it adds or changes parses without syntax errors (tree-sitter)
  C  the route is the one the website calls through its proxy:
       POST api/ai-chat   (website: POST /neverbeen-api/api/ai-chat)
     and the request carries the visitor's input (`Message`) and the model's character profile
     (`CharacterProfile`, her personality/characterdatabase.json) — both required
  D  the chat runs on gemini-2.5-flash, and the key is read from the GEMINI_API_KEY environment
     variable / configuration and sent in the x-goog-api-key header, never in the URL
  E  no Gemini key is committed: appsettings*.json carry no GEMINI_API_KEY value, and nothing in
     the patch looks like a Google API key
  F  Program.cs registers the Gemini HttpClient, the "ai-chat" rate-limit policy and
     app.UseRateLimiter(); the controller opts into that policy
  G  no database change: the patch adds no entity, no DbSet and touches no AppDbContext

Dependencies: git, and the tree_sitter / tree_sitter_c_sharp packages
(pip install tree-sitter tree-sitter-c-sharp).

Run:  python3 neverbeen-api-ai-chat-gemini-verification.py [path-to-neverbeen-api]
"""

import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-ai-chat-gemini.patch')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
failures = []


def check(label, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + label + ('' if ok or not detail else f'  -- {detail}'))
    if not ok:
        failures.append(label)


def git(args, cwd):
    return subprocess.run(['git'] + args, cwd=cwd, capture_output=True, text=True)


def read(root, rel):
    path = os.path.join(root, rel)
    if not os.path.exists(path):
        return ''
    with open(path, encoding='utf-8') as fh:
        return fh.read()


# A
tmp = tempfile.mkdtemp(prefix='api-ai-chat-')
subprocess.run(['git', 'clone', '-q', API_DIR, tmp], check=True)
r = git(['apply', '--check', PATCH], tmp)
check('A  patch applies to neverbeen-api', r.returncode == 0, r.stderr.strip())
if r.returncode == 0:
    git(['apply', PATCH], tmp)

with open(PATCH, encoding='utf-8') as fh:
    patch_text = fh.read()
changed = [line[len('+++ b/'):].strip() for line in patch_text.splitlines() if line.startswith('+++ b/')]

# B
try:
    import tree_sitter_c_sharp as tscs
    from tree_sitter import Language, Parser
    parser = Parser(Language(tscs.language()))
    for rel in changed:
        if not rel.endswith('.cs'):
            continue
        with open(os.path.join(tmp, rel), 'rb') as fh:
            tree = parser.parse(fh.read())
        check(f'B  {rel} parses', not tree.root_node.has_error)
except ImportError:
    check('B  tree-sitter available (pip install tree-sitter tree-sitter-c-sharp)', False)

controller = read(tmp, 'Controllers/AiChatController.cs')
dtos = read(tmp, 'Dtos/AiChatDtos.cs')
prompt = read(tmp, 'Services/AiChatPrompt.cs')
service = read(tmp, 'Services/GeminiChatService.cs')
program = read(tmp, 'Program.cs')

# C
check('C  route api/ai-chat', '[Route("api/ai-chat")]' in controller and '[HttpPost]' in controller)
check('C  request has Message', 'public string? Message { get; set; }' in dtos)
check('C  request has CharacterProfile (JSON object)', 'public JsonElement? CharacterProfile { get; set; }' in dtos)
check('C  empty message is refused', '"empty-message"' in prompt)
check('C  missing character profile is refused', '"missing-character-profile"' in prompt
      and 'JsonValueKind.Object' in prompt)
check('C  character profile goes into the system instruction', 'turn.CharacterProfileJson' in prompt)
check('C  response carries Reply', 'public string Reply { get; set; }' in dtos)

# D
check('D  model is gemini-2.5-flash', 'public const string ChatModel = "gemini-2.5-flash";' in service)
check('D  key read from GEMINI_API_KEY', 'public const string ApiKeySetting = "GEMINI_API_KEY";' in service
      and '_configuration[ApiKeySetting]' in service
      and 'Environment.GetEnvironmentVariable(ApiKeySetting)' in service)
check('D  key sent in x-goog-api-key header', 'request.Headers.Add("x-goog-api-key", apiKey)' in service)
check('D  key never in the URL', '?key=' not in service and 'key={' not in service)
check('D  no key configured -> 503 ai-unavailable', 'Status503ServiceUnavailable' in controller
      and '"ai-unavailable"' in controller)

# E
for rel in ('appsettings.json', 'appsettings.Development.json'):
    settings = read(tmp, rel)
    m = re.search(r'"GEMINI_API_KEY"\s*:\s*"([^"]*)"', settings)
    check(f'E  {rel} holds no Gemini key', m is None or m.group(1) == '')
added = '\n'.join(line for line in patch_text.splitlines() if line.startswith('+'))
check('E  no Google API key in the patch', re.search(r'AIza[0-9A-Za-z_\-]{30,}|AQ\.[0-9A-Za-z_\-]{30,}', added) is None)

# F
check('F  Gemini HttpClient registered', 'AddHttpClient<GeminiChatService>' in program)
check('F  rate-limit policy registered', 'AddRateLimiter' in program
      and 'AiChatController.RateLimitPolicy' in program)
check('F  rate limiter in the pipeline', 'app.UseRateLimiter();' in program
      and program.index('app.UseRateLimiter();') < program.index('app.MapControllers();'))
check('F  controller opts into the policy', '[EnableRateLimiting(RateLimitPolicy)]' in controller)

# G
check('G  no database change', not any(rel.startswith('Entities/') or rel == 'Data/AppDbContext.cs' for rel in changed))

print()
if failures:
    print(f'{len(failures)} check(s) failed')
    sys.exit(1)
print('All checks passed')
