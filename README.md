# NeverBeen Community API

.NET 7 Web API (ASP.NET Core + EF Core + **Npgsql**) for the **NeverBeen** community
website, backed by **Supabase (PostgreSQL)**. It persists every community feature:

| Area | What the API stores & serves |
|---|---|
| Sign in / sign up | OAuth SSO (Google / Facebook / Microsoft) → JWT; registration completes the profile |
| Profile | Name, gender, DOB, country/city/state, contact, About Me (8 sub-sections + per-field visibility), photos, verification, active status, profile lock, 20-digit `UniqueId` |
| Settings | Privacy & audience controls (who can message / connect / visit / see companions), journey visibility, tagging rules, notifications, 2FA, travel styles |
| Journey feed | Posts (photos, location, mood, place id, hashtags, audience public/companions/custom/only-me), shares, wall posts, nested comments, 12 reactions, tags, hide-from-feed |
| Message Book | Community posts + one level of replies, like/dislike + 10 hold reactions, tags |
| Messenger | 1:1 and group conversations, messages, replies, emoji reactions, read state / unread counts |
| Gallery | Albums (privacy, cover) + photos (bytes ≤ 100 KB or URLs) |
| Companionships | Request / accept / reject / cancel / remove, mutual companions |
| Followers | Follow / unfollow, followers & following lists, counters |
| Circles | Travel circles (owner/admins/members), archive, group chat |
| Notifications | Companionship, likes, comments, follows, tags → unread badge |
| Security | Login devices (block / remove), abuse reports, blocked users, hidden posts |
| Lookups | Countries / cities (reconciled with `Data/GeoSeedData.cs` on every start) + professions / genders |

## Solution layout

`neverbeen-api.sln` is a Visual Studio solution containing the single
`NeverBeen.API.csproj` web API project. The solution is optional for the
`dotnet` CLI, which can run the project directly.

```
NeverBeen.API/
├── Controllers/
│   ├── AuthController.cs            OAuth (SSO) login + /me
│   ├── RegistrationController.cs    completes a new member's profile (sign up)
│   ├── ProfileController.cs         profile, photo, settings
│   ├── JourneyController.cs         feed, posts, comments, reactions, tags, shares
│   ├── MessageBookController.cs     community message book
│   ├── CompanionsController.cs      companionship lifecycle + mutuals
│   ├── FollowsController.cs         followers / following
│   ├── CirclesController.cs         travel circles + circle chat
│   ├── MessagesController.cs        messenger conversations + chat
│   ├── NotificationsController.cs   notifications + unread count
│   ├── DevicesController.cs         login devices
│   ├── ModerationController.cs      abuse reports, blocks, hidden posts
│   ├── GalleryController.cs         photos (bytes)
│   ├── GalleryAlbumsController.cs   albums
│   └── LookupController.cs          countries, cities, professions, genders
├── Data/                            AppDbContext, DbInitializer, GeoSeedData
├── Entities/                        EF entities 1:1 with the Supabase tables
├── Dtos/                            request/response models + mappers
├── Services/                        JwtTokenService, OAuth providers
├── Common/                          constants, validation, helpers
└── Middleware/                      JSON error handling
```

## Run locally

1. Install the **.NET 7 SDK** (the project targets `net7.0`). Note: .NET 7 is out of
   support; upgrading the project to a supported .NET release is recommended separately.
2. **Configuration is already set** in `appsettings.json` for the live Supabase
   project (`ConnectionStrings:Supabase`), the deployed Angular site
   (`Cors:AllowedOrigins` = `https://youneverbeen.kingshukbanu1987.workers.dev`) and
   the frontend OAuth callback (`OAuth:FrontendRedirectUri`). On first start the API
   creates all tables in Supabase's `public` schema (via `CreateTablesAsync` — not
   `EnsureCreated`, which is a no-op on Supabase) and brings the Countries/Cities
   lookup tables in step with the seed. Startup requires a reachable database and will
   fail if schema creation fails.
3. Run from a terminal:

   ```bash
   dotnet restore neverbeen-api.sln
   dotnet run --project NeverBeen.API.csproj --launch-profile http
   ```

   Open **http://localhost:5080/swagger**. Swagger is enabled in every environment
   so the deployed API can be tested too. `/health` checks the database connection.

**Visual Studio:** Open `neverbeen-api.sln`, select the `http` launch profile,
then press F5. You can run without the `.sln` using the `.csproj` directly.

