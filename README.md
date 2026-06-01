# Futivo Backend

ASP.NET Core backend for Futivo app logic. Supabase remains responsible for
authentication and database storage. This API handles algorithms and safe server
logic:

- fantasy points calculation
- official group standings calculation
- fantasy group creation / join / list
- Telegram image + caption news ingestion

## Run locally

```powershell
cd C:\flutter-projects\gfn_tv_backend
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
- `GET /api/news/latest`
- `GET /api/news/image/{fileName}`
- `GET /api/ads/news`
- `POST /api/ads/news`

Run `supabase/schema.sql` in the Supabase SQL editor when you want database
persistence for groups.
