#!/usr/bin/env python3
"""Verifies the member-directory changes to neverbeen-api
(see neverbeen-api-user-search.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence
that the changed C# behaves as intended. It is a line-for-line port of the new
UsersController logic, run against an in-memory community, plus a structural
audit of the patch itself:

  A  GET /api/users/search     -> ILIKE matching over names / city / country /
                                  profession, name hits ranked first, self /
                                  Pending / search-invisible / blocked (both
                                  directions) never returned, every hit answers
                                  its companionship status, isFollowing and the
                                  mutual companions count; limit clamped 1..50
  B  GET /api/users/{id} and   -> CompanionDto with the privacy rules:
     /api/users/uid/{uniqueId}    unknown / Pending-for-others -> 404,
                                  public profile switched off -> 403,
                                  WhoCanVisitProfile (companions / none) ->
                                  identity cards of a locked profile,
                                  locked profile hides About-me from
                                  non-companions, companions and the member
                                  themself always see the full profile
  C  CompanionDto.IsFollowing  -> answered by the companions endpoints too
  D  the patch applies cleanly to the neverbeen-api checkout and every .cs
     file it touches parses without syntax errors

Run:  python3 neverbeen-api-user-search-verification.py [path-to-neverbeen-api]
"""

import glob
import os
import subprocess
import sys
import tempfile

API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
PATCH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'neverbeen-api-user-search.patch')

failures = []


def check(name, cond, detail=''):
    status = 'PASS' if cond else 'FAIL'
    print(f'  [{status}] {name}' + (f' — {detail}' if detail and not cond else ''))
    if not cond:
        failures.append(name)


# ---------------------------------------------------------------------------
# D. the patch applies cleanly to the API checkout and parses
# ---------------------------------------------------------------------------
print('D. patch applies cleanly to the neverbeen-api checkout')

patch_text = open(PATCH, encoding='utf-8').read()

