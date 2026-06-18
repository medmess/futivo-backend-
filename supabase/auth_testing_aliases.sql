alter table public.profiles
add column if not exists auth_email text;

update public.profiles
set auth_email = email
where (auth_email is null or auth_email = '')
  and email is not null
  and email <> '';

create index if not exists profiles_auth_email_lower_idx
on public.profiles (lower(auth_email));

create index if not exists profiles_email_lower_idx
on public.profiles (lower(email));

create index if not exists profiles_nickname_lower_idx
on public.profiles (lower(nickname));

create or replace function public.get_login_email(login_input text)
returns text
language sql
stable
security definer
set search_path = public
as $$
  select coalesce(nullif(p.auth_email, ''), p.email)
  from public.profiles p
  where lower(coalesce(p.email, '')) = lower(trim(login_input))
     or lower(coalesce(p.auth_email, '')) = lower(trim(login_input))
     or lower(coalesce(p.nickname, '')) = lower(trim(login_input))
  limit 1;
$$;

grant execute on function public.get_login_email(text) to anon, authenticated;
