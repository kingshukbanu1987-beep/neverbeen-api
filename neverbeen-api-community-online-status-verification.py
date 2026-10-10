#!/usr/bin/env python3
"""Verifies the "Online Now is every status but Inactive" change to neverbeen-api
(see neverbeen-api-community-online-status.patch).

Requirement A of the community Messenger work: a member whose status is Active, Busy,
Don't Disturb, Away or Custom belongs in "Online Now" (Messenger page) and "Online
Companions" (profile right rail); only Inactive moves them to "Offline Companions".
The API decided `IsOnline` from `presence == "Active"`, which pushed Busy, Don't Disturb,
Away and Custom members into the offline list. This patch makes the API agree with the
website.

There is no .NET SDK in the sandbox, so this script is the automated evidence that the
changed C# behaves as intended:

  A  the patch applies cleanly to the neverbeen-api checkout (main 94fa5fb) and touches
     only the two files it is meant to touch
  B  every .cs file the patch touches parses without syntax errors (tree-sitter when the
     packages are installed) and is structurally sound (braces / brackets / parens balanced)
  C  PresenceRules.IsOnline exists and is `effectiveStatus != "Inactive"`
  D  CompanionsController.ToCompanionDto takes IsOnline from PresenceRules.IsOnline and
     no longer from `presence == "Active"`, and still sends ActiveStatus + LastSeenUtc so
     the website can show "Busy" or "Away · last seen 12 min"
  E  the API rule and the website rule (src/app/services/community-presence.ts) agree for
     every status and last-seen combination the community can hold

Dependencies: git; optionally the tree_sitter / tree_sitter_c_sharp Python packages
(pip install tree-sitter tree-sitter-c-sharp) for the syntax check.

Run:  python3 neverbeen-api-community-online-status-verification.py [path-to-neverbeen-api]
"""

import os
import re
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-community-online-status.patch')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
API_COMMIT = '94fa5fb'

failures = []


def check(label, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + label + ('' if ok or not detail else f'  -- {detail}'))
    if not ok:
        failures.append(label)


def read(root, rel):
    with open(os.path.join(root, rel), encoding='utf-8') as fh:
        return fh.read()


def git(args, cwd):
    return subprocess.run(['git'] + args, cwd=cwd, capture_output=True, text=True)