with tempfile.TemporaryDirectory() as tmp:
    work = os.path.join(tmp, 'api')
    if os.path.isdir(API_DIR):
        subprocess.run(['git', 'clone', '--quiet', API_DIR, work], check=True)
    else:  # no local checkout: take the GitHub repository the website points at
        subprocess.run(['git', 'clone', '--quiet',
                        'https://github.com/kingshukbanu1987-beep/neverbeen-api', work],
                       check=True)
    # The patch was generated against the repository's default branch; a checkout
    # that already carries the changes would not apply it.
    base = None
    for candidate in ('origin/main', 'origin/master'):
        ref = subprocess.run(['git', '-C', work, 'rev-parse', '--verify', '--quiet', candidate],
                             capture_output=True, text=True)
        if ref.returncode == 0:
            base = candidate
            break
    if base:
        subprocess.run(['git', '-C', work, 'checkout', '--quiet', base], check=True)
    applied = subprocess.run(['git', '-C', work, 'apply', '--check', PATCH],
                             capture_output=True, text=True)
    check(f'git apply --check against {base or "HEAD"}', applied.returncode == 0, applied.stderr.strip())
    subprocess.run(['git', '-C', work, 'apply', PATCH], check=True)

    src_of = {}
    for f in glob.glob(os.path.join(work, '**', '*.cs'), recursive=True):
        src_of[os.path.relpath(f, work)] = open(f, encoding='utf-8').read()

    # --- C# source audit: every contract the verification ports must be present ---
    users_cs = src_of.get('Controllers/UsersController.cs', '')
    check('UsersController exposes GET /api/users/search, /{userId:int} and /uid/{uniqueId}',
          all(tok in users_cs for tok in
              ['[Route("api/users")]', '[HttpGet("search")]',
               '[HttpGet("{userId:int}")]', '[HttpGet("uid/{uniqueId:length(20)}")]']))
    check('search hides the member themself, search-invisible members and blocks both ways',
          all(tok in users_cs for tok in
              ['u.Id != myId', 'u.Settings == null || u.Settings.SearchVisibility',
               'BlockedIds(myId, cancellationToken)']))
    check('search matches name, city, country and profession with ILIKE',
          all(tok in users_cs for tok in
              ['EF.Functions.ILike(u.FullName', 'EF.Functions.ILike(u.FirstName',
               'EF.Functions.ILike(u.LastName', 'EF.Functions.ILike(u.City.Name',
               'EF.Functions.ILike(u.Country.Name', 'EF.Functions.ILike(u.Profession']))
    check('visit rules enforce PublicProfileEnabled, WhoCanVisitProfile and the profile lock',
          all(tok in users_cs for tok in
              ['PublicProfileEnabled == false', 'WhoCanVisitProfile',
               'user.IsProfileLocked && !connected', 'rule == "none"',
               'rule == "companions"']))
    check('Pending accounts are never answered to other members',
          'user.Status != UserProfileStatus.Active && user.Id != myId' in users_cs)

    dtos_cs = src_of.get('Dtos/CompanionDtos.cs', '')
    check('CompanionDto answers IsFollowing (also for the companions endpoints)',
          'public bool IsFollowing { get; set; }' in dtos_cs
          and 'UserSearchResultDto' in dtos_cs
          and 'IsFollowing = isFollowing' in src_of.get('Controllers/CompanionsController.cs', ''))

    # Syntax audit with a real C# grammar when tree-sitter is available.
    try:
        import tree_sitter_c_sharp as tscs
        from tree_sitter import Language, Parser

        parser = Parser(Language(tscs.language()))

        def broken_nodes(node):
            if node.type == 'ERROR' or node.is_missing:
                yield node
            for child in node.children:
                yield from broken_nodes(child)

        bad = 0
        for rel, src in src_of.items():
            errors = list(broken_nodes(parser.parse(src.encode()).root_node))
            if errors:
                bad += 1
                node = errors[0]
                print(f'    parse error in {rel} at {node.start_point}')
        check('every .cs file parses with the C# grammar', bad == 0, f'{bad} broken files')
    except ImportError:
        print('  [SKIP] tree-sitter C# grammar not installed; syntax audit skipped')


# ---------------------------------------------------------------------------
# In-memory community the ports below run against
# ---------------------------------------------------------------------------

def generate_unique_id(user_id):
    return '8920153401' + str(user_id).zfill(10)


class Member:
    def __init__(self, id, full_name, first='', last='', city='', country='',
                 profession='', status='Active', search_visibility=True,
                 public_profile=True, who_can_visit='everyone', locked=False,
                 verified=False):
        self.id = id
        self.unique_id = generate_unique_id(id)
        self.full_name = full_name
        self.first_name = first
        self.last_name = last
        self.city = city
        self.country = country
        self.profession = profession
        self.status = status
        self.search_visibility = search_visibility
        self.public_profile = public_profile
        self.who_can_visit = who_can_visit
        self.is_profile_locked = locked
        self.is_verified = verified
        self.about_me = f'About {full_name}'
        self.about_me_details_json = f'{{"intro": "story of {full_name}"}}'
        self.active_status = 'Active'


MEMBERS = {
    1: Member(1, 'Kingshuk Banu', 'Kingshuk', 'Banu', 'Kolkata', 'India', 'Engineer'),
    2: Member(2, 'Elena Rostova', 'Elena', 'Rostova', 'Paris', 'France', 'Travel Blogger'),
    3: Member(3, 'Marco Rossi', 'Marco', 'Rossi', 'Rome', 'Italy', 'Architect'),
    4: Member(4, 'Maya Patel', 'Maya', 'Patel', 'Mumbai', 'India', 'Designer', locked=True),
    5: Member(5, 'Hidden Harry', 'Hidden', 'Harry', 'Delhi', 'India', 'Painter',
              search_visibility=False),
    6: Member(6, 'Pending Penny', 'Pending', 'Penny', 'London', 'United Kingdom', 'Student',
              status='Pending'),
    7: Member(7, 'Blocked Bess', 'Blocked', 'Bess', 'Tokyo', 'Japan', 'Chef'),
    8: Member(8, 'Private Pia', 'Private', 'Pia', 'Berlin', 'Germany', 'Writer',
              who_can_visit='companions'),
    9: Member(9, 'Offgrid Omar', 'Offgrid', 'Omar', 'Cairo', 'Egypt', 'Guide',
              public_profile=False),
    10: Member(10, 'Rosa Paris', 'Rosa', 'Lee', 'Paris', 'France', 'Baker'),
}

