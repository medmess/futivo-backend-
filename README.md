# Futivo Backend

ASP.NET Core backend for the Futivo football and fantasy platform.

## Project structure

- Root folder: ASP.NET Core API used by Render.
- `Models/`: request/response and domain DTOs.
- `Services/`: fantasy, news, groups, ads, matches and Supabase services.
- `supabase/schema.sql`: database schema changes to run manually in Supabase.

Supabase remains responsible for authentication and database storage. The ASP.NET
Core API handles algorithms and safe server logic:

- fantasy points calculation
- official group standings calculation
- fantasy group creation / join / list
- Telegram image + caption news ingestion
- news ads and admin-ready news management

## Run locally

```powershell
cd C:\flutter-projects\fantasy_backend_repo
dotnet run
```

Without Supabase settings, group endpoints use in-memory storage for safe local
testing and do not affect any database.

## Supabase configuration

Set these as environment variables or in user secrets before production use:

```powershell
dotnet user-secrets set "Supabase:Url" "https://YOUR_PROJECT.supabase.co"
dotnet user-secrets set "Supabase:AnonKey" "YOUR_SUPABASE_ANON_KEY"
dotnet user-secrets set "Supabase:ServiceRoleKey" "YOUR_SUPABASE_SERVICE_ROLE_KEY"
```

The Flutter app should send the Supabase access token as:

```http
Authorization: Bearer <supabase_access_token>
```

## Endpoints

- `GET /health`
- `POST /api/fantasy/calculate-points`
- `POST /api/standings/calculate`
- `POST /api/groups/create`
- `POST /api/groups/join`
- `GET /api/groups/mine`
- `POST /api/news/telegram`
- `POST /api/news/admin`
- `GET /api/news/latest`
- `DELETE /api/news/telegram/{telegramPostId}`
- `GET /api/news/image/{fileName}`
- `GET /api/ads/news`
- `POST /api/ads/news`
- `GET /api/matches/{matchId}/manual`
- `POST /api/admin/matches/manual`

Run `supabase/schema.sql` in the Supabase SQL editor when you want database
persistence for groups.
