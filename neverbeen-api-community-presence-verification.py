#!/usr/bin/env python3
"""Verifies the member-presence change to neverbeen-api
(see neverbeen-api-community-presence.patch).

Presence rules: signing in sets the status to Active; signing out sets Inactive; a member
who has not used the community for more than 15 minutes is shown as Away (automatic, never
stored as a choice). The website reports activity to POST /api/presence/heartbeat, and the
Web API keeps the last-seen time (Users.LastSeenUtc, added by neverbeen-database-community-presence).

There is no .NET SDK in this sandbox, so this script is the automated evidence that the
changed C# behaves as intended:

  A  the patch applies cleanly to the neverbeen-api checkout (main e68f5eb)
  B  every .cs file the patch touches parses without syntax errors (tree-sitter)
  C  PresenceRules: a Python port of Effective() and LastSeenFromReport(), line for line,
     checked against the rules (Inactive kept, Away after 15 minutes, Busy/Do-not-disturb/
     Custom turn Away, a stored Away reads as Active, never-seen reads as Away, a report
     in the future is cut back to now, last seen never moves backwards)
  D  PresenceController: heartbeat never changes the chosen status; sign-out sets Inactive
     and records the time; both require the member's JWT and are under api/presence
  E  sign-in: OauthLogin sets ActiveStatus to Active and LastSeenUtc before it saves
  F  viewers: GET /api/profile/{id} shows other members the effective status, while
     GET /api/profile/me keeps the member's own stored choice; a status change in
     PUT /api/profile counts as activity
  G  the presence fields reach the DTOs other members read: AuthorDto (posts, comments,
     notifications, conversations), CompanionDto (IsOnline and ActiveStatus from the
     effective status) and ProfileDto
  H  the new column is the one the database patch adds (Users.LastSeenUtc)

Dependencies: git and the tree_sitter / tree_sitter_c_sharp Python packages
(pip install tree-sitter tree-sitter-c-sharp).

Run:  python3 neverbeen-api-community-presence-verification.py [path-to-neverbeen-api]
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-community-presence.patch')
DB_PATCH = os.path.join(HERE, 'neverbeen-database-community-presence.patch')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
BASE = 'e68f5eb'

failures = []


def check(label, ok, detail=''):
    print(('PASS ' if ok else 'FAIL ') + label + ('' if ok or not detail else f'  -- {detail}'))
    if not ok:
        failures.append(label)


def read(root, rel):
    with open(os.path.join(root, rel), encoding='utf-8') as fh:
        return fh.read()


def body(src, signature):
    """The text of the method or class that starts at `signature` (up to its closing brace line)."""
    i = src.index(signature)
    j = src.index('\n    }\n', i) if '\n    }\n' in src[i:] else src.index('\n}\n', i)
    return src[i:j]


# ---------------------------------------------------------------- Python port of PresenceRules.cs
AWAY_AFTER = timedelta(minutes=15)


def effective(stored, last_seen, now):
    """Port of PresenceRules.Effective."""
    status = 'Active' if stored is None or stored.strip() == '' else stored.strip()
    if status == 'Inactive':
        return 'Inactive'
    if status == 'Away':
        status = 'Active'
    if last_seen is None:
        return 'Away'
    return 'Away' if now - last_seen > AWAY_AFTER else status


def last_seen_from_report(reported, recorded, now):
    """Port of PresenceRules.LastSeenFromReport."""
    at = reported if reported is not None else now
    if at > now:
        at = now
    if recorded is not None and at < recorded:
        at = recorded
    return at


# ---------------------------------------------------------------- A: patch applies
work = tempfile.mkdtemp(prefix='api-community-presence-')
try:
    clone = os.path.join(work, 'api')
    subprocess.run(['git', 'clone', '-q', API_DIR, clone], check=True)
    subprocess.run(['git', '-C', clone, 'checkout', '-q', BASE], check=True)
    r = subprocess.run(['git', '-C', clone, 'apply', '--check', PATCH], capture_output=True, text=True)
    check(f'A  patch applies to neverbeen-api main {BASE}', r.returncode == 0, r.stderr.strip())
    if r.returncode != 0:
        print('     cannot apply patch, stopping')
        sys.exit(1)
    subprocess.run(['git', '-C', clone, 'apply', PATCH], check=True)

    patch_text = open(PATCH, encoding='utf-8').read()
    touched = sorted(set(re.findall(r'^\+\+\+ b/(.+)$', patch_text, re.M)))
    cs_files = [p for p in touched if p.endswith('.cs')]
    check('   patch touches the expected C# files',
          set(cs_files) == {'Common/PresenceRules.cs', 'Controllers/AuthController.cs',
                            'Controllers/CompanionsController.cs', 'Controllers/PresenceController.cs',
                            'Controllers/ProfileController.cs', 'Dtos/CompanionDtos.cs',
                            'Dtos/JourneyDtos.cs', 'Dtos/Mappers.cs', 'Dtos/PresenceDtos.cs',
                            'Dtos/ProfileDtos.cs', 'Entities/UserProfile.cs'},
          ', '.join(cs_files))

    # ------------------------------------------------------------ B: syntax
    try:
        import tree_sitter_c_sharp as tscs
        from tree_sitter import Language, Parser
        parser = Parser(Language(tscs.language()))
        for rel in cs_files:
            tree = parser.parse(read(clone, rel).encode('utf-8'))
            errs = []

            def walk(node):
                if node.type == 'ERROR' or node.is_missing:
                    errs.append(node.start_point[0] + 1)
                for c in node.children:
                    walk(c)

            walk(tree.root_node)
            check(f'B  {rel} parses without syntax errors', not errs, f'error lines {errs[:5]}')
    except ImportError:
        check('B  tree-sitter available', False, 'pip install tree-sitter tree-sitter-c-sharp')

    # ------------------------------------------------------------ C: rules
    now = datetime(2026, 10, 10, 12, 0, tzinfo=timezone.utc)
    ago = lambda minutes: now - timedelta(minutes=minutes)  # noqa: E731
    cases = [
        ('C  Active used 5 min ago stays Active', effective('Active', ago(5), now), 'Active'),
        ('C  Active used 14 min ago stays Active', effective('Active', ago(14), now), 'Active'),
        ('C  Active unused for 16 min is Away', effective('Active', ago(16), now), 'Away'),
        ('C  Busy unused for 30 min is Away (chosen status returns on use)', effective('Busy', ago(30), now), 'Away'),
        ("C  Don't Disturb unused for 30 min is Away", effective("Don't Disturb", ago(30), now), 'Away'),
        ('C  Custom unused for 30 min is Away', effective('Custom', ago(30), now), 'Away'),
        ('C  Busy used 2 min ago is still Busy', effective('Busy', ago(2), now), 'Busy'),
        ('C  Inactive is never overridden, even just seen', effective('Inactive', ago(1), now), 'Inactive'),
        ('C  stored Away reads as Active when recently used', effective('Away', ago(1), now), 'Active'),
        ('C  stored Away unused reads as Away', effective('Away', ago(60), now), 'Away'),
        ('C  never recorded reads as Away', effective('Active', None, now), 'Away'),
        ('C  never recorded and Inactive stays Inactive', effective('Inactive', None, now), 'Inactive'),
        ('C  blank stored status reads as Active', effective('  ', ago(1), now), 'Active'),
    ]
    for label, got, want in cases:
        check(label, got == want, f'got {got!r}')

    report_cases = [
        ('C  no report records now', last_seen_from_report(None, None, now), now),
        ('C  a report 3 min ago is kept', last_seen_from_report(ago(3), None, now), ago(3)),
        ('C  a report in the future is cut back to now', last_seen_from_report(now + timedelta(minutes=5), None, now), now),
        ('C  an older report never moves last seen backwards',
         last_seen_from_report(ago(20), ago(4), now), ago(4)),
        ('C  a newer report moves last seen forward', last_seen_from_report(ago(1), ago(4), now), ago(1)),
    ]
    for label, got, want in report_cases:
        check(label, got == want, f'got {got!r}')

    # Away after 15 minutes is measured from the last recorded use, so a heartbeat at 14:30 keeps the member Active.
    beat_then_idle = last_seen_from_report(ago(14), None, now)
    check('C  heartbeat 14 min ago keeps the member Active',
          effective('Active', beat_then_idle, now) == 'Active')

    # ------------------------------------------------------------ D: presence controller
    pc = read(clone, 'Controllers/PresenceController.cs')
    check('D  routes are under api/presence and require the member\'s JWT',
          '[Route("api/presence")]' in pc and '[Authorize]' in pc)
    check('D  heartbeat endpoint is POST api/presence/heartbeat',
          '[HttpPost("heartbeat")]' in pc and 'Heartbeat(' in pc)
    check('D  sign-out endpoint is POST api/presence/sign-out', '[HttpPost("sign-out")]' in pc)
    heartbeat = body(pc, 'public async Task<ActionResult<PresenceDto>> Heartbeat(')
    check('D  heartbeat records last use with the monotonic rule',
          'PresenceRules.LastSeenFromReport(request?.LastActivityUtc, user.LastSeenUtc, DateTime.UtcNow)' in heartbeat)
    check('D  heartbeat never changes the chosen status (it only reads user.ActiveStatus)',
          re.search(r'user\.ActiveStatus\s*=[^=]', heartbeat) is None)
    sign_out = body(pc, 'public async Task<ActionResult<PresenceDto>> SignOut(')
    check('D  sign-out makes the member Inactive and records the time',
          'user.ActiveStatus = "Inactive";' in sign_out and 'user.LastSeenUtc = DateTime.UtcNow;' in sign_out)
    check('D  both endpoints return 404 for an unknown member', sign_out.count('return NotFound();') == 1
          and heartbeat.count('return NotFound();') == 1)

    # ------------------------------------------------------------ E: sign-in
    auth = read(clone, 'Controllers/AuthController.cs')
    oauth = body(auth, 'public async Task<ActionResult<AuthResultDto>> OauthLogin(')
    i_set = oauth.index('user.ActiveStatus = "Active";')
    i_save = oauth.index('await _db.SaveChangesAsync(cancellationToken);')
    check('E  OauthLogin sets ActiveStatus to Active before it saves', i_set < i_save)
    check('E  OauthLogin records the sign-in as last use', 'user.LastSeenUtc = DateTime.UtcNow;' in oauth)

    # ------------------------------------------------------------ F: viewers
    prof = read(clone, 'Controllers/ProfileController.cs')
    get = body(prof, 'public async Task<ActionResult<ProfileDto>> Get(')
    check('F  other members see the effective status',
          'if (requesterId != id)' in get and 'dto.ActiveStatus = PresenceRules.Effective(user.ActiveStatus, user.LastSeenUtc, DateTime.UtcNow);' in get)
    me = body(prof, 'public async Task<ActionResult<ProfileDto>> Me(')
    check('F  GET /api/profile/me keeps the stored choice (no presence override)', 'PresenceRules' not in me)
    upd = body(prof, 'public async Task<ActionResult<ProfileDto>> Update(')
    check('F  a status change in PUT /api/profile counts as activity',
          re.search(r'if \(request\.ActiveStatus != null\)\s*\{[^}]*user\.LastSeenUtc = DateTime\.UtcNow;', upd, re.S) is not None)

    # ------------------------------------------------------------ G: DTOs
    jd = read(clone, 'Dtos/JourneyDtos.cs')
    author = jd[jd.index('public class AuthorDto'):]
    author = author[:author.index('\n}')]
    check('G  AuthorDto has ActiveStatus, CustomStatusText and LastSeenUtc',
          'public string? ActiveStatus' in author and 'public string? CustomStatusText' in author
          and 'public DateTime? LastSeenUtc' in author)
    mappers = read(clone, 'Dtos/Mappers.cs')
    am = mappers[mappers.index('public static class AuthorMapper'):]
    am = am[:am.index('\n}')]
    check('G  AuthorMapper sets presence as other members see it',
          'ActiveStatus = PresenceRules.Effective(u.ActiveStatus, u.LastSeenUtc, DateTime.UtcNow)' in am
          and 'LastSeenUtc = u.LastSeenUtc' in am and 'CustomStatusText = u.CustomStatusText' in am)
    check('G  ProfileMapper carries LastSeenUtc', 'LastSeenUtc = user.LastSeenUtc,' in mappers)
    check('G  ProfileDto has LastSeenUtc', 'public DateTime? LastSeenUtc' in read(clone, 'Dtos/ProfileDtos.cs'))
    cd = read(clone, 'Dtos/CompanionDtos.cs')
    check('G  CompanionDto has LastSeenUtc', 'public DateTime? LastSeenUtc' in cd)
    comp = read(clone, 'Controllers/CompanionsController.cs')
    check('G  companions: IsOnline and ActiveStatus use the effective status',
          'var presence = PresenceRules.Effective(user.ActiveStatus, user.LastSeenUtc, DateTime.UtcNow);' in comp
          and 'IsOnline = presence == "Active",' in comp and 'ActiveStatus = presence,' in comp
          and 'LastSeenUtc = user.LastSeenUtc,' in comp)

    # ------------------------------------------------------------ H: column alignment
    entity = read(clone, 'Entities/UserProfile.cs')
    check('H  UserProfile.LastSeenUtc is a nullable DateTime (timestamptz)',
          'public DateTime? LastSeenUtc { get; set; }' in entity)
    db_patch = open(DB_PATCH, encoding='utf-8').read() if os.path.exists(DB_PATCH) else ''
    check('H  the database patch adds Users.LastSeenUtc',
          '"LastSeenUtc"               timestamptz' in db_patch or '"LastSeenUtc" timestamptz' in db_patch
          or '"LastSeenUtc"' in db_patch, 'neverbeen-database-community-presence.patch not found' if not db_patch else '')

    # ------------------------------------------------------------ I: nothing stores Away
    new_code = '\n'.join(read(clone, p) for p in cs_files)
    check('I  the API never stores Away as a chosen status', 'ActiveStatus = "Away"' not in new_code)
finally:
    shutil.rmtree(work, ignore_errors=True)

print()
if failures:
    print(f'{len(failures)} check(s) failed')
    sys.exit(1)
print('All checks passed')
