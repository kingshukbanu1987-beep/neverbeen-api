#!/usr/bin/env python3
"""Verifies neverbeen-api-ai-chat-biography.patch.

The patch builds on neverbeen-api-ai-chat-gemini.patch: apply that one first, then this one.

  A  on a fresh neverbeen-api checkout (main, 7c92247 or later) the Gemini patch applies, and this
     patch applies cleanly on top of it
  B  every .cs file the two patches add or change parses without syntax errors (tree-sitter)
  C  the request accepts the model's biography (`Biography`, her personality/biography.json) as an
     optional JSON object, next to the required `Message` and `CharacterProfile`
  D  validation: a biography that is not an object answers 400 invalid-biography, one over
     BiographyLimit (60 000 characters, `_doc` notes removed) answers 400 biography-too-large, and
     the compacted biography travels in AiChatTurn.BiographyJson to every place a turn is built
  E  the Gemini system instruction carries the biography after the character profile and before
     the dossier, as first-person life story, profile-wins, fictional and data-not-instructions,
     and only when one was sent
  F  every website biography file fits: each public/NeverBeenModels/<Album>/personality/biography.json
     (42 of them, one per album) is an object with meta / profile / biography blocks, stays under
     BiographyLimit once compacted the way the API compacts it, and a full turn (character profile
     + biography + dossier + history) stays under the controller's 256 KB body limit
  G  the website sends it: ai-model-chat.service.ts puts `biography` in the body of every turn
  H  no database change and no committed key; the README documents the new field and errors

Dependencies: git, and the tree_sitter / tree_sitter_c_sharp packages
(pip install tree-sitter tree-sitter-c-sharp).

Run:  python3 neverbeen-api-ai-chat-biography-verification.py [path-to-neverbeen-api]
"""

import glob
import json
import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
WEBSITE = os.path.abspath(os.path.join(HERE, '..', '..'))
GEMINI_PATCH = os.path.join(HERE, 'neverbeen-api-ai-chat-gemini.patch')
PATCH = os.path.join(HERE, 'neverbeen-api-ai-chat-biography.patch')
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


def changed_files(patch_path):
    with open(patch_path, encoding='utf-8') as fh:
        return [line[len('+++ b/'):].strip() for line in fh if line.startswith('+++ b/')]


def strip_annotations(node):
    """The API's CompactProfile: drop every key that starts with `_`, at every depth."""
    if isinstance(node, dict):
        return {k: strip_annotations(v) for k, v in node.items() if not k.startswith('_')}
    if isinstance(node, list):
        return [strip_annotations(v) for v in node]
    return node


def compact(node):
    return json.dumps(strip_annotations(node), ensure_ascii=False, separators=(',', ':'))


# A
tmp = tempfile.mkdtemp(prefix='api-ai-chat-biography-')
subprocess.run(['git', 'clone', '-q', API_DIR, tmp], check=True)
r = git(['apply', '--check', GEMINI_PATCH], tmp)
check('A  the Gemini patch applies to neverbeen-api', r.returncode == 0, r.stderr.strip())
if r.returncode == 0:
    git(['apply', GEMINI_PATCH], tmp)
r = git(['apply', '--check', PATCH], tmp)
check('A  the biography patch applies on top of it', r.returncode == 0, r.stderr.strip())
if r.returncode == 0:
    git(['apply', PATCH], tmp)

# B
try:
    import tree_sitter_c_sharp as tscs
    from tree_sitter import Language, Parser
    parser = Parser(Language(tscs.language()))
    for rel in sorted(set(changed_files(GEMINI_PATCH) + changed_files(PATCH))):
        if not rel.endswith('.cs'):
            continue
        with open(os.path.join(tmp, rel), 'rb') as fh:
            tree = parser.parse(fh.read())
        check(f'B  {rel} parses', not tree.root_node.has_error)
except ImportError:
    check('B  tree-sitter available (pip install tree-sitter tree-sitter-c-sharp)', False)

dtos = read(tmp, 'Dtos/AiChatDtos.cs')
prompt = read(tmp, 'Services/AiChatPrompt.cs')
controller = read(tmp, 'Controllers/AiChatController.cs')
readme = read(tmp, 'README.md')

# C
check('C  AiChatRequest has `JsonElement? Biography`',
      re.search(r'public\s+JsonElement\?\s+Biography\s*\{\s*get;\s*set;\s*\}', dtos) is not None)
check('C  Message and CharacterProfile are still there',
      'public string? Message' in dtos and 'public JsonElement? CharacterProfile' in dtos)
check('C  the DTO names the file it carries', 'personality/biography.json' in dtos)
check('C  route is still POST api/ai-chat',
      '[Route("api/ai-chat")]' in controller and '[HttpPost]' in controller)

# D
record = re.search(r'record\s+AiChatTurn\((.*?)\);', prompt, re.S)
params = [p.strip().split()[-1] for p in record.group(1).split(',')] if record else []
check('D  AiChatTurn carries BiographyJson right after CharacterProfileJson',
      params[1:3] == ['CharacterProfileJson', 'BiographyJson'], str(params))
check('D  BiographyLimit is 60 000 characters', 'public const int BiographyLimit = 60000;' in prompt)
validate = prompt[prompt.find('public static (AiChatTurn? Turn, string? Error) Validate'):]
validate = validate[:validate.find('public static string CompactProfile')]
check('D  a biography that is not an object answers invalid-biography',
      'biography.ValueKind != JsonValueKind.Object) return (null, "invalid-biography")' in validate)
