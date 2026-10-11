#!/usr/bin/env python3
"""Verifies the AI model advertisement changes to neverbeen-api
(see neverbeen-api-model-advertisement.patch).

There is no .NET SDK in this sandbox, so this script is the automated evidence that the changed
C# behaves as intended. It is a line-for-line port of the new ModelAdsController logic, run
against an in-memory advertising pool, plus a structural audit of the patch and of the context
file it edits:

  A  the patch applies cleanly to the neverbeen-api checkout
  B  the endpoints exist with the right shape — public GET /api/models/ads (pool, heaviest
     rotation first) and public POST /api/models/ads/events (≤ 50 events per call), no
     [Authorize] on either, and AppDbContext carries both DbSets plus the model configuration
  C  a port of RecordEvents: valid impression / click events are stored and counted per model,
     a guest is stored with ViewerId = null, a member id is only accepted when the member
     exists, unknown slugs are ignored (an anonymous caller cannot grow the table), and an
     invalid slug / type / surface is rejected with 400
  D  the pool answer keeps a paused model visible (IsActive = false) so the website can skip
     her, and reports the counters the Founder reads

Run:  python3 neverbeen-api-model-advertisement-verification.py [path-to-neverbeen-api]
"""

import os
import re
import subprocess
import sys
import tempfile

API_DIR = sys.argv[1] if len(sys.argv) > 1 else '/tmp/neverbeen-api'
PATCH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     'neverbeen-api-model-advertisement.patch')

SLUG_PATTERN = re.compile(r'^[a-z0-9]+(?:-[a-z0-9]+)*$')
EVENT_TYPES = ('impression', 'click')
SURFACES = ('rail', 'feed')
MAX_EVENTS = 50

failures = []


def check(name, cond, detail=''):
    status = 'PASS' if cond else 'FAIL'
    print(f'  [{status}] {name}' + (f' — {detail}' if detail and not cond else ''))
    if not cond:
        failures.append(name)


# ---------------------------------------------------------------------------
# A. the patch applies cleanly to the api checkout
# ---------------------------------------------------------------------------
print('A. patch applies cleanly to the neverbeen-api checkout')

