create table if not exists public.fantasy_groups (
  id text primary key,
  code text not null unique,
  name text not null,
  owner_user_id uuid not null references auth.users(id) on delete cascade,
  members integer not null default 1,
  max_members integer not null default 7,
  created_at timestamptz not null default now()
);

create table if not exists public.fantasy_group_members (
  group_id text not null references public.fantasy_groups(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  joined_at timestamptz not null default now(),
  primary key (group_id, user_id)
);

alter table public.fantasy_groups enable row level security;
alter table public.fantasy_group_members enable row level security;

create policy "members can read groups"
on public.fantasy_groups
for select
using (
  exists (
    select 1
    from public.fantasy_group_members m
    where m.group_id = id and m.user_id = auth.uid()
  )
);

create policy "members can read memberships"
on public.fantasy_group_members
for select
using (user_id = auth.uid());

create table if not exists public.news_posts (
  id text primary key,
  telegram_post_id bigint not null unique,
  caption text not null,
  image_path text not null,
  image_url text,
  source text not null default 'Offside',
  moderation_status text not null default 'approved'
    check (moderation_status in ('pending', 'approved', 'rejected')),
  published_at timestamptz not null,
  created_at timestamptz not null default now(),
  reviewed_at timestamptz
);

alter table public.news_posts
add column if not exists moderation_status text not null default 'approved'
check (moderation_status in ('pending', 'approved', 'rejected'));

alter table public.news_posts
add column if not exists reviewed_at timestamptz;

update public.news_posts
set moderation_status = 'approved',
    reviewed_at = coalesce(reviewed_at, now())
where moderation_status is null
   or moderation_status not in ('pending', 'approved', 'rejected');

create index if not exists news_posts_published_at_idx
on public.news_posts (published_at desc);

create index if not exists news_posts_moderation_published_at_idx
on public.news_posts (moderation_status, published_at desc);

alter table public.news_posts enable row level security;

drop policy if exists "news posts are readable" on public.news_posts;

create policy "news posts are readable"
on public.news_posts
for select
using (moderation_status = 'approved');

create table if not exists public.news_ads (
  id text primary key,
  title text not null,
  subtitle text,
  image_url text not null,
  target_url text,
  placement text not null default 'news_feed',
  is_active boolean not null default true,
  created_at timestamptz not null default now()
);

create index if not exists news_ads_active_created_at_idx
on public.news_ads (is_active, created_at desc);

alter table public.news_ads enable row level security;

create policy "active news ads are readable"
on public.news_ads
for select
using (is_active = true);

create table if not exists public.manual_match_details (
  match_id text primary key,
  home_team text not null,
  away_team text not null,
  home_formation text,
  away_formation text,
  live_stream_url text,
  home_lineup jsonb not null default '[]'::jsonb,
  away_lineup jsonb not null default '[]'::jsonb,
  events jsonb not null default '[]'::jsonb,
  updated_at timestamptz not null default now()
);

alter table public.manual_match_details enable row level security;

create policy "manual match details are readable"
on public.manual_match_details
for select
using (true);

create table if not exists public.match_predictions (
  id text primary key,
  user_id uuid not null references auth.users(id) on delete cascade,
  match_id text not null,
  home_team text not null default '',
  away_team text not null default '',
  home_score integer not null check (home_score >= 0 and home_score <= 30),
  away_score integer not null check (away_score >= 0 and away_score <= 30),
  kickoff timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, match_id)
);

create index if not exists match_predictions_user_updated_idx
on public.match_predictions (user_id, updated_at desc);

create index if not exists match_predictions_match_id_idx
on public.match_predictions (match_id);

alter table public.match_predictions enable row level security;

drop policy if exists "users can read own match predictions"
on public.match_predictions;
create policy "users can read own match predictions"
on public.match_predictions
for select
using (auth.uid() = user_id);

drop policy if exists "users can insert own match predictions"
on public.match_predictions;
create policy "users can insert own match predictions"
on public.match_predictions
for insert
with check (auth.uid() = user_id);

drop policy if exists "users can update own match predictions"
on public.match_predictions;
create policy "users can update own match predictions"
on public.match_predictions
for update
using (auth.uid() = user_id)
with check (auth.uid() = user_id);

drop policy if exists "users can delete own match predictions"
on public.match_predictions;
create policy "users can delete own match predictions"
on public.match_predictions
for delete
using (auth.uid() = user_id);
