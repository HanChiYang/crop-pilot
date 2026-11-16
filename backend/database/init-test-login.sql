-- CropPilot local test login seed.
-- Test environments only. Do not run this script in production.
-- Credentials: Farmer01 / password
-- This intentionally creates only login-related data and no auth session,
-- field, planting, inventory, irrigation, or material-application records.

BEGIN;

INSERT INTO public.main_account (id, status, security_stamp)
VALUES (
    '00000000-0000-4000-8000-000000000001',
    'ACTIVE',
    '00000000000040008000000000000001'
)
ON CONFLICT (id) DO UPDATE
SET status = EXCLUDED.status,
    security_stamp = EXCLUDED.security_stamp;

INSERT INTO public.local_credentials (
    id,
    main_account_id,
    username,
    password_hash,
    access_failed_count,
    lockout_enabled,
    lockout_end,
    concurrency_stamp
)
VALUES (
    '00000000-0000-4000-8000-000000000002',
    '00000000-0000-4000-8000-000000000001',
    'Farmer01',
    'AQAAAAIAAYagAAAAEJ6vakqgwRshZatnLt8A4fgyTFHUzggDur6OA4qNj9sS5y2ZWqUDCkbDHOn4caYR2A==',
    0,
    TRUE,
    NULL,
    '00000000-0000-4000-8000-000000000002'
)
ON CONFLICT (id) DO UPDATE
SET username = EXCLUDED.username,
    password_hash = EXCLUDED.password_hash,
    access_failed_count = EXCLUDED.access_failed_count,
    lockout_enabled = EXCLUDED.lockout_enabled,
    lockout_end = EXCLUDED.lockout_end,
    concurrency_stamp = EXCLUDED.concurrency_stamp;

INSERT INTO public.user_profiles (
    id,
    main_account_id,
    email,
    display_name,
    surname,
    given_name
)
VALUES (
    '00000000-0000-4000-8000-000000000003',
    '00000000-0000-4000-8000-000000000001',
    NULL,
    NULL,
    NULL,
    NULL
)
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.user_roles (id, main_account_id, role)
VALUES (
    '00000000-0000-4000-8000-000000000004',
    '00000000-0000-4000-8000-000000000001',
    'USER'
)
ON CONFLICT (id) DO UPDATE
SET role = EXCLUDED.role;

COMMIT;
