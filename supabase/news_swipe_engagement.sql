alter table public.news_posts
add column if not exists is_featured boolean not null default false;

create index if not exists news_posts_featured_published_at_idx
on public.news_posts (is_featured desc, published_at desc);

create table if not exists public.news_reactions (
  article_id text not null,
  user_id uuid not null references auth.users(id) on delete cascade,
  reaction smallint not null check (reaction in (-1, 1)),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  primary key (article_id, user_id)
);

create index if not exists news_reactions_article_id_idx
on public.news_reactions (article_id);

alter table public.news_reactions enable row level security;

drop policy if exists "news reactions are readable"
on public.news_reactions;

create policy "news reactions are readable"
on public.news_reactions
for select
using (true);

drop policy if exists "users manage own news reactions"
on public.news_reactions;

create policy "users manage own news reactions"
on public.news_reactions
for all
using (auth.uid() = user_id)
with check (auth.uid() = user_id);

create table if not exists public.news_comments (
  id uuid primary key default gen_random_uuid(),
  article_id text not null,
  user_id uuid not null references auth.users(id) on delete cascade,
  author_name text not null default 'Futivo fan',
  body text not null check (char_length(trim(body)) between 1 and 500),
  created_at timestamptz not null default now()
);

create index if not exists news_comments_article_created_idx
on public.news_comments (article_id, created_at desc);

alter table public.news_comments enable row level security;

drop policy if exists "news comments are readable"
on public.news_comments;

create policy "news comments are readable"
on public.news_comments
for select
using (true);

drop policy if exists "users create own news comments"
on public.news_comments;

create policy "users create own news comments"
on public.news_comments
for insert
with check (auth.uid() = user_id);

drop policy if exists "users delete own news comments"
on public.news_comments;

create policy "users delete own news comments"
on public.news_comments
for delete
using (auth.uid() = user_id);
