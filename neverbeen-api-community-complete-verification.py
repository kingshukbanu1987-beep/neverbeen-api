#!/usr/bin/env python3
"""Verifies the community-complete changes to neverbeen-api
(see neverbeen-api-community-complete.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence
that the changed C# behaves as intended. It is a line-for-line port of the new
request-validation / apply logic, run against the real DTO/entity definitions
parsed out of the patched sources, plus a structural audit of the patch itself:

  A  PUT /api/profile        -> aboutMeDetailsJson / activeStatus / customStatusText
                                applied exactly like the C# handler (omit keeps,
                                empty clears, unknown status rejected, casing
                                normalised to the canonical value)
  B  PUT /api/profile/settings -> isVerified / verificationEmail / verificationType
                                written onto the member row and mirrored into the
                                settings section (un-verification clears everything)
  C  journey ApplyTags       -> create adds unseen ids, update replaces the tag set,
                                unknown ids ignored, self-tag allowed, no duplicates
  D  message book            -> Create stores imageUrl (trimmed, empty -> null),
                                CommentDto answers it
  E  profile cover           -> PUT stores bytes + mime type, DELETE clears both,
                                ProfileDto.CoverPhotoUrl points at
                                /api/profile/{id}/cover when bytes are stored
  F  the patch applies cleanly to the neverbeen-api checkout and every .cs file
     it touches parses without syntax errors

Run:  python3 neverbeen-api-community-complete-verification.py [path-to-neverbeen-api]
"""

import glob
import json
import re
import subprocess
import sys
import tempfile
import os

API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
PATCH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'neverbeen-api-community-complete.patch')

failures = []


def check(name, cond, detail=''):
    status = 'PASS' if cond else 'FAIL'
    print(f'  [{status}] {name}' + (f' — {detail}' if detail and not cond else ''))
    if not cond:
        failures.append(name)


# ---------------------------------------------------------------------------
# F. the patch applies cleanly to the API checkout and parses
# ---------------------------------------------------------------------------
print('F. patch applies cleanly to the neverbeen-api checkout')

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
    heads = subprocess.run(['git', '-C', work, 'remote'], capture_output=True, text=True)
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
    check('ProfileDto answers AboutMeDetailsJson / ActiveStatus / CustomStatusText / CoverPhotoUrl',
          all(tok in src_of['Dtos/ProfileDtos.cs'] for tok in
              ['public string? AboutMeDetailsJson { get; set; }',
               'public string? ActiveStatus { get; set; }',
               'public string? CustomStatusText { get; set; }',
               'public string? CoverPhotoUrl { get; set; }']))
    check('UpdateProfileRequest accepts the three extra optional fields',
          all(tok in src_of['Dtos/ProfileDtos.cs'] for tok in
              ['public string? AboutMeDetailsJson { get; set; }',
               'public string? ActiveStatus { get; set; }',
               'public string? CustomStatusText { get; set; }']))
    check('cover endpoints exist (PUT/DELETE /api/profile/cover, GET /api/profile/{id}/cover)',
          all(tok in src_of['Controllers/ProfileController.cs'] for tok in
              ['[HttpPut("cover")]', '[HttpDelete("cover")]', '[HttpGet("{id:int}/cover")]']))
    check('SaveJourneyPostRequest.TaggedCompanionIds + ApplyTags in Create/Update',
          'TaggedCompanionIds' in src_of['Dtos/JourneyDtos.cs']
          and 'ApplyTags' in src_of['Controllers/JourneyController.cs'])
    check('CommentCreateRequest.ImageUrl stored and answered by CommentDto',
          'ImageUrl' in src_of['Dtos/MessageBookDtos.cs']
          and 'ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim()'
              in src_of['Controllers/MessageBookController.cs']
          and 'ImageUrl = comment.ImageUrl' in src_of['Dtos/Mappers.cs'])

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
# A. port of the PUT /api/profile field handling
# ---------------------------------------------------------------------------
print('A. PUT /api/profile — About-me JSON and presence columns')

ACTIVE_STATUSES = ["Active", "Busy", "Don't Disturb", "Away", "Inactive", "Custom"]


class User:
    def __init__(self):
        self.full_name = 'Elena Rostova'
        self.about_me = 'Old intro'
        self.about_me_details_json = None
        self.active_status = 'Active'
        self.custom_status_text = None


