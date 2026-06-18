alter table public.news_posts
add column if not exists expires_at timestamptz;

alter table public.profiles
add column if not exists favorite_team text;

create index if not exists news_posts_expires_at_idx
on public.news_posts (expires_at);

drop policy if exists "news posts are readable" on public.news_posts;

create policy "news posts are readable"
on public.news_posts
for select
using (
  moderation_status = 'approved'
  and (expires_at is null or expires_at > now())
);
