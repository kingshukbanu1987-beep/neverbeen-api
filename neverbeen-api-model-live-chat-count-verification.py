#!/usr/bin/env python3
"""Verifies neverbeen-api-model-live-chat-count.patch.

  A  the patch applies cleanly to the neverbeen-api checkout (main, de8b40f or later)
  B  every .cs file it adds or changes parses without syntax errors (tree-sitter)
  C  the routes are the ones the website calls:
       GET  api/models/live-chats
       POST api/models/{modelSlug}/live-chats

Dependencies: git, and the tree_sitter / tree_sitter_c_sharp packages
(pip install tree-sitter tree-sitter-c-sharp).

Run:  python3 neverbeen-api-model-live-chat-count-verification.py [path-to-neverbeen-api]
"""

import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-model-live-chat-count.patch')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
failures = []


def check(label, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + label + ('' if ok or not detail else f'  -- {detail}'))
    if not ok:
        failures.append(label)


def git(args, cwd):
    return subprocess.run(['git'] + args, cwd=cwd, capture_output=True, text=True)


# A
tmp = tempfile.mkdtemp(prefix='api-live-chat-')
subprocess.run(['git', 'clone', '-q', API_DIR, tmp], check=True)
r = git(['apply', '--check', PATCH], tmp)
check('A  patch applies to neverbeen-api', r.returncode == 0, r.stderr.strip())
if r.returncode == 0:
    git(['apply', PATCH], tmp)

# B
try:
    import tree_sitter_c_sharp as tscs
    from tree_sitter import Language, Parser
    parser = Parser(Language(tscs.language()))
    with open(PATCH, encoding='utf-8') as fh:
        files = [line[len('+++ b/'):].strip() for line in fh if line.startswith('+++ b/')]
    for rel in files:
        if not rel.endswith('.cs'):
            continue
        path = os.path.join(tmp, rel)
        with open(path, 'rb') as fh:
            tree = parser.parse(fh.read())
        check(f'B  {rel} parses', not tree.root_node.has_error)
except ImportError:
    check('B  tree-sitter installed', False, 'pip install tree-sitter tree-sitter-c-sharp')

# C
with open(os.path.join(tmp, 'Controllers/ModelLiveChatController.cs'), encoding='utf-8') as fh:
    controller = fh.read()
check('C  GET live-chats route', '[HttpGet("live-chats")]' in controller)
check('C  POST {modelSlug}/live-chats route', '[HttpPost("{modelSlug}/live-chats")]' in controller)

print('\nALL CHECKS PASSED' if not failures else f'\n{len(failures)} CHECK(S) FAILED')
sys.exit(1 if failures else 0)