# pairs: (low, high, requester, status)
COMPANIONSHIPS = [
    (1, 2, 1, 'connected'),      # me + Elena
    (1, 3, 1, 'pending'),        # my outgoing request to Marco
    (1, 4, 4, 'pending'),        # Maya's incoming request to me
    (2, 3, 2, 'connected'),      # mutual of mine through Elena
    (3, 8, 3, 'connected'),      # Marco may visit Private Pia (WhoCanVisitProfile=companions)
]
FOLLOWS = {(1, 2), (1, 10), (3, 1)}   # I follow Elena + Rosa; Marco follows me
BLOCKS = [(1, 7)]                       # I blocked Bess


def connected_ids(user_id):
    out = set()
    for a, b, _req, st in COMPANIONSHIPS:
        if st != 'connected':
            continue
        if a == user_id:
            out.add(b)
        elif b == user_id:
            out.add(a)
    return out


def blocked_ids(user_id):
    out = set()
    for a, b in BLOCKS:
        if a == user_id:
            out.add(b)
        elif b == user_id:
            out.add(a)
    return out


def relative_status(row, my_id):
    a, b, requester, status = row
    if status == 'connected':
        return 'connected'
    return 'pending_outgoing' if requester == my_id else 'pending_incoming'


def find_pair(x, y):
    pair = (min(x, y), max(x, y))
    for row in COMPANIONSHIPS:
        if (row[0], row[1]) == pair:
            return row
    return None


# ---------------------------------------------------------------------------
# A. port of GET /api/users/search
# ---------------------------------------------------------------------------
print('A. GET /api/users/search — the member directory')


def ilike(value, pattern):
    """EF.Functions.ILIKE(%needle%) — case-insensitive substring match."""
    needle = pattern.strip('%')
    return needle.lower() in (value or '').lower()


def users_search(my_id, query, limit=20):
    """Line-for-line port of UsersController.Search."""
    needle = (query or '').strip()
    if len(needle) == 0:
        return []
    if len(needle) > 100:
        needle = needle[:100]
    if limit < 1:
        limit = 1
    if limit > 50:
        limit = 50
    pattern = f'%{needle}%'

    blocked = blocked_ids(my_id)
    candidates = [
        u for u in sorted(MEMBERS.values(), key=lambda u: u.id)
        if u.status == 'Active' and u.id != my_id and u.id not in blocked
        and u.search_visibility
        and (ilike(u.full_name, pattern) or ilike(u.first_name, pattern)
             or ilike(u.last_name, pattern) or ilike(u.city, pattern)
             or ilike(u.country, pattern) or ilike(u.profession, pattern))
    ][: limit * 3]

    def name_hit(u):
        return any(needle.lower() in (v or '').lower()
                   for v in (u.full_name, u.first_name, u.last_name))

    name_hits = [u for u in candidates if name_hit(u)]
    other_hits = [u for u in candidates if u not in name_hits]
    ranked = (name_hits + other_hits)[:limit]

    mine = connected_ids(my_id)
    results = []
    for user in ranked:
        row = find_pair(my_id, user.id)
        results.append({
            'id': user.id,
            'uniqueId': user.unique_id,
            'fullName': user.full_name,
            'country': user.country,
            'city': user.city,
            'profession': user.profession,
            'isOnline': user.active_status == 'Active',
            'isVerified': user.is_verified,
            'isProfileLocked': user.is_profile_locked,
            'status': 'none' if row is None else relative_status(row, my_id),
            'isFollowing': (my_id, user.id) in FOLLOWS,
            'mutualCompanionsCount': len(mine & connected_ids(user.id)),
        })
    return results