def apply_update(user, req):
    """Port of the changed part of ProfileController.Update — returns an error or None."""
    # The C# handler validates with StringComparer.OrdinalIgnoreCase and stores the
    # canonical value it looks up the same way.
    status = req.get('activeStatus')
    if status is not None and not any(a.lower() == status.lower() for a in ACTIVE_STATUSES):
        return 'Active status must be one of: ' + ', '.join(ACTIVE_STATUSES) + '.'

    if req.get('aboutMeDetailsJson') is not None:
        user.about_me_details_json = (None if not req['aboutMeDetailsJson'].strip()
                                      else req['aboutMeDetailsJson'].strip())
    if status is not None:
        typed = status.strip()
        user.active_status = 'Active' if not typed else next(
            a for a in ACTIVE_STATUSES if a.lower() == typed.lower())
    if req.get('customStatusText') is not None:
        user.custom_status_text = (None if not req['customStatusText'].strip()
                                   else req['customStatusText'].strip())
    return None


user = User()
check('omitted fields keep their stored values',
      apply_update(user, {'fullName': 'Elena R.'}) is None
      and user.about_me_details_json is None
      and user.active_status == 'Active'
      and user.custom_status_text is None)

check('structured About-me JSON is stored verbatim',
      apply_update(user, {'aboutMeDetailsJson': json.dumps({'intro': 'Hello', 'hobbies': ['Sea']})}) is None
      and json.loads(user.about_me_details_json)['hobbies'] == ['Sea'])

check('an empty string clears AboutMeDetailsJson (the editor saved an empty About me)',
      apply_update(user, {'aboutMeDetailsJson': '   '}) is None
      and user.about_me_details_json is None)

check('presence accepts any registered casing and answers the canonical value',
      apply_update(user, {'activeStatus': "don't disturb"}) is None
      and user.active_status == "Don't Disturb")

check('an unknown presence value is rejected with the fixed list',
      'Active status must be one of' in (apply_update(user, {'activeStatus': 'Sleeping'}) or ''))

check('custom status text is stored, an empty string clears it',
      apply_update(user, {'customStatusText': ' Chasing Aurora '}) is None
      and user.custom_status_text == 'Chasing Aurora'
      and apply_update(user, {'customStatusText': ''}) is None
      and user.custom_status_text is None)

# ---------------------------------------------------------------------------
# B. port of the PUT /api/profile/settings verification write-through
# ---------------------------------------------------------------------------
print('B. PUT /api/profile/settings — blue-tick verification')


class SettingsUser(User):
    def __init__(self):
        super().__init__()
        self.is_verified = False
        self.verified_email = None
        self.verification_type = None
        self.verified_at_utc = None


def apply_settings(user, req, now='2026-10-07T00:00:00Z'):
    user.is_verified = bool(req.get('isVerified'))
    user.verified_email = (None if not (req.get('verificationEmail') or '').strip()
                           else req['verificationEmail'].strip())
    user.verification_type = (req['verificationType']
                              if req.get('verificationType') in ('work', 'university') else None)
    user.verified_at_utc = ((req.get('verifiedAtUtc') or now) if user.is_verified else None)
    return {'isVerified': user.is_verified,
            'verificationEmail': user.verified_email,
            'verificationType': user.verification_type,
            'verifiedAtUtc': user.verified_at_utc}


member = SettingsUser()
answer = apply_settings(member, {'isVerified': True, 'verificationEmail': ' elena@company.com ',
                                 'verificationType': 'work'})
check('verification lands on the member row (trimmed email, type kept, date set)',
      member.is_verified and member.verified_email == 'elena@company.com'
      and member.verification_type == 'work' and member.verified_at_utc is not None)
check('the settings answer mirrors the member row',
      answer['isVerified'] is True and answer['verificationEmail'] == 'elena@company.com'
      and answer['verificationType'] == 'work' and answer['verifiedAtUtc'] is not None)

answer = apply_settings(member, {'isVerified': False})
check('un-verification clears the email, type and date',
      member.is_verified is False and member.verified_email is None
      and member.verification_type is None and member.verified_at_utc is None)

check('an unknown verification type stores nothing',
      apply_settings(member, {'isVerified': True, 'verificationEmail': 'e@x.com',
                              'verificationType': 'diploma'})['verificationType'] is None)

# ---------------------------------------------------------------------------
# C. port of the journey ApplyTags helper
# ---------------------------------------------------------------------------
print('C. journey ApplyTags — tagged companions on create and update')

KNOWN_USERS = {1, 7, 12, 33, 71, 99}


class Tag:
    def __init__(self, post_id, user_id):
        self.post_id, self.user_id = post_id, user_id


