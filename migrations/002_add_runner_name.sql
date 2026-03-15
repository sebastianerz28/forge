-- Add human-readable name to runners for GitHub label visibility
ALTER TABLE runners ADD COLUMN name TEXT NOT NULL DEFAULT '';