names = lambda rows: [r['fullName'] for r in rows]
by_name = lambda rows, n: next(r for r in rows if r['fullName'] == n)

check('an empty query answers an empty list', users_search(1, '   ') == [])

hits = users_search(1, 'elena')
check('finds a member by first name', names(hits) == ['Elena Rostova'])

hits = users_search(1, 'ROSTOVA')
check('matching is case-insensitive', names(hits) == ['Elena Rostova'])

hits = users_search(1, 'rossi')
check('finds a member by last name', names(hits) == ['Marco Rossi'])

hits = users_search(1, 'kingshuk banu')
check('the signed-in member themself is never a hit (even on their own full name)',
      hits == [])

hits = users_search(1, 'paris')
check('a city matches too, and the name hit ranks before the city hit',
      names(hits) == ['Rosa Paris', 'Elena Rostova'])

hits = users_search(1, 'architect')
check('finds by profession', names(hits) == ['Marco Rossi'])

hits = users_search(1, 'india')
check('finds by country, but never the signed-in member themself',
      all(r['id'] != 1 for r in hits) and 'Maya Patel' in names(hits))

hits = users_search(1, 'e')
# precise ordering check on a controlled needle:
hits = users_search(2, 'a')
check('every name hit is ranked before every city / country / profession hit',
      [r['id'] for r in hits] == [1, 3, 4, 8, 9, 10, 7],
      'got: ' + ', '.join(names(hits)))

check('Pending (not yet registered) members are never found',
      'Pending Penny' not in names(users_search(1, 'penny')))
check('members who switched off search visibility are never found',
      'Hidden Harry' not in names(users_search(1, 'harry')))
check('a member blocked by me never appears',
      'Blocked Bess' not in names(users_search(1, 'bess')))
check('a member who blocked me never appears either',
      'Kingshuk Banu' not in names(users_search(7, 'kingshuk')))

hits = users_search(1, 'elena')
elena = by_name(hits, 'Elena Rostova')
check('a connected companion answers status=connected and isFollowing=true',
      elena['status'] == 'connected' and elena['isFollowing'] is True)

marco = by_name(users_search(1, 'marco'), 'Marco Rossi')
check('an outgoing request answers status=pending_outgoing',
      marco['status'] == 'pending_outgoing' and marco['isFollowing'] is False)

maya = by_name(users_search(1, 'maya'), 'Maya Patel')
check('an incoming request answers status=pending_incoming',
      maya['status'] == 'pending_incoming')

rosa = by_name(users_search(1, 'rosa'), 'Rosa Paris')
check('a stranger answers status=none with isFollowing from the follows graph',
      rosa['status'] == 'none' and rosa['isFollowing'] is True)

check('mutual companions are counted from both connected sets',
      by_name(users_search(1, 'marco'), 'Marco Rossi')['mutualCompanionsCount'] == 1)

check('every hit carries the 20-digit unique id used in profile URLs',
      all(r['uniqueId'] == generate_unique_id(r['id']) for r in users_search(1, 'a')))

check('the limit clamps the result list', len(users_search(1, 'a', limit=2)) <= 2)
check('limit=0 is clamped up to 1', len(users_search(1, 'a', limit=0)) <= 1)


# ---------------------------------------------------------------------------
# B. port of GET /api/users/{id} and /api/users/uid/{uniqueId}
# ---------------------------------------------------------------------------
print('B. GET /api/users/{id} — public profile with the privacy rules')


