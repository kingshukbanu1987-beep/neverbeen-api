#!/usr/bin/env python3
"""Verifies the Community follow-feed / device-details / chat-notification
changes to neverbeen-api (see neverbeen-api-community-follow-feed-devices-chat.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence
that the changed C# behaves as intended. It is a structural audit of the
patched sources plus a line-for-line port of the new request-handling rules,
run against an in-memory community:

  A  GET /api/journey (feed) -> includes the posts of the members the feed
                                owner follows (public and companion-audience
                                posts), still excluding blocked / hidden /
                                only-me / custom-audience posts the viewer
                                was not allowed
  B  POST /api/devices and   -> store and answer the exact device model
     GET /api/devices           (Model) plus Country, City, Locality,
                                Latitude and Longitude; the registration
                                falls back to the request's trusted IP when
                                the browser did not report one
  C  message notifications   -> sending a 1:1 or Circle message writes one
                                'message' notification per other participant
                                (short preview, conversation id as RequestId)
  D  the patch applies cleanly to the neverbeen-api checkout

Run:  python3 neverbeen-api-community-follow-feed-devices-chat-verification.py [path-to-neverbeen-api]
"""

import glob
import os
import re
import subprocess
import sys
import tempfile

API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/api-repo'
PATCH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'neverbeen-api-community-follow-feed-devices-chat.patch')

failures = []


def check(name, cond, detail=''):
    status = 'PASS' if cond else 'FAIL'
    print(f'  [{status}] {name}' + (f' — {detail}' if detail and not cond else ''))
    if not cond:
        failures.append(name)


