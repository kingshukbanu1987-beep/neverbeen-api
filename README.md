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
| Lookups | Countries / cities (seeded at first start) + professions / genders |

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

1. Install the **.NET 7 SDK** (the project targets `net7.0`) and PostgreSQL,
   or use a Supabase PostgreSQL database. Note: .NET 7 is out of support;
   upgrading the project to a supported .NET release is recommended separately.
2. Create an **empty** PostgreSQL database named `neverbeen` (for example,
   `createdb -U postgres neverbeen`). On startup, the API uses EF Core
   `EnsureCreated` when no migrations exist, then seeds countries/cities.
   This checkout does **not** contain the previously mentioned sibling
   `NeverBeen.Database/schema.sql`; use a fresh empty database for this path.
3. From the repository root, configure local credentials using .NET user secrets
   (stored outside the repository). Replace the example password and signing key
   with your own values:

   ```bash
   dotnet user-secrets set "ConnectionStrings:Supabase" 'Host=localhost;Port=5432;Database=neverbeen;Username=postgres;Password=YOUR_LOCAL_PASSWORD;SSL Mode=Prefer'
   dotnet user-secrets set "Jwt:SigningKey" 'YOUR_RANDOM_SECRET_AT_LEAST_32_BYTES_LONG'
   ```

   For Supabase instead, set `ConnectionStrings:Supabase` to your project's
   PostgreSQL connection string (with its actual host, credentials and SSL
   settings). Never commit credentials to `appsettings*.json`. If you need OAuth
   login, also set `OAuth:Google`, `OAuth:Facebook` or `OAuth:Microsoft` client
   settings and redirect URIs; the API can start without OAuth credentials.
   Configure `Cors:AllowedOrigins` for any other frontend origin.
4. Run from a terminal:

   ```bash
   dotnet restore neverbeen-api.sln
   dotnet run --project NeverBeen.API.csproj --launch-profile http
   ```

   Open **http://localhost:5080/swagger**. Swagger is only enabled in
   Development (the `http` launch profile sets that environment). `/health`
   checks the database connection. Startup requires a reachable database and
   will fail if schema creation or seeding fails.

**Visual Studio:** Open `neverbeen-api.sln`, select the `http` launch profile,
then press F5. The user secrets above are also read when running in Development
from Visual Studio. You can run without the `.sln` using the `.csproj` directly.

A database password was previously checked into `appsettings.json`. Removing
it from the current file does not remove it from Git history: **rotate that
password in the database** before using it again.

## Notes

- Auth model: OAuth SSO issues a JWT (`POST /api/auth/oauth/login`); registration
  (`POST /api/registration`) fills the profile and flips `Users.Status`
  `Pending → Active`. Every `[Authorize]` endpoint expects `Authorization: Bearer <jwt>`.
- `Users.UniqueId` (20-digit profile URL id) is derived automatically by the
  database trigger (`8920153401` + zero-padded id).
- Photos: either `bytea` columns (max 100 KB, validated in `Common/ImageValidation.cs`)
  or URL columns (`ProfilePhotoUrl`, `CoverPhotoUrl`, `GalleryPhotos.Url`,
  `JourneyPosts.ImageUrls`) pointing at **Supabase Storage**.
- Message reaction maps (`{"👍": 2}`) and About Me details are stored as JSON
  in `text` columns; hashtag / travel-style / image lists are `text[]` arrays.
