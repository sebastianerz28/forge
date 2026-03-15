-- Factory Runner Schema
-- Coordination layer for distributed Claude Code agents

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TYPE runner_status AS ENUM ('active', 'dead');
CREATE TYPE task_status AS ENUM (
    'pending', 'claimed', 'pr_opened',
    'in_review', 'addressing_review',
    'done', 'failed'
);
CREATE TYPE run_type AS ENUM ('initial', 'review_address');

-- Runners: each machine/process registers itself here
CREATE TABLE runners (
    id UUID PRIMARY KEY,
    hostname TEXT NOT NULL,
    last_heartbeat TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    registered_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    status runner_status NOT NULL DEFAULT 'active'
);

-- Tasks: one row per GitHub issue the factory is tracking
CREATE TABLE tasks (
    id SERIAL PRIMARY KEY,
    github_issue_number INTEGER NOT NULL,
    github_repo TEXT NOT NULL,
    status task_status NOT NULL DEFAULT 'pending',
    claimed_by UUID REFERENCES runners(id),
    claimed_at TIMESTAMP WITH TIME ZONE,
    pr_number INTEGER,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    UNIQUE (github_issue_number, github_repo)
);

CREATE INDEX idx_tasks_status ON tasks(status);
CREATE INDEX idx_tasks_claimed_by ON tasks(claimed_by);

-- Runs: every executor invocation (initial or review) gets logged here
CREATE TABLE runs (
    id SERIAL PRIMARY KEY,
    task_id INTEGER NOT NULL REFERENCES tasks(id),
    runner_id UUID NOT NULL REFERENCES runners(id),
    run_type run_type NOT NULL,
    started_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    finished_at TIMESTAMP WITH TIME ZONE,
    duration_seconds DOUBLE PRECISION,
    token_usage JSONB,
    success BOOLEAN,
    error_message TEXT,
    prompt_hash TEXT
);

CREATE INDEX idx_runs_task_id ON runs(task_id);
CREATE INDEX idx_runs_runner_id ON runs(runner_id);
CREATE INDEX idx_runs_started_at ON runs(started_at);
