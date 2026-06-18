create table if not exists public.fantasy_squads (
  user_id uuid primary key references auth.users(id) on delete cascade,
  players jsonb not null default '[]'::jsonb,
  captain_id text null,
  vice_captain_id text null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index if not exists fantasy_squads_updated_at_idx
on public.fantasy_squads(updated_at desc);

alter table public.fantasy_squads enable row level security;

drop policy if exists "Users can read their own fantasy squad"
on public.fantasy_squads;

create policy "Users can read their own fantasy squad"
on public.fantasy_squads
for select
to authenticated
using (auth.uid() = user_id);

drop policy if exists "Users can insert their own fantasy squad"
on public.fantasy_squads;

create policy "Users can insert their own fantasy squad"
on public.fantasy_squads
for insert
to authenticated
with check (auth.uid() = user_id);

drop policy if exists "Users can update their own fantasy squad"
on public.fantasy_squads;

create policy "Users can update their own fantasy squad"
on public.fantasy_squads
for update
to authenticated
using (auth.uid() = user_id)
with check (auth.uid() = user_id);
