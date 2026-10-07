#!/usr/bin/env python3
"""Verifies the Countries/Cities lookup-sync logic added to neverbeen-api's
DbInitializer.SyncLookupsAsync() (see neverbeen-api-lookup-sync.patch).

It is a line-for-line port of the C# method, run against the real
Data/GeoSeedData.cs (184 countries / 1006 cities) and the cities written by
neverbeen-database/seed.sql (10 countries / 24 cities). There is no .NET SDK in this
sandbox, so this is the automated evidence that the sync reaches the intended state:

  A  deployed Supabase state (seed.sql)  -> 184 countries / 1006 cities, ids preserved
                                            (India 1, Kolkata 1, Bangalore 4 -> Bengaluru),
                                            second run adds nothing (idempotent)
  B  a database the API seeded itself    -> no-op
  C  an empty database                   -> full seed
  D  legacy + current city name present  -> no rename, no duplicate, no deletion

Run:  python3 neverbeen-api-lookup-sync-verification.py
"""

import re, json, unicodedata

geo = json.loads(re.search(r'GeoJson = """(.*?)"""',
      open('/home/user/neverbeen-api/Data/GeoSeedData.cs', encoding='utf-8').read(), re.S).group(1))

def normalize(name):                       # mirrors C# Normalize()
    decomposed = unicodedata.normalize('NFD', name)
    letters = ''.join(ch for ch in decomposed if unicodedata.category(ch) != 'Mn')
    key = ''.join(ch.lower() if ch.isalnum() else ' ' for ch in letters)
    return ' '.join(key.split())

LEGACY_CITY_RENAMES = (("DE", "Frankfurt", "Frankfurt am Main"), ("IN", "Bangalore", "Bengaluru"))

class Country:
    def __init__(self, id, iso2, name, phone=None):
        self.id, self.iso2, self.name, self.phone = id, iso2, name, phone
        self.cities = []                   # list of City

class City:
    def __init__(self, id, country_id, name):
        self.id, self.country_id, self.name = id, country_id, name

def seed_sql_state():
    """The deployed Supabase state: neverbeen-database/seed.sql, identity ids 1..10 / 1..24."""
    rows = [('IN','India','+91',['Kolkata','Mumbai','Delhi','Bangalore','Chennai']),
            ('BD','Bangladesh','+880',['Dhaka','Chittagong']),
            ('PK','Pakistan','+92',['Karachi','Lahore']),
            ('US','United States','+1',['New York','San Francisco','Chicago']),
            ('GB','United Kingdom','+44',['London','Manchester']),
            ('FR','France','+33',['Paris','Nice']),
            ('DE','Germany','+49',['Berlin','Frankfurt']),
            ('JP','Japan','+81',['Tokyo','Osaka']),
            ('AU','Australia','+61',['Sydney','Melbourne']),
            ('MY','Malaysia','+60',['George Town','Kuala Lumpur'])]
    countries, city_id = [], 1
    for i, (iso2, name, phone, cities) in enumerate(rows, start=1):
        c = Country(i, iso2, name, phone)
        for city in cities:
            c.cities.append(City(city_id, i, city)); city_id += 1
        countries.append(c)
    return countries

def fresh_api_state():                     # a database the API seeded itself
    countries, cid, city_id = [], 1, 1
    for s in geo:
        c = Country(cid, s['iso2'], s['name'], s.get('phone'))
        for city in s['cities']:
            c.cities.append(City(city_id, cid, city)); city_id += 1
        countries.append(c); cid += 1
    return countries

def sync(countries):
    """Line-for-line port of SyncLookupsAsync()."""
    by_iso = {c.iso2: c for c in countries}
    by_name = {normalize(c.name): c for c in countries}
    added_countries = added_cities = renamed_cities = 0

    for seeded in geo:
        country = by_iso.get(seeded['iso2']) or by_name.get(normalize(seeded['name']))
        if country is None:
            country = Country(0, seeded['iso2'], seeded['name'], seeded.get('phone'))
            country.cities = [City(0, 0, city) for city in seeded['cities']]
            countries.append(country)
            by_iso[seeded['iso2']] = country
            by_name[normalize(seeded['name'])] = country
            added_countries += 1
            added_cities += len(seeded['cities'])
            continue

        stored = {normalize(city.name): city for city in country.cities}
        for iso2, frm, to in LEGACY_CITY_RENAMES:
            if iso2.lower() != seeded['iso2'].lower():
                continue
            from_key, to_key = normalize(frm), normalize(to)
            if from_key in stored and to_key not in stored:
                legacy = stored.pop(from_key)
                legacy.name = to
                stored[to_key] = legacy
                renamed_cities += 1

        for city in seeded['cities']:
            key = normalize(city)
            if key in stored:
                continue
            entity = City(0, country.id, city)
            country.cities.append(entity)
            stored[key] = entity
            added_cities += 1
    return added_countries, added_cities, renamed_cities

def check(countries, label, expect_total=1006):
    """The invariants POST /api/registration relies on."""
    assert len(countries) == len(geo) == 184, f"{label}: countries {len(countries)}"
    total = sum(len(c.cities) for c in countries)
    assert total == expect_total, f"{label}: cities {total}"
    for seeded in geo:
        country = next(c for c in countries if c.iso2 == seeded['iso2'])
        names = {normalize(x.name) for x in country.cities}
        for city in seeded['cities']:
            assert normalize(city) in names, f"{label}: {seeded['iso2']} missing {city}"
        assert all(x.country_id == country.id for x in country.cities), f"{label}: cross-country city"
    for country in countries:                                   # UX_Cities_Country_Name
        keys = [normalize(x.name) for x in country.cities]
        assert len(keys) == len(set(keys)), f"{label}: duplicate city in {country.name}"
    iso = [c.iso2 for c in countries]                           # UX_Countries_IsoCode2
    assert len(iso) == len(set(iso)), f"{label}: duplicate ISO code"

print("Scenario A: deployed Supabase state (seed.sql: 10 countries / 24 cities)")
db = seed_sql_state()
before = {c.iso2: (c.id, [ (x.id, x.name) for x in c.cities ]) for c in db}
print("   sync #1 ->", sync(db), "(added countries, cities, renames)")
check(db, "A")
in_ = next(c for c in db if c.iso2 == 'IN')
print("   India keeps id", in_.id, "| Kolkata keeps id",
      next(x.id for x in in_.cities if x.name == 'Kolkata'))
print("   renamed rows keep ids:",
      [(x.id, x.name) for x in in_.cities if x.name == 'Bengaluru'],
      [(x.id, x.name) for x in next(c for c in db if c.iso2 == 'DE').cities if x.name.startswith('Frankfurt')])
print("   sync #2 (idempotency) ->", sync(db))
print("   sync #3 (idempotency) ->", sync(db))
check(db, "A-again")

print("\nScenario B: a database the API seeded itself (184 / 1006)")
db = fresh_api_state()
print("   sync ->", sync(db))
check(db, "B")

print("\nScenario C: empty database")
db = []
print("   sync ->", sync(db))
check(db, "C")

print("\nScenario D: seed.sql database that already has both legacy and current names")
db = seed_sql_state()
next(c for c in db if c.iso2 == 'IN').cities.append(City(999, 1, 'Bengaluru'))
next(c for c in db if c.iso2 == 'DE').cities.append(City(998, 7, 'Frankfurt am Main'))
print("   sync ->", sync(db), "(no rename: the current row already exists)")
check(db, "D", expect_total=1008)  # the 2 extra fixture rows are kept (rows are never deleted)

print("\nALL SCENARIOS PASS")