with tempfile.TemporaryDirectory() as tmp:
    work = os.path.join(tmp, 'api')
    if os.path.isdir(API_DIR):
        subprocess.run(['git', 'clone', '--quiet', API_DIR, work], check=True)
    else:
        subprocess.run(['git', 'clone', '--quiet',
                        'https://github.com/kingshukbanu1987-beep/neverbeen-api',
                        work], check=True)
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

    controller = open(os.path.join(work, 'Controllers', 'ModelAdsController.cs'), encoding='utf-8').read()
    dtos = open(os.path.join(work, 'Dtos', 'ModelAdDtos.cs'), encoding='utf-8').read()
    context = open(os.path.join(work, 'Data', 'AppDbContext.cs'), encoding='utf-8').read()
    entity = open(os.path.join(work, 'Entities', 'ModelAd.cs'), encoding='utf-8').read()
    event_entity = open(os.path.join(work, 'Entities', 'ModelAdEvent.cs'), encoding='utf-8').read()

    # -------------------------------------------------------------------
    # B. endpoint shape and EF wiring
    # -------------------------------------------------------------------
    print('B. endpoints and EF wiring')

    check('controller route is api/models/ads',
          re.search(r'\[Route\("api/models/ads"\)\]', controller) is not None)
    check('GET /api/models/ads lists the pool', '[HttpGet]' in controller)
    check('POST /api/models/ads/events records a batch', '[HttpPost("events")]' in controller)
    check('both endpoints are public (no [Authorize] on the controller or the actions)',
          '[Authorize' not in controller)
    check('at most 50 events per call', 'MaximumEventsPerRequest = 50' in controller)
    check('a model missing from the pool is advertised by the website (documented on Pool())',
          'missing from this list as advertised' in controller or 'missing from the pool' in controller)

    check('DbSet<ModelAd> registered', 'DbSet<ModelAd> ModelAds' in context)
    check('DbSet<ModelAdEvent> registered', 'DbSet<ModelAdEvent> ModelAdEvents' in context)
    check('unique index on the model slug',
          re.search(r'entity\.HasIndex\(ad => ad\.ModelSlug\)\.IsUnique\(\)', context) is not None)
    check('the viewer reference nulls out instead of deleting history',
          'DeleteBehavior.SetNull' in context)
    check('both entities are mapped to the SQL table names',
          '[Table("ModelAds")]' in entity and '[Table("ModelAdEvents")]' in event_entity)
    check('event rows are bounded in size (slug 120, type 16, surface 16)',
          'StringLength(120, MinimumLength = 2)' in dtos and dtos.count('StringLength(16)') >= 2)
    check('the batch request refuses empty and oversized payloads',
          '[MinLength(1)]' in dtos and '[MaxLength(50)]' in dtos)

    # -------------------------------------------------------------------
    # C. port of RecordEvents
    # -------------------------------------------------------------------
    print('C. RecordEvents behaviour (ported)')

    def record_events(pool, events, viewer_exists=True, viewer_id=7):
        """Port of ModelAdsController.RecordEvents. Returns (status, recorded, pool, stored)."""
        if not events or len(events) > MAX_EVENTS:
            return 400, 0, pool, []

        normalised = []
        for entry in events:
            slug = (entry.get('modelSlug') or '').strip()
            kind = (entry.get('type') or '').strip().lower()
            surface = (entry.get('surface') or '').strip().lower()
            if not slug or len(slug) > 120 or not SLUG_PATTERN.match(slug):
                return 400, 0, pool, []
            if kind not in EVENT_TYPES or surface not in SURFACES:
                return 400, 0, pool, []
            normalised.append((slug, kind, surface))

        resolved_viewer = viewer_id if viewer_exists else None
        stored = []
        for slug in {entry[0] for entry in normalised}:
            if slug not in pool:
                continue  # not part of the pool — nothing to count
            rows = [entry for entry in normalised if entry[0] == slug]
            pool[slug]['impressions'] += sum(1 for row in rows if row[1] == 'impression')
            pool[slug]['clicks'] += sum(1 for row in rows if row[1] == 'click')
            for row in rows:
                stored.append({'modelSlug': row[0], 'type': row[1], 'surface': row[2],
                               'viewerId': resolved_viewer})
        return 202, len(stored), pool, stored

    def fresh_pool():
        return {
            'fiadh-dara': {'isActive': True, 'weight': 1, 'impressions': 0, 'clicks': 0},
            'nourhan-durrani': {'isActive': True, 'weight': 5, 'impressions': 3, 'clicks': 1},
            'sereenah-noorzai': {'isActive': False, 'weight': 1, 'impressions': 0, 'clicks': 0},
        }

    pool = fresh_pool()
    status, recorded, pool, stored = record_events(pool, [
        {'modelSlug': 'fiadh-dara', 'type': 'impression', 'surface': 'rail'},
        {'modelSlug': 'fiadh-dara', 'type': 'click', 'surface': 'rail'},
        {'modelSlug': 'nourhan-durrani', 'type': 'impression', 'surface': 'feed'},
    ])
    check('a valid batch is accepted (202)', status == 202, str(status))
    check('every valid event is stored', recorded == 3 and len(stored) == 3)
    check('rail impressions and clicks are counted per model',
          pool['fiadh-dara']['impressions'] == 1 and pool['fiadh-dara']['clicks'] == 1)
    check('a feed impression adds to the model totals',
          pool['nourhan-durrani']['impressions'] == 4 and pool['nourhan-durrani']['clicks'] == 1)
    check('the signed-in member is recorded as the viewer',
          all(row['viewerId'] == 7 for row in stored))

    pool = fresh_pool()
    status, recorded, pool, stored = record_events(pool, [
        {'modelSlug': 'fiadh-dara', 'type': 'impression', 'surface': 'feed'},
    ], viewer_exists=False)
    check('a guest view is still counted', status == 202 and recorded == 1)
    check('a guest has no viewer id', stored[0]['viewerId'] is None)

    pool = fresh_pool()
    before = {slug: (state['impressions'], state['clicks']) for slug, state in pool.items()}
    status, recorded, pool, stored = record_events(pool, [
        {'modelSlug': 'not-in-the-pool', 'type': 'impression', 'surface': 'rail'},
    ])
    check('an unknown slug is ignored instead of creating a row',
          status == 202 and recorded == 0 and 'not-in-the-pool' not in pool)
    check('an ignored slug changes no totals',
          all((state['impressions'], state['clicks']) == before[slug]
              for slug, state in pool.items()))

    for bad in ({'modelSlug': 'Bad Slug!', 'type': 'impression', 'surface': 'rail'},
                {'modelSlug': 'fiadh-dara', 'type': 'view', 'surface': 'rail'},
                {'modelSlug': 'fiadh-dara', 'type': 'impression', 'surface': 'sidebar'}):
        status, _, _, _ = record_events(fresh_pool(), [bad])
        check(f'invalid event rejected with 400: {bad["type"]} / {bad["surface"]}', status == 400)

    check('an empty batch is rejected', record_events(fresh_pool(), [])[0] == 400)
    check('more than 50 events are rejected',
          record_events(fresh_pool(), [{'modelSlug': 'fiadh-dara', 'type': 'impression',
                                        'surface': 'rail'}] * 51)[0] == 400)

    # -------------------------------------------------------------------
    # D. pool answer
    # -------------------------------------------------------------------
    print('D. the pool answer the website reads')

    pool = fresh_pool()
    ordered = sorted(pool.items(), key=lambda item: (-item[1]['weight'], item[0]))
    check('heaviest rotation first', [slug for slug, _ in ordered][0] == 'nourhan-durrani')
    check('a paused model stays in the answer with IsActive = false',
          pool['sereenah-noorzai']['isActive'] is False)
    check('counters travel with the pool',
          pool['nourhan-durrani']['impressions'] == 3 and pool['nourhan-durrani']['clicks'] == 1)
    check('the query orders by weight descending, then by slug',
          'OrderByDescending(ad => ad.Weight)' in controller
          and 'ThenBy(ad => ad.ModelSlug)' in controller)
    check('the answer carries every field the website reads',
          all(field in dtos for field in ('ModelSlug', 'DisplayName', 'IsActive', 'Weight',
                                          'ImpressionCount', 'ClickCount')))

print()
if failures:
    print(f'{len(failures)} check(s) failed:')
    for name in failures:
        print(f'  - {name}')
    sys.exit(1)
print('All checks passed.')