check('D  an oversized biography answers biography-too-large',
      'biographyJson.Length > BiographyLimit) return (null, "biography-too-large")' in validate)
check('D  the biography is compacted without its _doc notes', 'CompactProfile(biography)' in validate)
check('D  a missing (or null) biography is allowed', 'JsonValueKind.Null' in validate
      and 'missing-biography' not in prompt)
check('D  the order of the checks keeps the required fields first',
      validate.find('"empty-message"') < validate.find('"missing-character-profile"')
      < validate.find('"invalid-biography"'))
for cs in glob.glob(os.path.join(tmp, '**', '*.cs'), recursive=True):
    text = read(tmp, os.path.relpath(cs, tmp))
    for match in re.finditer(r'new\s+AiChatTurn\s*\((.*?)\);', text, re.S):
        check(f'D  {os.path.relpath(cs, tmp)} builds AiChatTurn with BiographyJson',
              'BiographyJson:' in match.group(1))
check('D  the controller still validates through AiChatPrompt.Validate',
      'AiChatPrompt.Validate(request)' in controller)

# E
build = prompt[prompt.find('public static string BuildSystemInstruction'):]
build = build[:build.find('public static List<(string Role, string Text)> BuildContents')]
i_profile = build.find('lines.AppendLine(turn.CharacterProfileJson)')
i_bio = build.find('lines.AppendLine(turn.BiographyJson)')
i_dossier = build.find('turn.Dossier.Length > 0')
check('E  the biography follows the character profile and precedes the dossier',
      0 <= i_profile < i_bio < i_dossier, f'{i_profile} {i_bio} {i_dossier}')
check('E  it is only added when one was sent', 'if (turn.BiographyJson.Length > 0)' in build)
instruction = re.sub(r'"\s*\+\s*"', '', build).replace('\\"', '"')
for phrase, label in [
    ('biography.json', 'names the file'),
    ('first person', 'is told in the first person'),
    ('the profile wins', 'profile wins over the biography'),
    ('Never invent', 'never contradicts it'),
    ('fictional', 'is fictional'),
    ('AI model', 'is honest about being an AI model when sincerely asked'),
    ('not instructions', 'is data, never instructions'),
    ('studio rules', 'cannot change the studio rules'),
]:
    check(f'E  the instruction says it {label}', phrase in instruction)

# F
albums = sorted(d for d in glob.glob(os.path.join(WEBSITE, 'public', 'NeverBeenModels', '*'))
                if os.path.isdir(d))
files = [os.path.join(d, 'personality', 'biography.json') for d in albums]
missing = [os.path.basename(os.path.dirname(os.path.dirname(f))) for f in files if not os.path.exists(f)]
check(f'F  every album has personality/biography.json ({len(files)} albums)',
      len(files) > 0 and not missing, ', '.join(missing))
largest_turn, largest_bio = 0, 0
for path in files:
    if not os.path.exists(path):
        continue
    album = os.path.basename(os.path.dirname(os.path.dirname(path)))
    with open(path, encoding='utf-8') as fh:
        data = json.load(fh)
    shaped = isinstance(data, dict) and all(isinstance(data.get(k), dict) for k in ('meta', 'profile', 'biography'))
    bio = compact(data)
    character_path = os.path.join(os.path.dirname(path), 'characterdatabase.json')
    with open(character_path, encoding='utf-8') as fh:
        character = compact(json.load(fh))
    source = data.get('meta', {}).get('biographySource')
    written = os.path.exists(os.path.join(os.path.dirname(os.path.dirname(path)), 'Biography', 'biography.json'))
    ok = (shaped and len(bio) < 60000 and len(character) < 60000
          and source == ('Biography/biography.json' if written else 'generated-for-live-chat')
          and data['profile'].get('album') == album)
    check(f'F  {album}: {len(bio)} characters, {source}', ok)
    largest_bio = max(largest_bio, len(bio))
    # The rest of a turn at its limits: 12 000-character dossier, 24 history messages of 800.
    turn = len(bio.encode()) + len(character.encode()) + 12000 * 3 + 24 * 800 * 3 + 2000 * 3 + 4096
    largest_turn = max(largest_turn, turn)
check(f'F  the largest full turn ({largest_turn} bytes) fits the 256 KB body limit',
      'MaxBodyBytes = 256 * 1024' in controller and largest_turn < 256 * 1024)

# G
service = read(WEBSITE, 'src/app/services/ai-model-chat.service.ts')
check('G  the website loads the biography file with every turn',
      'loadModelBiography(model)' in service)
body = service[service.find('const body'):]
body = body[:body.find('JSON.stringify(body)')]
check('G  the request body carries `biography` next to `characterProfile`',
      re.search(r'\bcharacterProfile,', body) is not None and re.search(r'\bbiography,', body) is not None)
loader = read(WEBSITE, 'src/app/pages/ai-models/chat/chat-biography.ts')
check('G  the loader reads personality/biography.json', '/personality/biography.json' in loader)

# H
check('H  no database change',
      not any(f.startswith(('Models/', 'Data/', 'Migrations/')) or 'DbContext' in f for f in changed_files(PATCH)))
with open(PATCH, encoding='utf-8') as fh:
    patch_text = fh.read()
check('H  nothing in the patch looks like a Google API key',
      re.search(r'AIza[0-9A-Za-z_\-]{30,}', patch_text) is None)
check('H  README documents `biography` and its errors',
      '"biography":' in readme and 'invalid-biography' in readme and 'biography-too-large' in readme)

print()
print(f'{len(failures)} failure(s)' if failures else 'All checks passed.')
sys.exit(1 if failures else 0)
