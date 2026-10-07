#!/usr/bin/env python3
"""Verifies the First name / Last name / State logic added to neverbeen-api's
registration + profile endpoints (see neverbeen-api-registration-names-state.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence that the
patch stores the three fields the registration page collects:

  1. it checks the patched C# sources really carry the fields end-to-end
     (RegistrationRequest -> RegistrationController -> ProfileDto / Mapper,
      UpdateProfileRequest -> ProfileController, AuthController for the Pending row),
  2. it runs a line-for-line port of ResolveNames() / ProfileMapper.SplitFirstName() /
     SplitLastName() over the payloads the Angular registration page sends, plus the
     older-client payload that only has "fullName",
  3. it walks a registration payload through the ported controller into the row and out
     through the ported mapper, and checks PUT /api/profile semantics
     (omitted field keeps its value, empty string clears it).

Usage:
    python3 neverbeen-api-registration-names-state-verification.py [neverbeen-api-checkout]

The default checkout path is the directory this script sits in (the neverbeen-api
repository root, or docs/patches/ of the neverbeen web app — pass the checkout as the
first argument there).
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_API = HERE if os.path.exists(os.path.join(HERE, 'Dtos', 'RegistrationDtos.cs')) \
    else '/home/user/neverbeen-api'
API = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_API


def read(rel):
    with open(os.path.join(API, rel), encoding='utf-8') as fh:
        return fh.read()


# --------------------------------------------------------------------------- #
# 1. The patched sources carry the fields end to end
# --------------------------------------------------------------------------- #
checks = []
def check(label, ok):
    checks.append((label, ok))
    print(('  PASS  ' if ok else '  FAIL  ') + label)

print('Source checks (patched neverbeen-api checkout at %s)' % API)

reg_dto = read('Dtos/RegistrationDtos.cs')
check('RegistrationRequest has FirstName / LastName / State',
      all(re.search(r'public string\? %s \{ get; set; \}' % name, reg_dto)
          for name in ('FirstName', 'LastName', 'State')))

reg_ctl = read('Controllers/RegistrationController.cs')
check('RegistrationController writes user.FirstName / user.LastName',
      'user.FirstName = firstName;' in reg_ctl and 'user.LastName = lastName;' in reg_ctl)
check('RegistrationController writes user.State',
      'user.State = request.State.Trim();' in reg_ctl)
check('RegistrationController resolves names with a FullName fallback',
      'ResolveNames(request.FirstName, request.LastName, user.FullName)' in reg_ctl)

profile_dto = read('Dtos/ProfileDtos.cs')
check('ProfileDto answers FirstName / LastName / State',
      all(re.search(r'public string\? %s \{ get; set; \}' % name, profile_dto)
          for name in ('FirstName', 'LastName', 'State')))
check('UpdateProfileRequest accepts FirstName / LastName / State',
      len(re.findall(r'public string\? (?:FirstName|LastName|State) \{ get; set; \}',
                     profile_dto.split('public class UpdateProfileRequest')[1])) == 3)

mappers = read('Dtos/Mappers.cs')
check('ProfileMapper fills FirstName / LastName / State',
      'FirstName = SplitFirstName(user),' in mappers
      and 'LastName = SplitLastName(user),' in mappers
      and 'State = user.State,' in mappers)

profile_ctl = read('Controllers/ProfileController.cs')
check('ProfileController applies the three fields on PUT',
      'user.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim();' in profile_ctl
      and 'user.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();' in profile_ctl
      and 'user.State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim();' in profile_ctl)

auth_ctl = read('Controllers/AuthController.cs')
check('AuthController fills the name columns for a new Pending row',
      'FirstName = string.IsNullOrWhiteSpace(external.FirstName)' in auth_ctl
      and 'LastName = string.IsNullOrWhiteSpace(external.LastName)' in auth_ctl)


# --------------------------------------------------------------------------- #
# 2. Ports of the C# helpers, run over the payloads the app sends
# --------------------------------------------------------------------------- #
def split_words(text):
    return [w for w in (text or '').split(' ') if w.strip()]


def resolve_names(first_name, last_name, full_name):
    """Port of RegistrationController.ResolveNames()."""
    first = first_name.strip() or None if first_name else None
    last = last_name.strip() or None if last_name else None
    if first is not None and last is not None:
        return first, last
    words = split_words(full_name)
    if not words:
        return first, last
    if first is None:
        first = words[0]
    if last is None:
        rest = words[max(1, len(split_words(first))):]
        last = ' '.join(rest) if rest else None
    return first, last


def split_first_name(first_name, full_name):
    """Port of ProfileMapper.SplitFirstName()."""
    if first_name and first_name.strip():
        return first_name.strip()
    words = split_words(full_name)
    return words[0] if words else None


def split_last_name(last_name, full_name):
    """Port of ProfileMapper.SplitLastName()."""
    if last_name and last_name.strip():
        return last_name.strip()
    words = split_words(full_name)
    return ' '.join(words[1:]) if len(words) > 1 else None


def register(payload, row=None):
    """Port of RegistrationController.Complete(): the stored row after a registration."""
    row = dict(row or {'FirstName': None, 'LastName': None, 'State': None})
    full_name = payload['FullName'].strip()
    first, last = resolve_names(payload.get('FirstName'), payload.get('LastName'), full_name)
    row['FullName'] = full_name
    row['FirstName'] = first
    row['LastName'] = last
    if payload.get('State') and payload['State'].strip():
        row['State'] = payload['State'].strip()
    return row


def to_profile_dto(row):
    """Port of ProfileMapper.ToDto() for the three fields."""
    return {
        'FullName': row.get('FullName'),
        'FirstName': split_first_name(row.get('FirstName'), row.get('FullName')),
        'LastName': split_last_name(row.get('LastName'), row.get('FullName')),
        'State': row.get('State'),
    }


def update_profile(row, request):
    """Port of ProfileController.Update() for the three fields."""
    row = dict(row)
    if request.get('FirstName') is not None:
        row['FirstName'] = request['FirstName'].strip() or None
    if request.get('LastName') is not None:
        row['LastName'] = request['LastName'].strip() or None
    if request.get('State') is not None:
        row['State'] = request['State'].strip() or None
    return row


print('\nRegistration payloads (what the Angular registration page sends)')
# The registration page posts name / surname / state as firstName / lastName / state.
cases = [
    ({'FullName': 'Kingshuk Banu', 'FirstName': 'Kingshuk', 'LastName': 'Banu',
      'State': 'West Bengal'}, ('Kingshuk', 'Banu', 'West Bengal')),
    ({'FullName': '  Kingshuk   Banu  ', 'FirstName': ' Kingshuk ', 'LastName': ' Banu ',
      'State': ' West Bengal '}, ('Kingshuk', 'Banu', 'West Bengal')),
    # Older client: only the full name — the columns are still filled by the split.
    ({'FullName': 'Kingshuk Banu', 'State': 'West Bengal'}, ('Kingshuk', 'Banu', 'West Bengal')),
    ({'FullName': 'Kingshuk Banu'}, ('Kingshuk', 'Banu', None)),
    ({'FullName': 'Ana Maria Lopez'}, ('Ana', 'Maria Lopez', None)),
    ({'FullName': 'Madonna'}, ('Madonna', None, None)),
    ({'FullName': 'Kingshuk Banu', 'FirstName': 'Kingshuk'},
     ('Kingshuk', 'Banu', None)),
    ({'FullName': 'Kingshuk Banu', 'LastName': 'Banu'}, ('Kingshuk', 'Banu', None)),
    # A payload with only spaces in the name parts falls back to the full name.
    ({'FullName': 'Kingshuk Banu', 'FirstName': '  ', 'LastName': ''},
     ('Kingshuk', 'Banu', None)),
]

for payload, expected in cases:
    row = register(payload)
    got = (row['FirstName'], row['LastName'], row['State'])
    check('%-58s -> %s' % (str({k: payload[k] for k in payload}), got), got == expected)
    # Every stored row answers the same three values through the profile DTO.
    dto = to_profile_dto(row)
    check('   ProfileDto answers the stored values',
          (dto['FirstName'], dto['LastName'], dto['State']) == expected)

print('\nRows created before the columns were filled (mapper split fallback)')
legacy = [
    ({'FullName': 'Sofia Laurent', 'FirstName': None, 'LastName': None},
     ('Sofia', 'Laurent')),
    ({'FullName': 'Kingshuk', 'FirstName': '', 'LastName': ''}, ('Kingshuk', None)),
    ({'FullName': None, 'FirstName': None, 'LastName': None}, (None, None)),
    ({'FullName': 'Ana Maria Lopez', 'FirstName': None, 'LastName': 'Lopez'},
     ('Ana', 'Lopez')),
    ({'FullName': 'Ana Maria Lopez', 'FirstName': 'Ana Maria', 'LastName': 'Lopez'},
     ('Ana Maria', 'Lopez')),
]
for row, expected in legacy:
    dto = to_profile_dto(row)
    got = (dto['FirstName'], dto['LastName'])
    check('%-58s -> %s' % (str(row), got), got == expected)

print('\nPUT /api/profile semantics (omitted keeps, empty clears)')
row = {'FullName': 'Kingshuk Banu', 'FirstName': 'Kingshuk', 'LastName': 'Banu', 'State': 'West Bengal'}
check('empty object keeps everything',
      update_profile(row, {}) == row)
check('state updated', update_profile(row, {'State': 'Karnataka'})['State'] == 'Karnataka')
check('state cleared', update_profile(row, {'State': ''})['State'] is None)
check('names updated',
      (update_profile(row, {'FirstName': 'K', 'LastName': 'B'})['FirstName'],
       update_profile(row, {'FirstName': 'K', 'LastName': 'B'})['LastName']) == ('K', 'B'))
check('names cleared',
      (update_profile(row, {'FirstName': ''})['FirstName'],
       update_profile(row, {'LastName': ''})['LastName']) == (None, None))
check('row (and state) untouched while clearing a name',
      update_profile(row, {'FirstName': ''})['State'] == 'West Bengal')

print('\nPending member row created by the OAuth login (AuthController)')
def pending_row(external, email):
    full_name = (external.get('FullName') or '').strip() or email.split('@')[0]
    words = split_words(full_name)
    return {
        'Email': email,
        'FullName': full_name,
        'FirstName': external['FirstName'].strip() if external.get('FirstName', '').strip()
        else (words[0] if words else None),
        'LastName': external['LastName'].strip() if external.get('LastName', '').strip()
        else (' '.join(words[1:]) if len(words) > 1 else None),
    }

cases = [
    ({'FullName': 'Kingshuk Banu', 'FirstName': 'Kingshuk', 'LastName': 'Banu'},
     'kingshuk.banu@gmail.com', ('Kingshuk', 'Banu')),
    ({'FullName': 'Kingshuk Banu'}, 'kingshuk.banu@gmail.com', ('Kingshuk', 'Banu')),
    ({'FullName': None}, 'traveler.google@gmail.com', ('traveler.google', None)),
    ({'FullName': 'Madonna'}, 'madonna@gmail.com', ('Madonna', None)),
]
for external, email, expected in cases:
    row = pending_row(external, email)
    got = (row['FirstName'], row['LastName'])
    check('google %-46s -> %s' % (str(external), got), got == expected)

failed = [label for label, ok in checks if not ok]
print('\n%d checks, %d failed' % (len(checks), len(failed)))
if failed:
    for label in failed:
        print('  FAILED: ' + label)
    sys.exit(1)
print('ALL CHECKS PASS')
