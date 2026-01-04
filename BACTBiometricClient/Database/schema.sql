-- BACT Biometric Desktop Client - SQLite Database Schema
-- Created: December 2025
-- Purpose: Offline-first biometric registration and verification

-- ==========================================
-- TABLE 1: cached_students
-- Stores downloaded student data for offline access
-- ==========================================
CREATE TABLE IF NOT EXISTS cached_students (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL,
    roll_number TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    father_name TEXT,
    cnic TEXT,
    gender TEXT,
    
    -- Images stored as BLOB
    picture BLOB,
    test_photo BLOB,
    
    -- Test and venue information
    test_id INTEGER,
    test_name TEXT,
    college_id INTEGER,
    college_name TEXT,
    venue TEXT,
    hall TEXT,
    zone TEXT,
    row TEXT,
    seat TEXT,
    
    -- Biometric data
    fingerprint_template TEXT,
    fingerprint_image BLOB,
    fingerprint_quality INTEGER,
    
    -- Sync tracking
    sync_status TEXT DEFAULT 'pending',
    last_synced_at TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_roll_number ON cached_students(roll_number);
CREATE INDEX idx_student_id ON cached_students(student_id);
CREATE INDEX idx_test_id ON cached_students(test_id);
CREATE INDEX idx_sync_status ON cached_students(sync_status);

-- ==========================================
-- TABLE 2: pending_registrations
-- Queue for fingerprint registrations waiting to sync
-- ==========================================
CREATE TABLE IF NOT EXISTS pending_registrations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL,
    roll_number TEXT NOT NULL,
    fingerprint_template TEXT NOT NULL,
    fingerprint_image BLOB,
    quality_score INTEGER,
    captured_at TEXT NOT NULL,
    operator_id INTEGER,
    operator_name TEXT,
    
    -- Sync tracking
    sync_attempts INTEGER DEFAULT 0,
    sync_status TEXT DEFAULT 'pending',
    last_sync_attempt TEXT,
    sync_error TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_pending_reg_status ON pending_registrations(sync_status);
CREATE INDEX idx_pending_reg_roll ON pending_registrations(roll_number);

-- ==========================================
-- TABLE 3: pending_verifications
-- Queue for verification logs waiting to sync
-- ==========================================
CREATE TABLE IF NOT EXISTS pending_verifications (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL,
    roll_number TEXT NOT NULL,
    match_result TEXT NOT NULL,
    confidence_score REAL,
    entry_allowed INTEGER DEFAULT 0,
    verified_at TEXT NOT NULL,
    verifier_id INTEGER,
    verifier_name TEXT,
    notes TEXT,
    
    -- Sync tracking
    sync_attempts INTEGER DEFAULT 0,
    sync_status TEXT DEFAULT 'pending',
    last_sync_attempt TEXT,
    sync_error TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_pending_ver_status ON pending_verifications(sync_status);
CREATE INDEX idx_pending_ver_roll ON pending_verifications(roll_number);

-- ==========================================
-- TABLE 4: cached_tests
-- Test information for offline access
-- ==========================================
CREATE TABLE IF NOT EXISTS cached_tests (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    test_id INTEGER NOT NULL UNIQUE,
    test_name TEXT NOT NULL,
    test_date TEXT,
    test_time TEXT,
    status TEXT,
    total_students INTEGER DEFAULT 0,
    synced_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_test_id ON cached_tests(test_id);

-- ==========================================
-- TABLE 5: app_settings
-- Application configuration and preferences
-- ==========================================
CREATE TABLE IF NOT EXISTS app_settings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    key TEXT NOT NULL UNIQUE,
    value TEXT,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
);

-- Default settings
INSERT OR IGNORE INTO app_settings (key, value) VALUES 
    ('api_url', 'https://your-domain.com/api'),
    ('quality_threshold', '50'),
    ('auto_sync_enabled', 'true'),
    ('sync_interval_minutes', '5'),
    ('scanner_timeout_seconds', '30'),
    ('match_threshold', '70'),
    ('last_login_email', ''),
    ('remember_credentials', 'false');

-- ==========================================
-- TABLE 6: sync_logs
-- History of sync operations
-- ==========================================
CREATE TABLE IF NOT EXISTS sync_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sync_type TEXT NOT NULL,
    direction TEXT NOT NULL,
    records_count INTEGER DEFAULT 0,
    success_count INTEGER DEFAULT 0,
    failed_count INTEGER DEFAULT 0,
    error_message TEXT,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    duration_seconds REAL
);

CREATE INDEX idx_sync_started ON sync_logs(started_at);
CREATE INDEX idx_sync_type ON sync_logs(sync_type);

-- ==========================================
-- TABLE 7: error_logs
-- Application error tracking
-- ==========================================
CREATE TABLE IF NOT EXISTS error_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    error_type TEXT NOT NULL,
    error_message TEXT NOT NULL,
    stack_trace TEXT,
    user_id INTEGER,
    occurred_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_error_occurred ON error_logs(occurred_at);

-- ==========================================
-- TABLE 8: user_sessions
-- Track logged-in user sessions
-- ==========================================
CREATE TABLE IF NOT EXISTS user_sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    email TEXT NOT NULL,
    role TEXT NOT NULL,
    token TEXT NOT NULL,
    token_expires_at TEXT,
    logged_in_at TEXT DEFAULT CURRENT_TIMESTAMP,
    last_activity_at TEXT DEFAULT CURRENT_TIMESTAMP,
    is_active INTEGER DEFAULT 1
);

CREATE INDEX idx_user_sessions_active ON user_sessions(is_active);

-- ==========================================
-- VIEWS for reporting
-- ==========================================

-- Pending sync summary
CREATE VIEW IF NOT EXISTS v_pending_sync_summary AS
SELECT 
    (SELECT COUNT(*) FROM pending_registrations WHERE sync_status = 'pending') as pending_registrations,
    (SELECT COUNT(*) FROM pending_verifications WHERE sync_status = 'pending') as pending_verifications,
    (SELECT MAX(last_synced_at) FROM cached_students) as last_sync_time;

-- Student summary with fingerprint status
CREATE VIEW IF NOT EXISTS v_student_summary AS
SELECT 
    s.student_id,
    s.roll_number,
    s.name,
    s.test_name,
    s.college_name,
    s.venue,
    s.hall,
    CASE 
        WHEN s.fingerprint_template IS NOT NULL THEN 'Registered'
        ELSE 'Not Registered'
    END as fingerprint_status,
    s.sync_status,
    s.last_synced_at
FROM cached_students s;

-- ==========================================
-- Database version tracking
-- ==========================================
CREATE TABLE IF NOT EXISTS db_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO db_version (version) VALUES (1);

-- ==========================================
-- END OF SCHEMA
-- ==========================================