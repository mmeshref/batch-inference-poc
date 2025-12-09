-- Migration script to add WasEscalated column to the requests table
-- Run this script against your PostgreSQL database to add the new column
-- This script is idempotent and will skip if the table doesn't exist yet

-- Check if the requests table exists before attempting to alter it
DO $$
BEGIN
    IF EXISTS (
        SELECT FROM information_schema.tables 
        WHERE table_schema = 'public' 
        AND table_name = 'requests'
    ) THEN
        -- Add WasEscalated column (boolean, default false)
        -- This tracks whether a request was escalated from spot to dedicated pool
        ALTER TABLE requests 
        ADD COLUMN IF NOT EXISTS "WasEscalated" BOOLEAN NOT NULL DEFAULT false;

        RAISE NOTICE 'Migration applied: Added WasEscalated column to requests table';
    ELSE
        RAISE NOTICE 'Skipping migration: requests table does not exist yet. Schema will be created by EF Core on first application startup.';
    END IF;
END $$;

-- Verify the column was added (only if table exists)
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'requests' 
  AND column_name = 'WasEscalated'
ORDER BY column_name;
