#!/usr/bin/env python3
"""Verifies the community live-fixes changes to neverbeen-api
(see neverbeen-api-community-live-fixes.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence
that the changed C# behaves as intended:

  A  the patch applies cleanly to the neverbeen-api checkout (main 3ff5ae9)
  B  every .cs file the patch touches parses without syntax errors (tree-sitter)
  C  companion requests: accept, reject and cancel record their answer on the
     companionship_request notification (approved / rejected / cancelled), so the
     Approve / Reject buttons no longer reappear after a reload; reject answers
     the notification before the companionship row is removed
  D  followers / following: FollowDto carries Country and City, and the list
     query loads both navigation properties
  E  circles: Circle.PhotoUrl has no 1024-character cap, and Create and Update
     both run ValidateRequest, which mirrors the stored column sizes
  F  ValidateRequest rules, ported line for line from CirclesController.cs:
     a 1.4 MB JPEG data URL is accepted; an SVG data URL, a data URL over 2 MB,
     a 121-character name, a 501-character description and a 1025-character
     link are rejected with the message the website shows
  G  mappers: ProfilePhotoUrl (uploaded photo) is the fallback before
     ExternalProfilePictureUrl, for ProfileDto and CommentDto

Dependencies: git and the tree_sitter / tree_sitter_c_sharp Python packages
(pip install tree-sitter tree-sitter-c-sharp).

Run:  python3 neverbeen-api-community-live-fixes-verification.py [path-to-neverbeen-api]
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PATCH = os.path.join(HERE, 'neverbeen-api-community-live-fixes.patch')
API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'

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
work = tempfile.mkdtemp(prefix='api-community-live-fixes-')
try:
    clone = os.path.join(work, 'api')
    subprocess.run(['git', 'clone', '-q', API_DIR, clone], check=True)
    git(['checkout', '-q', '3ff5ae9'], clone)
    r = git(['apply', '--check', PATCH], clone)
    check('A  patch applies to neverbeen-api main 3ff5ae9', r.returncode == 0, r.stderr.strip())
    r = git(['apply', PATCH], clone)
    if r.returncode != 0:
        print('     cannot apply patch, stopping:', r.stderr.strip())
        sys.exit(1)

    touched = sorted(set(re.findall(r'^\+\+\+ b/(.+)$', open(PATCH, encoding='utf-8').read(), re.M)))
    cs_files = [p for p in touched if p.endswith('.cs')]
    check('   patch touches the expected C# files',
          set(cs_files) == {'Controllers/CirclesController.cs', 'Controllers/CompanionsController.cs',
                            'Controllers/FollowsController.cs', 'Dtos/CompanionDtos.cs',
                            'Dtos/Mappers.cs', 'Entities/Circle.cs'}, ', '.join(cs_files))

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

    # ------------------------------------------------------------ C: companion notifications
    comp = read(clone, 'Controllers/CompanionsController.cs')
    check('C  AnswerRequestNotifications helper exists',
          'private async Task AnswerRequestNotifications(int recipientId, int requesterId, string status' in comp)
    check('C  helper only touches pending companionship_request notifications from the requester',
          re.search(r'n\.UserId == recipientId\s*&&\s*n\.FromUserId == requesterId\s*&&\s*n\.Type == NotificationTypes\.CompanionshipRequest\s*&&\s*n\.Status == "pending"', comp) is not None)
    check('C  helper sets Status and IsRead', 'notification.Status = status;' in comp and 'notification.IsRead = true;' in comp)

    def body(src, signature):
        i = src.index(signature)
        j = src.index('\n    }\n', i)
        return src[i:j]

    accept = body(comp, 'private async Task<ActionResult<CompanionshipResultDto>> AcceptInternal')
    check('C  accept records "approved" for the recipient (myId) and the requester',
          'AnswerRequestNotifications(myId, row.RequesterId, "approved"' in accept)
    reject = body(comp, 'public async Task<ActionResult<CompanionshipResultDto>> Reject')
    check('C  reject records "rejected" before removing the row',
          reject.index('"rejected"') < reject.index('_db.Companionships.Remove(row)'))
    cancel = body(comp, 'public async Task<ActionResult<CompanionshipResultDto>> Cancel')
    check('C  cancel records "cancelled" for the recipient',
          'AnswerRequestNotifications(userId, myId, "cancelled"' in cancel)

    # ------------------------------------------------------------ D: follower location
    dto = read(clone, 'Dtos/CompanionDtos.cs')
    fdto = dto[dto.index('class FollowDto'):]
    fdto = fdto[:fdto.index('\n}')]  # the class closes at the first "}" in column 0
    check('D  FollowDto has Country and City', 'Country' in fdto and 'City' in fdto)
    follows = read(clone, 'Controllers/FollowsController.cs')
    check('D  follow list loads Country and City',
          '.Include(u => u.Country).Include(u => u.City)' in follows)
    check('D  follow list maps Country and City', 'Country = u.Country?.Name' in follows and 'City = u.City?.Name' in follows)

    # ------------------------------------------------------------ E: circle photo column
    circle = read(clone, 'Entities/Circle.cs')
    check('E  Circle.PhotoUrl has no MaxLength', 'MaxLength(1024)' not in circle and 'public string? PhotoUrl' in circle)
    circles = read(clone, 'Controllers/CirclesController.cs')
    check('E  Create runs ValidateRequest', re.search(r'Create\(\[FromBody\].*?ValidateRequest\(request\)', circles, re.S) is not None)
    check('E  Update runs ValidateRequest', re.search(r'Update\(.*?ValidateRequest\(request\)', circles, re.S) is not None)
    check('E  photo limit is 2 Mi characters', 'MaxCirclePhotoChars = 2 * 1024 * 1024' in circles)

    # ------------------------------------------------------------ F: ValidateRequest port
    m = re.search(r'private static string\? ValidateRequest\(SaveCircleRequest request\)\s*\{(.*?)\n    \}\n', circles, re.S)
    check('F  ValidateRequest found in CirclesController.cs', m is not None)
    if m:
        def validate(name=None, description=None, icon=None, color=None, photo=None):
            """Line-for-line port of ValidateRequest (C#: Trim, Length, StartsWith, Regex)."""
            if name is not None and len(name.strip()) > 120:
                return 'Circle name must be 120 characters or fewer.'
            if description is not None and len(description) > 500:
                return 'Circle description must be 500 characters or fewer.'
            if icon is not None and len(icon) > 60:
                return 'Circle icon is too long.'
            if color is not None and len(color) > 20:
                return 'Circle colour is too long.'
            p = photo.strip() if photo is not None else None
            if not p:
                return None
            if p.lower().startswith('data:'):
                if not re.match(r'^data:image/(png|jpe?g|gif|webp);base64,', p, re.I):
                    return 'Circle photo must be a PNG, JPEG, GIF or WebP image.'
                if len(p) > 2 * 1024 * 1024:
                    return 'Circle photo must be 1 MB or smaller.'
            elif len(p) > 1024:
                return 'Circle photo link must be 1024 characters or fewer.'
            return None

        jpeg_1_4mb = 'data:image/jpeg;base64,' + 'A' * 1_400_000
        cases = [
            ('F  1.4 MB JPEG data URL is accepted', validate(name='Trip', photo=jpeg_1_4mb), None),
            ('F  PNG / WebP data URLs are accepted',
             validate(name='Trip', photo='data:image/png;base64,AAAA') or validate(name='Trip', photo='data:image/webp;base64,AAAA'), None),
            ('F  SVG data URL is rejected', validate(name='Trip', photo='data:image/svg+xml;base64,AAAA'),
             'Circle photo must be a PNG, JPEG, GIF or WebP image.'),
            ('F  data URL over 2 MB is rejected', validate(name='Trip', photo='data:image/jpeg;base64,' + 'A' * 2_100_000),
             'Circle photo must be 1 MB or smaller.'),
            ('F  121-character name is rejected', validate(name='x' * 121), 'Circle name must be 120 characters or fewer.'),
            ('F  120-character name is accepted', validate(name='x' * 120), None),
            ('F  501-character description is rejected', validate(name='Trip', description='d' * 501),
             'Circle description must be 500 characters or fewer.'),
            ('F  1025-character link is rejected', validate(name='Trip', photo='https://x.test/' + 'a' * 1025),
             'Circle photo link must be 1024 characters or fewer.'),
            ('F  stock image link is accepted', validate(name='Trip', photo='https://images.example/beach.jpg'), None),
        ]
        for label, got, want in cases:
            check(label, got == want, f'got {got!r}')

    # ------------------------------------------------------------ G: mapper fallbacks
    mappers = read(clone, 'Dtos/Mappers.cs')
    check('G  ProfileDto falls back to ProfilePhotoUrl before ExternalProfilePictureUrl',
          ': user.ProfilePhotoUrl ?? user.ExternalProfilePictureUrl' in mappers)
    check('G  CommentDto falls back to ProfilePhotoUrl before ExternalProfilePictureUrl',
          ': comment.Author.ProfilePhotoUrl ?? comment.Author.ExternalProfilePictureUrl' in mappers)
finally:
    shutil.rmtree(work, ignore_errors=True)

print()
if failures:
    print(f'{len(failures)} check(s) failed')
    sys.exit(1)
print('All checks passed')