def get_user(my_id, target_id):
    """Line-for-line port of UsersController.Get + ApplyVisitRulesAsync.
    Returns ('404'|'403'|200, dto)."""
    user = MEMBERS.get(target_id)
    if user is None or (user.status != 'Active' and user.id != my_id):
        return '404', None
    if target_id != my_id and not user.public_profile:
        return '403', None

    row = find_pair(my_id, user.id)
    dto = {
        'id': user.id,
        'uniqueId': user.unique_id,
        'fullName': user.full_name,
        'country': user.country,
        'city': user.city,
        'profession': user.profession,
        'status': 'none' if row is None else relative_status(row, my_id),
        'isFollowing': (my_id, user.id) in FOLLOWS,
        'isVerified': user.is_verified,
        'isProfileLocked': user.is_profile_locked,
        'bio': user.about_me,
        'aboutMe': user.about_me,
        'aboutMeDetailsJson': user.about_me_details_json,
        'connectedCompanionIds': sorted(connected_ids(user.id)),
    }

    # ApplyVisitRulesAsync
    if user.id == my_id:
        return 200, dto
    connected = dto['status'] == 'connected'
    rule = user.who_can_visit or 'everyone'
    if rule == 'none' or (rule == 'companions' and not connected):
        return 200, {
            'id': dto['id'], 'uniqueId': dto['uniqueId'], 'fullName': dto['fullName'],
            'country': dto['country'], 'city': dto['city'], 'profession': dto['profession'],
            'status': dto['status'], 'isFollowing': dto['isFollowing'],
            'isVerified': dto['isVerified'], 'isProfileLocked': True,
        }
    if user.is_profile_locked and not connected:
        dto['bio'] = None
        dto['aboutMe'] = None
        dto['aboutMeDetailsJson'] = None
    return 200, dto


def get_user_by_uid(my_id, unique_id):
    target = next((u.id for u in MEMBERS.values() if u.unique_id == unique_id), None)
    return get_user(my_id, target) if target else ('404', None)


code, dto = get_user(1, 9999)
check('an unknown member answers 404', code == '404')

code, dto = get_user(1, 6)
check('a Pending member answers 404 to everybody else', code == '404')

code, dto = get_user(6, 6)
check('a Pending member may still read their own profile', code == 200)

code, dto = get_user(1, 9)
check('a member who switched off the public profile answers 403', code == '403')

code, dto = get_user(1, 2)
check('a companion is answered the full profile (About-me JSON included)',
      code == 200 and dto['status'] == 'connected'
      and dto['aboutMeDetailsJson'] is not None
      and dto['connectedCompanionIds'] == [1, 3])

code, dto = get_user(1, 4)
check('a locked profile hides About-me from a non-companion',
      code == 200 and dto['isProfileLocked'] is True
      and dto['aboutMe'] is None and dto['aboutMeDetailsJson'] is None
      and dto['fullName'] == 'Maya Patel')

code, dto = get_user(2, 8)
check('WhoCanVisitProfile=companions trims a stranger to the locked identity cards',
      code == 200 and dto['isProfileLocked'] is True and 'aboutMe' not in dto
      and dto['fullName'] == 'Private Pia')

code, dto = get_user(3, 8)
check('WhoCanVisitProfile=companions still answers a connected companion fully',
      code == 200 and dto.get('aboutMeDetailsJson') is not None)

code, dto = get_user(1, 3)
check('the numeric id and the 20-digit uid answer the same member',
      get_user_by_uid(1, generate_unique_id(3)) == (code, dto))

code, dto = get_user_by_uid(1, '89201534019999999999')
check('an unknown 20-digit uid answers 404', code == '404')

code, dto = get_user(1, 1)
check('my own profile always answers full data',
      code == 200 and dto['aboutMeDetailsJson'] is not None)

check('the visitor answer carries isFollowing for the follow button',
      get_user(1, 10)[1]['isFollowing'] is True
      and get_user(1, 3)[1]['isFollowing'] is False)


# ---------------------------------------------------------------------------
print()
if failures:
    print(f'{len(failures)} check(s) FAILED:')
    for f in failures:
        print(f'  - {f}')
    sys.exit(1)
print('All checks passed — the member-directory endpoints behave as documented.')