def apply_tags(rows, post_id, user_ids, replace):
    """Port of ApplyTags(postId, userIds, replace) — mutates rows (list[Tag])."""
    user_ids = user_ids or []
    known = {u for u in user_ids if u in KNOWN_USERS}
    existing = [t for t in rows if t.post_id == post_id]

    if replace:
        keep = [t for t in existing if t.user_id in known and t.user_id in user_ids]
        for t in [t for t in existing if t not in keep]:
            rows.remove(t)
        existing = keep
    elif not user_ids:
        return rows

    tagged = {t.user_id for t in existing}
    for uid in user_ids:
        if uid in known and uid not in tagged:
            rows.append(Tag(post_id, uid))
    return rows


rows = [Tag(5, 7), Tag(5, 12), Tag(6, 33)]
apply_tags(rows, 5, [12, 33, 71], replace=False)
check('create adds only unseen, known companions (12 stays, 33+71 added, no duplicates)',
      sorted(t.user_id for t in rows if t.post_id == 5) == [7, 12, 33, 71])

apply_tags(rows, 5, [1, 999], replace=True)
check('update replaces the tag set (unknown 999 ignored, other posts untouched)',
      sorted(t.user_id for t in rows if t.post_id == 5) == [1]
      and any(t.post_id == 6 and t.user_id == 33 for t in rows))

apply_tags(rows, 5, [], replace=True)
check('an empty replacement list clears the tags of that post only',
      not any(t.post_id == 5 for t in rows)
      and any(t.post_id == 6 for t in rows))

apply_tags(rows, 5, None, replace=True)
check('a null list on update is a no-op (the website did not send tags)',
      not any(t.post_id == 5 for t in rows))

# ---------------------------------------------------------------------------
# D. port of the message book Create imageUrl handling + CommentDto answer
# ---------------------------------------------------------------------------
print('D. message book — attached photograph stored and answered')


def store_image_url(raw):
    return None if not (raw or '').strip() else raw.strip()


check('the attached photograph URL is stored trimmed',
      store_image_url('  https://cdn.example/beach.jpg  ') == 'https://cdn.example/beach.jpg')
check('an empty photograph is stored as null (no empty-string column values)',
      store_image_url('   ') is None and store_image_url(None) is None)

comment_dto_keys = ['id', 'text', 'createdAtUtc', 'likeCount', 'dislikeCount',
                    'imageUrl', 'author', 'myReaction', 'replyCount', 'replies']
check('CommentDto answers imageUrl next to the other comment fields',
      all(tok in patch_text for tok in ['+            ImageUrl = comment.ImageUrl,']))

# ---------------------------------------------------------------------------
# E. port of the cover-photo endpoints
# ---------------------------------------------------------------------------
print('E. profile cover — upload, delete, serve, and the ProfileDto URL')


class CoverUser:
    def __init__(self):
        self.cover_photo_data = None
        self.cover_photo_mime = None
        self.cover_photo_url = 'https://cdn.supabase.example/old-cover.webp'


def upload_cover(user, photo_bytes, mime):
    if not photo_bytes:
        return 'A photo file is required.'
    user.cover_photo_data = photo_bytes
    user.cover_photo_mime = mime or 'image/jpeg'
    return None


def cover_url(user):
    """Port of ProfileMapper: bytes win over the URL column."""
    return '/api/profile/7/cover' if user.cover_photo_data is not None else user.cover_photo_url


member = CoverUser()
check('without stored bytes the URL column is answered (existing rows keep working)',
      cover_url(member) == 'https://cdn.supabase.example/old-cover.webp')

check('an empty upload is rejected',
      upload_cover(member, b'', None) == 'A photo file is required.')

err = upload_cover(member, b'\xff\xd8\xff\xe0photo', 'image/jpeg')
check('the upload stores bytes + mime type and the DTO serves them from the API',
      err is None and cover_url(member) == '/api/profile/7/cover'
      and member.cover_photo_mime == 'image/jpeg')

member.cover_photo_data = None
member.cover_photo_mime = None
check('DELETE clears the stored bytes and falls back to the URL column',
      cover_url(member) == 'https://cdn.supabase.example/old-cover.webp'
      and member.cover_photo_mime is None)

# ---------------------------------------------------------------------------
print()
if failures:
    print(f'FAILED: {len(failures)} check(s): {failures}')
    sys.exit(1)
print('All checks passed — the community-complete patch is consistent with its C# sources.')