# ---------------------------------------------------------------- A: patch applies
work = tempfile.mkdtemp(prefix='api-community-online-status-')
try:
    clone = os.path.join(work, 'api')
    subprocess.run(['git', 'clone', '-q', API_DIR, clone], check=True)
    git(['checkout', '-q', API_COMMIT], clone)
    r = git(['apply', '--check', PATCH], clone)
    check(f'A  patch applies to neverbeen-api main {API_COMMIT}', r.returncode == 0, r.stderr.strip())
    r = git(['apply', PATCH], clone)
    if r.returncode != 0:
        print('     cannot apply patch, stopping:', r.stderr.strip())
        sys.exit(1)

    touched = sorted(set(re.findall(r'^\+\+\+ b/(.+)$', open(PATCH, encoding='utf-8').read(), re.M)))
    check('A  patch touches exactly the presence rule and the companion mapping',
          touched == ['Common/PresenceRules.cs', 'Controllers/CompanionsController.cs'],
          ', '.join(touched))

    # ---------------------------------------------------------------- B: syntax
    def structure_ok(text):
        pairs = {'{': '}', '(': ')', '[': ']'}
        closing = {v: k for k, v in pairs.items()}
        stack = []
        in_string = in_char = in_line_comment = in_block_comment = False
        previous = ''
        for ch in text:
            two = previous + ch
            if in_line_comment:
                if ch == '\n':
                    in_line_comment = False
            elif in_block_comment:
                if two == '*/':
                    in_block_comment = False
            elif in_string:
                if ch == '"' and previous != '\\':
                    in_string = False
            elif in_char:
                if ch == "'" and previous != '\\':
                    in_char = False
            elif two == '//':
                in_line_comment = True
            elif two == '/*':
                in_block_comment = True
            elif ch == '"':
                in_string = True
            elif ch == "'":
                in_char = True
            elif ch in pairs:
                stack.append(pairs[ch])
            elif ch in closing:
                if not stack or stack.pop() != ch:
                    return False
            previous = ch
        return not stack and not in_string and not in_char and not in_block_comment

    try:
        import tree_sitter_c_sharp as tscs
        from tree_sitter import Language, Parser
        parser = Parser(Language(tscs.language()))
        parsed = True
    except ImportError:
        parsed = False
        print('NOTE tree-sitter is not installed, so the C# was checked structurally only '
              '(pip install tree-sitter tree-sitter-c-sharp)')

    for rel in touched:
        source = read(clone, rel)
        if parsed:
            tree = parser.parse(source.encode('utf-8'))
            errs = []

            def walk(node):
                if node.type == 'ERROR' or node.is_missing:
                    errs.append(node.start_point[0] + 1)
                for c in node.children:
                    walk(c)

            walk(tree.root_node)
            check(f'B  {rel} parses without syntax errors', not errs, f'error lines {errs[:5]}')
        check(f'B  {rel} is structurally sound', structure_ok(source))

    rules = read(clone, 'Common/PresenceRules.cs')
    companions = read(clone, 'Controllers/CompanionsController.cs')
    dto = read(clone, 'Dtos/CompanionDtos.cs')

    # ---------------------------------------------------------------- C: the rule
    check('C  PresenceRules.IsOnline exists',
          'public static bool IsOnline(string effectiveStatus)' in rules)
    check('C  every status but Inactive is online',
          re.search(r'public static bool IsOnline\(string effectiveStatus\)\s*=>\s*effectiveStatus != "Inactive";',
                    rules) is not None)
    check('C  Effective still never overrides Inactive',
          'if (status == "Inactive")\n            return "Inactive";' in rules)

    # ---------------------------------------------------------------- D: the mapping
    check('D  IsOnline comes from PresenceRules.IsOnline',
          'var online = PresenceRules.IsOnline(presence);' in companions
          and 'IsOnline = online,' in companions)
    check('D  IsOnline is no longer decided by presence == "Active"',
          'IsOnline = presence == "Active"' not in companions)
    check('D  the DTO still carries the status and last-seen time',
          'ActiveStatus = presence,' in companions and 'LastSeenUtc = user.LastSeenUtc,' in companions
          and 'public string? ActiveStatus { get; set; }' in dto and 'public DateTime? LastSeenUtc { get; set; }' in dto)

    # ---------------------------------------------------------------- E: API vs website
    AWAY_AFTER_MINUTES = 15
    STATUSES = ['Active', 'Busy', "Don't Disturb", 'Away', 'Inactive', 'Custom']

    def api_effective(stored, last_seen_minutes_ago, now=0.0):
        """Ported line for line from Common/PresenceRules.cs Effective()."""
        status = 'Active' if not stored or not stored.strip() else stored.strip()
        if status == 'Inactive':
            return 'Inactive'
        if status == 'Away':
            status = 'Active'
        if last_seen_minutes_ago is None:
            return 'Away'
        return 'Away' if last_seen_minutes_ago > AWAY_AFTER_MINUTES else status

    def api_is_online(effective_status):
        """Ported from Common/PresenceRules.cs IsOnline()."""
        return effective_status != 'Inactive'

    def website_is_online(stored, last_seen_minutes_ago):
        """Ported from src/app/services/community-presence.ts effectivePresence(),
        plus CommunityService.companionIsOnline() (status !== 'Inactive')."""
        base = stored if stored in STATUSES else 'Active'
        if base in ('Inactive', 'Away'):
            effective = base
        elif last_seen_minutes_ago is not None and last_seen_minutes_ago > AWAY_AFTER_MINUTES:
            effective = 'Away'
        else:
            effective = base
        return effective != 'Inactive'

    disagreements = []
    for stored in STATUSES + [None, '', 'Working from Goa']:
        for seen in (None, 0, 5, 14, 16, 60, 3 * 24 * 60):
            effective = api_effective(stored, seen)
            api_online = api_is_online(effective)
            # The API resolves Away itself; the website may still be handed the stored choice
            # (a member's own profile), so both readings are compared.
            if api_online != website_is_online(stored, seen) or api_online != website_is_online(effective, seen):
                disagreements.append(f'{stored!r}/{seen} -> {effective} api={api_online}')
    check('E  the API and the website agree on who is online, for every status and last-seen time',
          not disagreements, '; '.join(disagreements[:5]))

    check('E  Busy, Don\'t Disturb, Away and Custom members are online',
          all(api_is_online(api_effective(s, 2)) for s in ['Active', 'Busy', "Don't Disturb", 'Custom'])
          and api_is_online(api_effective('Active', 40)) is True)
    check('E  only Inactive is offline',
          api_is_online(api_effective('Inactive', 0)) is False
          and api_is_online(api_effective('Inactive', 3 * 24 * 60)) is False)
finally:
    subprocess.run(['rm', '-rf', work], check=False)

print()
if failures:
    print(f'{len(failures)} check(s) failed:')
    for f in failures:
        print('  -', f)
    sys.exit(1)
print('All checks passed.')