**Deploying to a Windows VPS / IIS and wiring the Angular site to this API:**
see [DEPLOYMENT.md](DEPLOYMENT.md) for the full step-by-step guide.

> **Security:** This repository is **public** and `appsettings.json` contains the
> Supabase database password and the JWT signing key in plain text. The database
> password has also been committed to Git history before: **rotate it in Supabase**
> (Database settings → Reset database password) and update the connection string.
> Anyone with the JWT signing key can forge login tokens — keep both secret.

## Notes

- **Lookup data (`Countries` / `Cities`).** `DbInitializer.SyncLookupsAsync()` reconciles
  the tables with `Data/GeoSeedData.cs` (184 countries, 1006 cities) on **every** start:
  countries already stored keep their ids and gain missing cities, countries that are not
  stored yet are added, and two city rows seeded under older names are renamed in place
  (`Bangalore` → `Bengaluru`, `Frankfurt` → `Frankfurt am Main`). Rows are never deleted and
  re-running adds nothing, so a database seeded from a shorter list — for example
  `neverbeen-database/seed.sql`, which ships 10 countries — is completed automatically on the
  next deploy instead of being left with only those 10. This matters because
  `POST /api/registration` and `PUT /api/profile` validate the submitted pair against the
  `Cities` row and answer *"The selected city does not belong to the selected country."*
  for any city the table does not know. After a deploy, check it with
  `GET /api/lookup/countries` (expect 184 entries) and
  `GET /api/lookup/countries/1/cities` (expect the cities of Andorra).
- Auth model: OAuth SSO issues a JWT (`POST /api/auth/oauth/login`); registration
  (`POST /api/registration`) fills the profile and flips `Users.Status`
  `Pending → Active`. Every `[Authorize]` endpoint expects `Authorization: Bearer <jwt>`.
- **First name / last name / state.** `Users.FirstName`, `Users.LastName` and `Users.State`
  are stored from the registration form: `POST /api/registration` accepts the extra
  multipart fields `firstName`, `lastName` and `state` next to `fullName`, and
  `GET /api/profile/me`, `GET /api/profile/{id}` and `PUT /api/profile` carry the same three
  fields (the registration page posts them for every new member). A name part that is not
  posted is split out of `fullName` ("Kingshuk Banu" → `Kingshuk` / `Banu`), so rows created
  by an older client still fill the columns, and a profile whose `FirstName` / `LastName`
  columns are empty is answered with the split of its full name. The OAuth login stores the
  provider's `given_name` / `family_name` on the Pending row as well.
- `Users.UniqueId` (20-digit profile URL id) is derived automatically by the
  database trigger (`8920153401` + zero-padded id).
- Photos: either `bytea` columns (max 100 KB, validated in `Common/ImageValidation.cs`)
  or URL columns (`ProfilePhotoUrl`, `CoverPhotoUrl`, `GalleryPhotos.Url`,
  `JourneyPosts.ImageUrls`) pointing at **Supabase Storage**.
- Message reaction maps (`{"👍": 2}`) and About Me details are stored as JSON
  in `text` columns; hashtag / travel-style / image lists are `text[]` arrays.
- **Complete community integration (website ↔ API).** The community pages read and
  write their data through these endpoints; the following pieces complete the last gaps:
  - `GET`/`PUT`/`DELETE /api/profile/cover` and `GET /api/profile/{id}/cover` store and
    serve the cover photograph from `Users.CoverPhotoData` (mirroring the profile photo
    endpoints). `ProfileDto.CoverPhotoUrl` points at `/api/profile/{id}/cover` when bytes
    are stored.
  - `PUT /api/profile` accepts three extra optional fields: `aboutMeDetailsJson` (the
    structured About-me sub-sections, stored verbatim in `Users.AboutMeDetailsJson`),
    `activeStatus` (validated against Active/Busy/Don't Disturb/Away/Inactive/Custom) and
    `customStatusText`; `ProfileDto` answers all three (plus the cover URL) so the profile
    page can reload its own presence and About-me editor content.
  - `PUT /api/profile/settings` now also persists the blue-tick verification
    (`isVerified`, `verificationEmail`, `verificationType`) onto the member row and its
    settings mirror.
  - `POST /api/journey` and `PUT /api/journey/{id}` accept `taggedCompanionIds`; the tags
    are applied with the post (create adds, update replaces the tag set), so a newly
    created post carries its tagged companions without extra round trips.
  - `POST /api/messagebook` accepts `imageUrl` and `CommentDto` answers it, so message
    book entries with an attached photograph are stored and re-served.
