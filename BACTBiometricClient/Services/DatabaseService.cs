using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using BACTBiometricClient.Models;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Handles all SQLite database operations for offline storage
    /// </summary>
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public DatabaseService()
        {
            // Database path in AppData/Local folder
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "BACTBiometric");

            // Create directory if not exists
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            _dbPath = Path.Combine(appFolder, "bact_biometric.db");
            _connectionString = $"Data Source={_dbPath}";
        }

        /// <summary>
        /// Initialize database by creating tables if they don't exist
        /// </summary>
        public void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Read schema from embedded resource or file
            string schema = GetDatabaseSchema();

            using var command = connection.CreateCommand();
            command.CommandText = schema;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get database schema SQL
        /// </summary>
        private string GetDatabaseSchema()
        {
            // For now, return inline schema. Later we can read from file.
            return @"
                CREATE TABLE IF NOT EXISTS cached_students (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    student_id INTEGER NOT NULL,
                    roll_number TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    father_name TEXT,
                    cnic TEXT,
                    gender TEXT,
                    picture BLOB,
                    test_photo BLOB,
                    test_id INTEGER,
                    test_name TEXT,
                    college_id INTEGER,
                    college_name TEXT,
                    venue TEXT,
                    hall TEXT,
                    zone TEXT,
                    row TEXT,
                    seat TEXT,
                    fingerprint_template TEXT,
                    fingerprint_image BLOB,
                    fingerprint_quality INTEGER,
                    sync_status TEXT DEFAULT 'pending',
                    last_synced_at TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS idx_roll_number ON cached_students(roll_number);
                CREATE INDEX IF NOT EXISTS idx_student_id ON cached_students(student_id);

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
                    sync_attempts INTEGER DEFAULT 0,
                    sync_status TEXT DEFAULT 'pending',
                    last_sync_attempt TEXT,
                    sync_error TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

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
                    sync_attempts INTEGER DEFAULT 0,
                    sync_status TEXT DEFAULT 'pending',
                    last_sync_attempt TEXT,
                    sync_error TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS app_settings (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    key TEXT NOT NULL UNIQUE,
                    value TEXT,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                INSERT OR IGNORE INTO app_settings (key, value) VALUES 
                    ('api_url', 'http://localhost:8000/api'),
                    ('quality_threshold', '50'),
                    ('auto_sync_enabled', 'true'),
                    ('sync_interval_minutes', '5'),
                    ('scanner_timeout_seconds', '30'),
                    ('match_threshold', '70'),
                    ('last_login_email', ''),
                    ('remember_credentials', 'false'),
                    ('inactivity_timeout_minutes', '30');

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

                CREATE TABLE IF NOT EXISTS error_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    error_type TEXT NOT NULL,
                    error_message TEXT NOT NULL,
                    stack_trace TEXT,
                    user_id INTEGER,
                    occurred_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
            ";
        }

        #region Student Operations

        /// <summary>
        /// Save or update a student in cache
        /// </summary>
        public void SaveStudent(Student student)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO cached_students (
                    student_id, roll_number, name, father_name, cnic, gender,
                    picture, test_photo, test_id, test_name, college_id, college_name,
                    venue, hall, zone, row, seat,
                    fingerprint_template, fingerprint_image, fingerprint_quality,
                    sync_status, updated_at
                ) VALUES (
                    @student_id, @roll_number, @name, @father_name, @cnic, @gender,
                    @picture, @test_photo, @test_id, @test_name, @college_id, @college_name,
                    @venue, @hall, @zone, @row, @seat,
                    @fingerprint_template, @fingerprint_image, @fingerprint_quality,
                    @sync_status, @updated_at
                )
                ON CONFLICT(roll_number) DO UPDATE SET
                    name = @name,
                    father_name = @father_name,
                    picture = @picture,
                    test_photo = @test_photo,
                    fingerprint_template = @fingerprint_template,
                    fingerprint_image = @fingerprint_image,
                    fingerprint_quality = @fingerprint_quality,
                    sync_status = @sync_status,
                    updated_at = @updated_at
            ";

            command.Parameters.AddWithValue("@student_id", student.StudentId);
            command.Parameters.AddWithValue("@roll_number", student.RollNumber);
            command.Parameters.AddWithValue("@name", student.Name);
            command.Parameters.AddWithValue("@father_name", student.FatherName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@cnic", student.CNIC ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@gender", student.Gender ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@picture", student.Picture ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@test_photo", student.TestPhoto ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@test_id", student.TestId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@test_name", student.TestName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@college_id", student.CollegeId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@college_name", student.CollegeName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@venue", student.Venue ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@hall", student.Hall ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@zone", student.Zone ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@row", student.Row ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@seat", student.Seat ?? (object)DBNull.Value);

            // Convert byte[] to base64 string for storage
            command.Parameters.AddWithValue("@fingerprint_template",
                student.FingerprintTemplate != null
                    ? Convert.ToBase64String(student.FingerprintTemplate)
                    : (object)DBNull.Value);

            command.Parameters.AddWithValue("@fingerprint_image", student.FingerprintImage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fingerprint_quality", student.FingerprintQuality ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sync_status", student.SyncStatus ?? "pending");
            command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get student by roll number
        /// </summary>
        public Student GetStudentByRollNumber(string rollNumber)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM cached_students WHERE roll_number = @roll_number";
            command.Parameters.AddWithValue("@roll_number", rollNumber);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return MapStudent(reader);
            }

            return null;
        }

        /// <summary>
        /// Search students by name or roll number
        /// </summary>
        public List<Student> SearchStudents(string searchTerm)
        {
            var students = new List<Student>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM cached_students 
                WHERE name LIKE @search OR roll_number LIKE @search
                LIMIT 50
            ";
            command.Parameters.AddWithValue("@search", $"%{searchTerm}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                students.Add(MapStudent(reader));
            }

            return students;
        }

        /// <summary>
        /// Get all cached students
        /// </summary>
        public List<Student> GetAllStudents()
        {
            var students = new List<Student>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM cached_students ORDER BY name";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                students.Add(MapStudent(reader));
            }

            return students;
        }

        /// <summary>
        /// Update student fingerprint data
        /// </summary>
        public void UpdateStudentFingerprint(string rollNumber, byte[] template, byte[] image, int quality)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE cached_students 
                SET fingerprint_template = @template,
                    fingerprint_image = @image,
                    fingerprint_quality = @quality,
                    updated_at = @updated_at
                WHERE roll_number = @roll_number
            ";

            // Convert byte[] to base64 string for storage
            command.Parameters.AddWithValue("@template",
                template != null ? Convert.ToBase64String(template) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@image", image ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@quality", quality);
            command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@roll_number", rollNumber);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Map database reader to Student object
        /// </summary>
        private Student MapStudent(SqliteDataReader reader)
        {
            return new Student
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                StudentId = reader.GetInt32(reader.GetOrdinal("student_id")),
                RollNumber = reader.GetString(reader.GetOrdinal("roll_number")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                FatherName = reader.IsDBNull(reader.GetOrdinal("father_name")) ? null : reader.GetString(reader.GetOrdinal("father_name")),
                CNIC = reader.IsDBNull(reader.GetOrdinal("cnic")) ? null : reader.GetString(reader.GetOrdinal("cnic")),
                Gender = reader.IsDBNull(reader.GetOrdinal("gender")) ? null : reader.GetString(reader.GetOrdinal("gender")),
                Picture = reader.IsDBNull(reader.GetOrdinal("picture")) ? null : (byte[])reader["picture"],
                TestPhoto = reader.IsDBNull(reader.GetOrdinal("test_photo")) ? null : (byte[])reader["test_photo"],
                TestId = reader.IsDBNull(reader.GetOrdinal("test_id")) ? null : reader.GetInt32(reader.GetOrdinal("test_id")),
                TestName = reader.IsDBNull(reader.GetOrdinal("test_name")) ? null : reader.GetString(reader.GetOrdinal("test_name")),
                CollegeId = reader.IsDBNull(reader.GetOrdinal("college_id")) ? null : reader.GetInt32(reader.GetOrdinal("college_id")),
                CollegeName = reader.IsDBNull(reader.GetOrdinal("college_name")) ? null : reader.GetString(reader.GetOrdinal("college_name")),
                Venue = reader.IsDBNull(reader.GetOrdinal("venue")) ? null : reader.GetString(reader.GetOrdinal("venue")),
                Hall = reader.IsDBNull(reader.GetOrdinal("hall")) ? null : reader.GetString(reader.GetOrdinal("hall")),
                Zone = reader.IsDBNull(reader.GetOrdinal("zone")) ? null : reader.GetString(reader.GetOrdinal("zone")),
                Row = reader.IsDBNull(reader.GetOrdinal("row")) ? null : reader.GetString(reader.GetOrdinal("row")),
                Seat = reader.IsDBNull(reader.GetOrdinal("seat")) ? null : reader.GetString(reader.GetOrdinal("seat")),

                // Convert base64 string from database to byte[]
                FingerprintTemplate = reader.IsDBNull(reader.GetOrdinal("fingerprint_template"))
                    ? null
                    : Convert.FromBase64String(reader.GetString(reader.GetOrdinal("fingerprint_template"))),

                FingerprintImage = reader.IsDBNull(reader.GetOrdinal("fingerprint_image")) ? null : (byte[])reader["fingerprint_image"],
                FingerprintQuality = reader.IsDBNull(reader.GetOrdinal("fingerprint_quality")) ? null : reader.GetInt32(reader.GetOrdinal("fingerprint_quality")),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status"))
            };
        }

        #endregion

        #region Registration Operations

        /// <summary>
        /// Add registration to pending queue
        /// </summary>
        public void AddPendingRegistration(Registration registration)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO pending_registrations (
                    student_id, roll_number, fingerprint_template, fingerprint_image,
                    quality_score, captured_at, operator_id, operator_name, sync_status
                ) VALUES (
                    @student_id, @roll_number, @fingerprint_template, @fingerprint_image,
                    @quality_score, @captured_at, @operator_id, @operator_name, @sync_status
                )
            ";

            command.Parameters.AddWithValue("@student_id", registration.StudentId);
            command.Parameters.AddWithValue("@roll_number", registration.RollNumber);
            command.Parameters.AddWithValue("@fingerprint_template", registration.FingerprintTemplate);
            command.Parameters.AddWithValue("@fingerprint_image", registration.FingerprintImage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@quality_score", registration.QualityScore);
            command.Parameters.AddWithValue("@captured_at", registration.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@operator_id", registration.OperatorId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@operator_name", registration.OperatorName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sync_status", registration.SyncStatus);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get all pending registrations
        /// </summary>
        public List<Registration> GetPendingRegistrations()
        {
            var registrations = new List<Registration>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM pending_registrations WHERE sync_status = 'pending' ORDER BY created_at";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                registrations.Add(MapRegistration(reader));
            }

            return registrations;
        }

        /// <summary>
        /// Update registration sync status
        /// </summary>
        public void UpdateRegistrationStatus(int id, string status, string error = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE pending_registrations 
                SET sync_status = @status,
                    sync_error = @error,
                    sync_attempts = sync_attempts + 1,
                    last_sync_attempt = @last_attempt
                WHERE id = @id
            ";

            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@last_attempt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@id", id);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Delete synced registrations
        /// </summary>
        public void DeleteSyncedRegistrations()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pending_registrations WHERE sync_status = 'synced'";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Map database reader to Registration object
        /// </summary>
        private Registration MapRegistration(SqliteDataReader reader)
        {
            return new Registration
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                StudentId = reader.GetInt32(reader.GetOrdinal("student_id")),
                RollNumber = reader.GetString(reader.GetOrdinal("roll_number")),
                FingerprintTemplate = reader.GetString(reader.GetOrdinal("fingerprint_template")),
                FingerprintImage = reader.IsDBNull(reader.GetOrdinal("fingerprint_image")) ? null : (byte[])reader["fingerprint_image"],
                QualityScore = reader.GetInt32(reader.GetOrdinal("quality_score")),
                CapturedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("captured_at"))),
                OperatorId = reader.IsDBNull(reader.GetOrdinal("operator_id")) ? null : reader.GetInt32(reader.GetOrdinal("operator_id")),
                OperatorName = reader.IsDBNull(reader.GetOrdinal("operator_name")) ? null : reader.GetString(reader.GetOrdinal("operator_name")),
                SyncAttempts = reader.GetInt32(reader.GetOrdinal("sync_attempts")),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status")),
                SyncError = reader.IsDBNull(reader.GetOrdinal("sync_error")) ? null : reader.GetString(reader.GetOrdinal("sync_error"))
            };
        }

        #endregion

        #region Verification Operations

        /// <summary>
        /// Add verification to pending queue
        /// </summary>
        public void AddPendingVerification(Verification verification)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO pending_verifications (
                    student_id, roll_number, match_result, confidence_score, entry_allowed,
                    verified_at, verifier_id, verifier_name, notes, sync_status
                ) VALUES (
                    @student_id, @roll_number, @match_result, @confidence_score, @entry_allowed,
                    @verified_at, @verifier_id, @verifier_name, @notes, @sync_status
                )
            ";

            command.Parameters.AddWithValue("@student_id", verification.StudentId);
            command.Parameters.AddWithValue("@roll_number", verification.RollNumber);
            command.Parameters.AddWithValue("@match_result", verification.MatchResult);
            command.Parameters.AddWithValue("@confidence_score", verification.ConfidenceScore);
            command.Parameters.AddWithValue("@entry_allowed", verification.EntryAllowed ? 1 : 0);
            command.Parameters.AddWithValue("@verified_at", verification.VerifiedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@verifier_id", verification.VerifierId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@verifier_name", verification.VerifierName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@notes", verification.Notes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sync_status", verification.SyncStatus);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get all pending verifications
        /// </summary>
        public List<Verification> GetPendingVerifications()
        {
            var verifications = new List<Verification>();

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM pending_verifications WHERE sync_status = 'pending' ORDER BY created_at";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                verifications.Add(MapVerification(reader));
            }

            return verifications;
        }

        /// <summary>
        /// Update verification sync status
        /// </summary>
        public void UpdateVerificationStatus(int id, string status, string error = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE pending_verifications 
                SET sync_status = @status,
                    sync_error = @error,
                    sync_attempts = sync_attempts + 1,
                    last_sync_attempt = @last_attempt
                WHERE id = @id
            ";

            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@last_attempt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@id", id);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Delete synced verifications
        /// </summary>
        public void DeleteSyncedVerifications()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM pending_verifications WHERE sync_status = 'synced'";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Map database reader to Verification object
        /// </summary>
        private Verification MapVerification(SqliteDataReader reader)
        {
            return new Verification
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                StudentId = reader.GetInt32(reader.GetOrdinal("student_id")),
                RollNumber = reader.GetString(reader.GetOrdinal("roll_number")),
                MatchResult = reader.GetString(reader.GetOrdinal("match_result")),
                ConfidenceScore = reader.GetDouble(reader.GetOrdinal("confidence_score")),
                EntryAllowed = reader.GetInt32(reader.GetOrdinal("entry_allowed")) == 1,
                VerifiedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("verified_at"))),
                VerifierId = reader.IsDBNull(reader.GetOrdinal("verifier_id")) ? null : reader.GetInt32(reader.GetOrdinal("verifier_id")),
                VerifierName = reader.IsDBNull(reader.GetOrdinal("verifier_name")) ? null : reader.GetString(reader.GetOrdinal("verifier_name")),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
                SyncAttempts = reader.GetInt32(reader.GetOrdinal("sync_attempts")),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status")),
                SyncError = reader.IsDBNull(reader.GetOrdinal("sync_error")) ? null : reader.GetString(reader.GetOrdinal("sync_error"))
            };
        }

        #endregion

        #region Settings Operations

        /// <summary>
        /// Get setting value by key
        /// </summary>
        public string GetSetting(string key)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_settings WHERE key = @key";
            command.Parameters.AddWithValue("@key", key);

            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        /// <summary>
        /// Set setting value
        /// </summary>
        public void SetSetting(string key, string value)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO app_settings (key, value, updated_at)
                VALUES (@key, @value, @updated_at)
                ON CONFLICT(key) DO UPDATE SET
                    value = @value,
                    updated_at = @updated_at
            ";

            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", value);
            command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get all settings as AppSettings object
        /// </summary>
        public AppSettings GetAppSettings()
        {
            var settings = new AppSettings
            {
                ApiUrl = GetSetting("api_url") ?? "http://localhost:8000/api",
                QualityThreshold = int.Parse(GetSetting("quality_threshold") ?? "50"),
                ScannerTimeoutSeconds = int.Parse(GetSetting("scanner_timeout_seconds") ?? "30"),
                MatchThreshold = int.Parse(GetSetting("match_threshold") ?? "70"),
                AutoSyncEnabled = bool.Parse(GetSetting("auto_sync_enabled") ?? "true"),
                SyncIntervalMinutes = int.Parse(GetSetting("sync_interval_minutes") ?? "5"),
                LastLoginEmail = GetSetting("last_login_email") ?? "",
                RememberCredentials = bool.Parse(GetSetting("remember_credentials") ?? "false"),
                InactivityTimeoutMinutes = int.Parse(GetSetting("inactivity_timeout_minutes") ?? "30")
            };

            return settings;
        }

        /// <summary>
        /// Save AppSettings object to database
        /// </summary>
        public void SaveAppSettings(AppSettings settings)
        {
            SetSetting("api_url", settings.ApiUrl);
            SetSetting("quality_threshold", settings.QualityThreshold.ToString());
            SetSetting("scanner_timeout_seconds", settings.ScannerTimeoutSeconds.ToString());
            SetSetting("match_threshold", settings.MatchThreshold.ToString());
            SetSetting("auto_sync_enabled", settings.AutoSyncEnabled.ToString());
            SetSetting("sync_interval_minutes", settings.SyncIntervalMinutes.ToString());
            SetSetting("last_login_email", settings.LastLoginEmail);
            SetSetting("remember_credentials", settings.RememberCredentials.ToString());
            SetSetting("inactivity_timeout_minutes", settings.InactivityTimeoutMinutes.ToString());
        }

        #endregion

        #region User Session Operations

        /// <summary>
        /// Save user session
        /// </summary>
        public void SaveUserSession(User user)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // First deactivate all existing sessions
            using (var deactivateCommand = connection.CreateCommand())
            {
                deactivateCommand.CommandText = "UPDATE user_sessions SET is_active = 0";
                deactivateCommand.ExecuteNonQuery();
            }

            // Insert new session
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO user_sessions (
                    user_id, email, role, token, token_expires_at, logged_in_at, is_active
                ) VALUES (
                    @user_id, @email, @role, @token, @token_expires_at, @logged_in_at, 1
                )
            ";

            command.Parameters.AddWithValue("@user_id", user.Id);
            command.Parameters.AddWithValue("@email", user.Email);
            command.Parameters.AddWithValue("@role", user.Role);
            command.Parameters.AddWithValue("@token", user.Token);
            command.Parameters.AddWithValue("@token_expires_at", user.TokenExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@logged_in_at", user.LoggedInAt.ToString("yyyy-MM-dd HH:mm:ss"));

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Get active user session
        /// </summary>
        public User GetActiveSession()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM user_sessions WHERE is_active = 1 ORDER BY logged_in_at DESC LIMIT 1";

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32(reader.GetOrdinal("user_id")),
                    Email = reader.GetString(reader.GetOrdinal("email")),
                    Role = reader.GetString(reader.GetOrdinal("role")),
                    Token = reader.GetString(reader.GetOrdinal("token")),
                    TokenExpiresAt = reader.IsDBNull(reader.GetOrdinal("token_expires_at"))
                        ? null
                        : DateTime.Parse(reader.GetString(reader.GetOrdinal("token_expires_at"))),
                    LoggedInAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("logged_in_at"))),
                    LastActivityAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_activity_at"))),
                    IsActive = true
                };
            }

            return null;
        }

        /// <summary>
        /// Clear all sessions (logout)
        /// </summary>
        public void ClearAllSessions()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE user_sessions SET is_active = 0";
            command.ExecuteNonQuery();
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get pending sync counts
        /// </summary>
        public (int registrations, int verifications) GetPendingCounts()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            int regCount = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pending_registrations WHERE sync_status = 'pending'";
                regCount = Convert.ToInt32(command.ExecuteScalar());
            }

            int verCount = 0;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pending_verifications WHERE sync_status = 'pending'";
                verCount = Convert.ToInt32(command.ExecuteScalar());
            }

            return (regCount, verCount);
        }

        #endregion
    }
}