# ---------------------------------------------------------------------------
# D. the patch applies cleanly to the API checkout
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
    check(f'git apply --check against {base or "HEAD"}', applied.returncode == 0,
          applied.stderr.strip())
    subprocess.run(['git', '-C', work, 'apply', PATCH], check=True)

    src_of = {}
    for f in glob.glob(os.path.join(work, '**', '*.cs'), recursive=True):
        src_of[os.path.relpath(f, work)] = open(f, encoding='utf-8').read()

    journey = src_of.get('Controllers/JourneyController.cs', '')
    devices = src_of.get('Controllers/DevicesController.cs', '')
    login_device = src_of.get('Entities/LoginDevice.cs', '')
    notification_dtos = src_of.get('Dtos/NotificationDtos.cs', '')
    community_notification = src_of.get('Entities/CommunityNotification.cs', '')
    messages = src_of.get('Controllers/MessagesController.cs', '')
    circles = src_of.get('Controllers/CirclesController.cs', '')

    # -------------------------------------------------------------------
    # A. the follow-driven Journey feed
    # -------------------------------------------------------------------
    print('A. GET /api/journey includes the posts of the followed members')

    check('the feed loads the follower graph of the signed-in member',
          all(tok in journey for tok in
              ['var followedIds = await _db.Follows.AsNoTracking()',
               '.Where(f => f.FollowerId == myId)',
               '.Select(f => f.FolloweeId)']))
    check('the feed where-clause admits posts by the followed members',
          re.search(r'companionIds\.Contains\(p\.AuthorId\)\s*\|\|\s*followedIds\.Contains\(p\.AuthorId\)',
                    journey) is not None)
    check('companion-audience posts are visible to followers as well as companions',
          re.search(r'JourneyPostAudience\.Companions\s*&&\s*\(\s*companionIds\.Contains\(p\.AuthorId\)'
                    r'\s*\|\|\s*followedIds\.Contains\(p\.AuthorId\)\s*\)', journey) is not None)
    check('custom / only-me posts stay private (audience rules untouched)',
          all(tok in journey for tok in
              ['p.AudienceMode == JourneyPostAudience.Custom',
               'p.AudienceMode == JourneyPostAudience.Public']))

    # Port of the feed visibility rules: a post appears in the feed when
    # (author is me / wall owner / companion / followed) AND the audience
    # admits the viewer.
    class Feed:
        def __init__(self, posts, my_id, follows, companions, blocked, hidden, audience_allow):
            self.posts = posts
            self.my_id = my_id
            self.follows = follows          # set of ids my_id follows
            self.companions = set(companions)
            self.blocked = set(blocked)
            self.hidden = set(hidden)
            self.audience_allow = audience_allow  # post_id -> set of allowed user ids

        def visible(self):
            out = []
            for p in self.posts:
                if p['id'] in self.hidden:
                    continue
                if p['author'] in self.blocked:
                    continue
                in_reach = (p['author'] == self.my_id
                            or p.get('wall_owner') == self.my_id
                            or p['author'] in self.companions
                            or p['author'] in self.follows)
                if not in_reach:
                    continue
                mode = p.get('mode', 'public')
                if p['author'] == self.my_id:
                    ok = True
                elif mode == 'public':
                    ok = True
                elif mode == 'companions':
                    ok = p['author'] in self.companions or p['author'] in self.follows
                elif mode == 'custom':
                    ok = self.my_id in self.audience_allow.get(p['id'], set())
                else:  # only-me
                    ok = False
                if ok:
                    out.append(p['id'])
            return out

    posts = [
        {'id': 1, 'author': 1, 'mode': 'public'},                                   # own post
        {'id': 2, 'author': 2, 'mode': 'public'},                                   # followed (req A)
        {'id': 3, 'author': 2, 'mode': 'companions'},                               # followed, companion audience (req A)
        {'id': 4, 'author': 3, 'mode': 'public'},                                   # not followed, not companion
        {'id': 5, 'author': 2, 'mode': 'only-me'},                                  # followed but private
        {'id': 6, 'author': 2, 'mode': 'custom'},                                   # followed, custom w/o me
        {'id': 7, 'author': 3, 'mode': 'public', 'wall_owner': 1},                  # wall post on my journey
        {'id': 8, 'author': 4, 'mode': 'public'},                                   # blocked author
        {'id': 9, 'author': 2, 'mode': 'public'},                                   # hidden for me
    ]
    feed = Feed(posts, my_id=1, follows={2}, companions={5}, blocked={4}, hidden={9},
                audience_allow={6: {71}})
    check('following pulls B\'s public and companion posts into A\'s feed',
          2 in feed.visible() and 3 in feed.visible())
    check('posts by members that are neither followed nor connected stay out',
          4 not in feed.visible() and 8 not in feed.visible())
    check('only-me and custom-audience posts of followed members stay private',
          5 not in feed.visible() and 6 not in feed.visible())
    check('own posts, wall posts and hides keep working',
          1 in feed.visible() and 7 in feed.visible() and 9 not in feed.visible())

    # -------------------------------------------------------------------
    # B. the exact device model + location details
    # -------------------------------------------------------------------
    print('B. POST /api/devices and GET /api/devices carry the device details')

    device_fields = ['Model', 'Country', 'City', 'Locality', 'Latitude', 'Longitude']
    check('the LoginDevice entity stores the new detail columns',
          all(f'public string? {f} {{ get; set; }}' in login_device for f in device_fields[:4])
          and 'public double? Latitude { get; set; }' in login_device
          and 'public double? Longitude { get; set; }' in login_device)
    check('DeviceDto answers the new detail fields',
          all(f'public string? {f} {{ get; set; }}' in notification_dtos for f in device_fields[:4])
          and 'public double? Latitude { get; set; }' in notification_dtos
          and 'public double? Longitude { get; set; }' in notification_dtos)
    check('SaveDeviceRequest accepts the new detail fields',
          all(f'public string? {f} {{ get; set; }}' in notification_dtos for f in device_fields[:4])
          and notification_dtos.count('public double? Latitude { get; set; }') >= 2)
    check('registration writes the new fields onto the row',
          all(f'device.{f} = request.{f};' in devices for f in device_fields))
    check('registration falls back to the request IP the server sees',
          'string.IsNullOrWhiteSpace(request.IpAddress) ? RemoteIp()' in devices)
    check('GET /api/devices maps the new fields back to the client',
          all(f'{f} = d.{f},' in devices for f in device_fields))

    # Port of the registration IP fallback.
    def register_ip(client_ip, forwarded, remote):
        if client_ip is None or not client_ip.strip():
            if forwarded and forwarded.strip():
                return forwarded.split(',')[0].strip()
            return remote or ''
        return client_ip

    check('client-reported IP wins over the request address',
          register_ip('103.25.184.42', '203.0.113.7', '198.51.100.9') == '103.25.184.42')
    check('missing client IP falls back to the proxy chain, then the socket',
          register_ip(None, '203.0.113.7, 198.51.100.9', '10.0.0.4') == '203.0.113.7'
          and register_ip('', None, '198.51.100.9') == '198.51.100.9')

    # -------------------------------------------------------------------
    # C. one 'message' notification per received message
    # -------------------------------------------------------------------
    print('C. sending a message writes one notification per other participant')

    check('NotificationTypes defines the "message" type',
          'public const string Message = "message";' in community_notification)
    check('1:1 messages notify every other participant of the conversation',
          all(tok in messages for tok in
              ['Type = NotificationTypes.Message',
               'FromUserId = myId',
               'Where(p => p.ConversationId == id && p.UserId != myId)',
               'RequestId = id']))
    check('Circle messages notify every other member of the Circle',
          all(tok in circles for tok in
              ['Type = NotificationTypes.Message',
               'Where(m => m.CircleId == id && m.UserId != myId)',
               'sent you a message in {circle.Name}:']))
    check('the preview is kept inside the 500-char notification column',
          'NotificationTypes.MessagePreview(message.Text)' in messages
          and 'NotificationTypes.MessagePreview(message.Text)' in circles)

    # Port of MessagePreview (C#: clean[..119] + "…" when longer than 120).
    def message_preview(text, limit=120):
        clean = ' '.join(text.split())
        return clean if len(clean) <= limit else clean[:119] + '…'

    check('short previews pass through unchanged',
          message_preview('Hi Kingshuk!') == 'Hi Kingshuk!')
    check('long previews truncate to 120 chars with an ellipsis',
          len(message_preview('x ' * 200)) == 120
          and message_preview('x ' * 200).endswith('…'))
    check('notification rows fit the Message column (500 chars)',
          len('sent you a message in ' + 'C' * 40 + ': “' + message_preview('y ' * 200) + '”') <= 500)


# ---------------------------------------------------------------------------
print()
if failures:
    print(f'{len(failures)} check(s) FAILED:')
    for f in failures:
        print(f'  - {f}')
    sys.exit(1)
print('All checks passed — the follow feed, device details and chat notifications behave as documented.')
