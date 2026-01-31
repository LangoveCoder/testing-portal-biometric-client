using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using Microsoft.Data.Sqlite;
using BACTBiometricClient.Models;
using BACTBiometricClient.Helpers;
using Dapper;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Enhanced SQLite database service with connection pooling, transactions, and migration support
    /// Implements offline-first architecture with optimized queries and proper indexing
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private readonly string _dbPath;
        private readonly object _lockObject = new object();
        private const int CurrentSchemaVersion = 2;
        private bool _disposed = false;

        public DatabaseService()
        {
            // Database path in AppData/Local folder
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(appDataPath, "BACTBiometric");

            // Create directory if not exists
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);

            _dbPath = Path.Combine(appFolder, "bact_biometric.db");
            
            // Enhanced connection string with performance optimizations
            _connectionString = $"Data Source={_dbPath};Cache=Shared;Synchronous=Normal;Foreign Keys=True;Pooling=True;Max Pool Size=10";
        }

        /// <summary>
        /// Initialize database with migration support and enhanced schema
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Check current schema version and migrate if needed
            int currentVersion = await GetSchemaVersionAsync(connection);
            
            if (currentVersion == 0)
            {
                // Fresh installation - create all tables
                await CreateInitialSchemaAsync(connection);
                await SetSchemaVersionAsync(connection, CurrentSchemaVersion);
            }
            else if (currentVersion < CurrentSchemaVersion)
            {
                // Migrate existing database
                await MigrateDatabaseAsync(connection, currentVersion, CurrentSchemaVersion);
            }

            // Optimize database performance
            await OptimizeDatabaseAsync(connection);
        }

        /// <summary>
        /// Get current database schema version
        /// </summary>
        private async Task<int> GetSchemaVersionAsync(SqliteConnection connection)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM app_settings WHERE key = 'schema_version'";
                var result = await command.ExecuteScalarAsync();
                return result != null ? int.Parse(result.ToString()) : 0;
            }
            catch
            {
                return 0; // No version table exists
            }
        }

        /// <summary>
        /// Set database schema version
        /// </summary>
        private async Task SetSchemaVersionAsync(SqliteConnection connection, int version)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO app_settings (key, value, updated_at)
                VALUES ('schema_version', @version, @updated_at)
            ";
            command.Parameters.AddWithValue("@version", version.ToString());
            command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Create initial database schema with enhanced design
        /// </summary>
        private async Task CreateInitialSchemaAsync(SqliteConnection connection)
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                string schema = GetEnhancedDatabaseSchema();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = schema;
                await command.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Migrate database from old version to new version
        /// </summary>
        private async Task MigrateDatabaseAsync(SqliteConnection connection, int fromVersion, int toVersion)
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                for (int version = fromVersion + 1; version <= toVersion; version++)
                {
                    await ApplyMigrationAsync(connection, transaction, version);
                }

                await SetSchemaVersionAsync(connection, toVersion);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Apply specific migration version
        /// </summary>
        private async Task ApplyMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, int version)
        {
            string migrationSql = version switch
            {
                2 => GetMigrationV2Sql(),
                _ => throw new NotSupportedException($"Migration version {version} not supported")
            };

            if (!string.IsNullOrEmpty(migrationSql))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = migrationSql;
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Optimize database performance settings
        /// </summary>
        private async Task OptimizeDatabaseAsync(SqliteConnection connection)
        {
            var optimizations = new[]
            {
                "PRAGMA journal_mode = WAL",
                "PRAGMA synchronous = NORMAL", 
                "PRAGMA cache_size = 10000",
                "PRAGMA temp_store = MEMORY",
                "PRAGMA mmap_size = 268435456", // 256MB
                "ANALYZE" // Update query planner statistics
            };

            foreach (var pragma in optimizations)
            {
                using var command = connection.CreateCommand();
                command.CommandText = pragma;
                await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Get enhanced database schema with proper indexing and relationships
        /// </summary>
        private string GetEnhancedDatabaseSchema()
        {
            return @"
                -- Users table for caching login information
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    email TEXT UNIQUE NOT NULL,
                    role TEXT NOT NULL CHECK (role IN ('Operator', 'CollegeAdmin')),
                    assigned_college_id INTEGER,
                    token TEXT,
                    token_expires_at TEXT,
                    last_login_at TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                -- Colleges table for caching college information
                CREATE TABLE IF NOT EXISTS colleges (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    district TEXT,
                    is_active INTEGER DEFAULT 1,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                -- User-college assignments for operators (many-to-many)
                CREATE TABLE IF NOT EXISTS user_colleges (
                    user_id INTEGER NOT NULL,
                    college_id INTEGER NOT NULL,
                    assigned_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (user_id, college_id),
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                    FOREIGN KEY (college_id) REFERENCES colleges(id) ON DELETE CASCADE
                );

                -- Enhanced students table for offline caching
                CREATE TABLE IF NOT EXISTS students (
                    id INTEGER PRIMARY KEY,
                    roll_number TEXT UNIQUE NOT NULL,
                    name TEXT NOT NULL,
                    father_name TEXT,
                    cnic TEXT,
                    gender TEXT CHECK (gender IN ('Male', 'Female')),
                    test_id INTEGER,
                    test_name TEXT,
                    college_id INTEGER NOT NULL,
                    college_name TEXT,
                    picture BLOB,
                    fingerprint_template BLOB,
                    fingerprint_image BLOB,
                    fingerprint_quality INTEGER,
                    fingerprint_registered_at TEXT,
                    sync_status TEXT DEFAULT 'synced' CHECK (sync_status IN ('pending', 'syncing', 'synced', 'failed')),
                    last_updated TEXT DEFAULT CURRENT_TIMESTAMP,
                    cache_expires_at TEXT,
                    FOREIGN KEY (college_id) REFERENCES colleges(id)
                );

                -- Enhanced queued operations for offline sync with retry logic
                CREATE TABLE IF NOT EXISTS queued_operations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    operation_type TEXT NOT NULL CHECK (operation_type IN ('FingerprintRegistration', 'FingerprintVerification', 'StudentUpdate')),
                    operation_data TEXT NOT NULL, -- JSON payload
                    priority INTEGER DEFAULT 1, -- Higher number = higher priority
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    scheduled_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    sync_attempts INTEGER DEFAULT 0,
                    max_attempts INTEGER DEFAULT 3,
                    last_error TEXT,
                    sync_status TEXT DEFAULT 'pending' CHECK (sync_status IN ('pending', 'syncing', 'synced', 'failed', 'cancelled')),
                    last_sync_attempt TEXT,
                    synced_at TEXT
                );

                -- Verification results cache with enhanced tracking
                CREATE TABLE IF NOT EXISTS verification_results (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    roll_number TEXT NOT NULL,
                    student_id INTEGER,
                    match_result TEXT NOT NULL CHECK (match_result IN ('Match', 'NoMatch', 'Inconclusive')),
                    confidence_score REAL CHECK (confidence_score >= 0 AND confidence_score <= 100),
                    entry_allowed INTEGER DEFAULT 0,
                    verified_at TEXT NOT NULL,
                    verified_by_id INTEGER,
                    verified_by_name TEXT,
                    remarks TEXT,
                    sync_status TEXT DEFAULT 'pending' CHECK (sync_status IN ('pending', 'syncing', 'synced', 'failed')),
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (student_id) REFERENCES students(id),
                    FOREIGN KEY (verified_by_id) REFERENCES users(id)
                );

                -- Application settings with enhanced configuration
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT,
                    description TEXT,
                    category TEXT DEFAULT 'general',
                    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
                );

                -- Sync logs for monitoring and debugging
                CREATE TABLE IF NOT EXISTS sync_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sync_type TEXT NOT NULL,
                    direction TEXT NOT NULL CHECK (direction IN ('upload', 'download')),
                    records_count INTEGER DEFAULT 0,
                    success_count INTEGER DEFAULT 0,
                    failed_count INTEGER DEFAULT 0,
                    error_message TEXT,
                    started_at TEXT NOT NULL,
                    completed_at TEXT,
                    duration_seconds REAL
                );

                -- Error logs for comprehensive debugging
                CREATE TABLE IF NOT EXISTS error_logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    error_type TEXT NOT NULL,
                    error_message TEXT NOT NULL,
                    stack_trace TEXT,
                    user_id INTEGER,
                    context_data TEXT, -- JSON with additional context
                    occurred_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (user_id) REFERENCES users(id)
                );

                -- Performance-optimized indexes
                CREATE INDEX IF NOT EXISTS idx_students_roll_number ON students(roll_number);
                CREATE INDEX IF NOT EXISTS idx_students_college_id ON students(college_id);
                CREATE INDEX IF NOT EXISTS idx_students_sync_status ON students(sync_status);
                CREATE INDEX IF NOT EXISTS idx_students_name_search ON students(name COLLATE NOCASE);
                CREATE INDEX IF NOT EXISTS idx_students_cache_expires ON students(cache_expires_at);
                
                CREATE INDEX IF NOT EXISTS idx_queued_operations_status ON queued_operations(sync_status);
                CREATE INDEX IF NOT EXISTS idx_queued_operations_type ON queued_operations(operation_type);
                CREATE INDEX IF NOT EXISTS idx_queued_operations_priority ON queued_operations(priority DESC, created_at);
                CREATE INDEX IF NOT EXISTS idx_queued_operations_scheduled ON queued_operations(scheduled_at);
                
                CREATE INDEX IF NOT EXISTS idx_verification_results_sync_status ON verification_results(sync_status);
                CREATE INDEX IF NOT EXISTS idx_verification_results_roll_number ON verification_results(roll_number);
                CREATE INDEX IF NOT EXISTS idx_verification_results_verified_at ON verification_results(verified_at);
                
                CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
                CREATE INDEX IF NOT EXISTS idx_users_role ON users(role);
                
                CREATE INDEX IF NOT EXISTS idx_sync_logs_started_at ON sync_logs(started_at);
                CREATE INDEX IF NOT EXISTS idx_error_logs_occurred_at ON error_logs(occurred_at);

                -- Full-text search for student names
                CREATE VIRTUAL TABLE IF NOT EXISTS students_fts USING fts5(
                    name, father_name, roll_number,
                    content='students',
                    content_rowid='id'
                );

                -- Triggers to maintain FTS index
                CREATE TRIGGER IF NOT EXISTS students_fts_insert AFTER INSERT ON students BEGIN
                    INSERT INTO students_fts(rowid, name, father_name, roll_number) 
                    VALUES (new.id, new.name, new.father_name, new.roll_number);
                END;

                CREATE TRIGGER IF NOT EXISTS students_fts_delete AFTER DELETE ON students BEGIN
                    INSERT INTO students_fts(students_fts, rowid, name, father_name, roll_number) 
                    VALUES('delete', old.id, old.name, old.father_name, old.roll_number);
                END;

                CREATE TRIGGER IF NOT EXISTS students_fts_update AFTER UPDATE ON students BEGIN
                    INSERT INTO students_fts(students_fts, rowid, name, father_name, roll_number) 
                    VALUES('delete', old.id, old.name, old.father_name, old.roll_number);
                    INSERT INTO students_fts(rowid, name, father_name, roll_number) 
                    VALUES (new.id, new.name, new.father_name, new.roll_number);
                END;

                -- Insert default settings
                INSERT OR IGNORE INTO app_settings (key, value, description, category) VALUES 
                    ('api_url', 'http://localhost:8000/api', 'Base API URL for Laravel backend', 'api'),
                    ('quality_threshold', '50', 'Minimum fingerprint quality score', 'biometric'),
                    ('auto_sync_enabled', 'true', 'Enable automatic synchronization', 'sync'),
                    ('sync_interval_minutes', '5', 'Sync interval in minutes', 'sync'),
                    ('scanner_timeout_seconds', '30', 'Scanner operation timeout', 'biometric'),
                    ('match_threshold', '70', 'Fingerprint match confidence threshold', 'biometric'),
                    ('last_login_email', '', 'Last successful login email', 'auth'),
                    ('remember_credentials', 'false', 'Remember login credentials', 'auth'),
                    ('inactivity_timeout_minutes', '30', 'Session inactivity timeout', 'auth'),
                    ('cache_expiry_hours', '24', 'Student data cache expiry time', 'cache'),
                    ('max_retry_attempts', '3', 'Maximum sync retry attempts', 'sync'),
                    ('batch_size', '50', 'Sync batch size for operations', 'sync');
            ";
        }

        /// <summary>
        /// Get migration SQL for version 2
        /// </summary>
        private string GetMigrationV2Sql()
        {
            return @"
                -- Add new columns to existing tables
                ALTER TABLE students ADD COLUMN cache_expires_at TEXT;
                ALTER TABLE queued_operations ADD COLUMN priority INTEGER DEFAULT 1;
                ALTER TABLE queued_operations ADD COLUMN scheduled_at TEXT DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE queued_operations ADD COLUMN max_attempts INTEGER DEFAULT 3;
                ALTER TABLE queued_operations ADD COLUMN synced_at TEXT;
                
                -- Update existing data
                UPDATE students SET cache_expires_at = datetime('now', '+24 hours') WHERE cache_expires_at IS NULL;
                UPDATE queued_operations SET scheduled_at = created_at WHERE scheduled_at IS NULL;
            ";
        }

        #region Connection Management and Transactions

        /// <summary>
        /// Create a new database connection with optimized settings
        /// </summary>
        private SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            return connection;
        }

        /// <summary>
        /// Execute operation within a transaction with retry logic
        /// </summary>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> operation, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var connection = CreateConnection();
                    await connection.OpenAsync();
                    
                    using var transaction = connection.BeginTransaction();
                    try
                    {
                        var result = await operation(connection, transaction);
                        await transaction.CommitAsync();
                        return result;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries - 1) // SQLITE_BUSY
                {
                    lastException = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt))); // Exponential backoff
                }
            }
            
            throw lastException ?? new InvalidOperationException("Transaction failed after retries");
        }

        /// <summary>
        /// Execute operation without transaction with retry logic
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<SqliteConnection, Task<T>> operation, int maxRetries = 3)
        {
            Exception lastException = null;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var connection = CreateConnection();
                    await connection.OpenAsync();
                    return await operation(connection);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < maxRetries - 1) // SQLITE_BUSY
                {
                    lastException = ex;
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt))); // Exponential backoff
                }
            }
            
            throw lastException ?? new InvalidOperationException("Operation failed after retries");
        }

        #endregion

        #region Student Operations

        /// <summary>
        /// Save or update a student in cache with enhanced async support
        /// </summary>
        public async Task SaveStudentAsync(Student student)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO students (
                        id, roll_number, name, father_name, cnic, gender,
                        test_id, test_name, college_id, college_name,
                        picture, fingerprint_template, fingerprint_image, fingerprint_quality,
                        fingerprint_registered_at, sync_status, last_updated, cache_expires_at
                    ) VALUES (
                        @id, @roll_number, @name, @father_name, @cnic, @gender,
                        @test_id, @test_name, @college_id, @college_name,
                        @picture, @fingerprint_template, @fingerprint_image, @fingerprint_quality,
                        @fingerprint_registered_at, @sync_status, @last_updated, @cache_expires_at
                    )
                    ON CONFLICT(roll_number) DO UPDATE SET
                        name = @name,
                        father_name = @father_name,
                        cnic = @cnic,
                        gender = @gender,
                        test_id = @test_id,
                        test_name = @test_name,
                        college_id = @college_id,
                        college_name = @college_name,
                        picture = @picture,
                        fingerprint_template = @fingerprint_template,
                        fingerprint_image = @fingerprint_image,
                        fingerprint_quality = @fingerprint_quality,
                        fingerprint_registered_at = @fingerprint_registered_at,
                        sync_status = @sync_status,
                        last_updated = @last_updated,
                        cache_expires_at = @cache_expires_at
                ";

                AddStudentParameters(command, student);
                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Save multiple students in a batch operation for better performance
        /// </summary>
        public async Task SaveStudentsBatchAsync(IEnumerable<Student> students)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                foreach (var student in students)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO students (
                            id, roll_number, name, father_name, cnic, gender,
                            test_id, test_name, college_id, college_name,
                            picture, fingerprint_template, fingerprint_image, fingerprint_quality,
                            fingerprint_registered_at, sync_status, last_updated, cache_expires_at
                        ) VALUES (
                            @id, @roll_number, @name, @father_name, @cnic, @gender,
                            @test_id, @test_name, @college_id, @college_name,
                            @picture, @fingerprint_template, @fingerprint_image, @fingerprint_quality,
                            @fingerprint_registered_at, @sync_status, @last_updated, @cache_expires_at
                        )
                        ON CONFLICT(roll_number) DO UPDATE SET
                            name = @name,
                            father_name = @father_name,
                            cnic = @cnic,
                            gender = @gender,
                            test_id = @test_id,
                            test_name = @test_name,
                            college_id = @college_id,
                            college_name = @college_name,
                            picture = @picture,
                            sync_status = @sync_status,
                            last_updated = @last_updated,
                            cache_expires_at = @cache_expires_at
                    ";

                    AddStudentParameters(command, student);
                    await command.ExecuteNonQueryAsync();
                }
                return true;
            });
        }

        /// <summary>
        /// Get student by roll number with async support
        /// </summary>
        public async Task<Student> GetStudentByRollNumberAsync(string rollNumber)
        {
            return await ExecuteAsync(async connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM students WHERE roll_number = @roll_number";
                command.Parameters.AddWithValue("@roll_number", rollNumber);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapStudent(reader);
                }

                return null;
            });
        }

        /// <summary>
        /// Search students with enhanced full-text search and college filtering
        /// </summary>
        public async Task<List<Student>> SearchStudentsAsync(string searchTerm, int? collegeId = null, int limit = 50)
        {
            return await ExecuteAsync(async connection =>
            {
                var students = new List<Student>();
                
                using var command = connection.CreateCommand();
                
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    // No search term - return all students for college
                    command.CommandText = collegeId.HasValue 
                        ? "SELECT * FROM students WHERE college_id = @college_id ORDER BY name LIMIT @limit"
                        : "SELECT * FROM students ORDER BY name LIMIT @limit";
                    
                    if (collegeId.HasValue)
                        command.Parameters.AddWithValue("@college_id", collegeId.Value);
                }
                else
                {
                    // Use full-text search for better performance
                    command.CommandText = collegeId.HasValue
                        ? @"SELECT s.* FROM students s 
                           JOIN students_fts fts ON s.id = fts.rowid 
                           WHERE fts MATCH @search AND s.college_id = @college_id 
                           ORDER BY rank LIMIT @limit"
                        : @"SELECT s.* FROM students s 
                           JOIN students_fts fts ON s.id = fts.rowid 
                           WHERE fts MATCH @search 
                           ORDER BY rank LIMIT @limit";
                    
                    command.Parameters.AddWithValue("@search", $"{searchTerm}*");
                    if (collegeId.HasValue)
                        command.Parameters.AddWithValue("@college_id", collegeId.Value);
                }
                
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    students.Add(MapStudent(reader));
                }

                return students;
            });
        }

        /// <summary>
        /// Get students by college with pagination support
        /// </summary>
        public async Task<List<Student>> GetStudentsByCollegeAsync(int collegeId, int offset = 0, int limit = 100)
        {
            return await ExecuteAsync(async connection =>
            {
                var students = new List<Student>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM students 
                    WHERE college_id = @college_id 
                    ORDER BY name 
                    LIMIT @limit OFFSET @offset
                ";
                command.Parameters.AddWithValue("@college_id", collegeId);
                command.Parameters.AddWithValue("@limit", limit);
                command.Parameters.AddWithValue("@offset", offset);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    students.Add(MapStudent(reader));
                }

                return students;
            });
        }

        /// <summary>
        /// Update student fingerprint data with enhanced validation
        /// </summary>
        public async Task UpdateStudentFingerprintAsync(string rollNumber, byte[] template, byte[] image, int quality)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE students 
                    SET fingerprint_template = @template,
                        fingerprint_image = @image,
                        fingerprint_quality = @quality,
                        fingerprint_registered_at = @registered_at,
                        sync_status = 'pending',
                        last_updated = @updated_at
                    WHERE roll_number = @roll_number
                ";

                command.Parameters.AddWithValue("@template", template);
                command.Parameters.AddWithValue("@image", image);
                command.Parameters.AddWithValue("@quality", quality);
                command.Parameters.AddWithValue("@registered_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@roll_number", rollNumber);

                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Get students with expired cache for refresh
        /// </summary>
        public async Task<List<Student>> GetExpiredCacheStudentsAsync()
        {
            return await ExecuteAsync(async connection =>
            {
                var students = new List<Student>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM students 
                    WHERE cache_expires_at < @now 
                    ORDER BY last_updated 
                    LIMIT 100
                ";
                command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    students.Add(MapStudent(reader));
                }

                return students;
            });
        }

        /// <summary>
        /// Add parameters for student operations
        /// </summary>
        private void AddStudentParameters(SqliteCommand command, Student student)
        {
            command.Parameters.AddWithValue("@id", student.Id);
            command.Parameters.AddWithValue("@roll_number", student.RollNumber);
            command.Parameters.AddWithValue("@name", student.Name);
            command.Parameters.AddWithValue("@father_name", student.FatherName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@cnic", student.CNIC ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@gender", student.Gender ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@test_id", student.TestId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@test_name", student.TestName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@college_id", student.CollegeId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@college_name", student.CollegeName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@picture", student.Picture ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fingerprint_template", student.FingerprintTemplate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fingerprint_image", student.FingerprintImage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fingerprint_quality", student.FingerprintQuality ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fingerprint_registered_at", student.FingerprintRegisteredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sync_status", student.SyncStatus ?? "pending");
            command.Parameters.AddWithValue("@last_updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@cache_expires_at", DateTime.Now.AddHours(24).ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// Map database reader to Student object with enhanced field mapping
        /// </summary>
        private Student MapStudent(SqliteDataReader reader)
        {
            return new Student
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                RollNumber = reader.GetString(reader.GetOrdinal("roll_number")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                FatherName = reader.IsDBNull(reader.GetOrdinal("father_name")) ? null : reader.GetString(reader.GetOrdinal("father_name")),
                CNIC = reader.IsDBNull(reader.GetOrdinal("cnic")) ? null : reader.GetString(reader.GetOrdinal("cnic")),
                Gender = reader.IsDBNull(reader.GetOrdinal("gender")) ? null : reader.GetString(reader.GetOrdinal("gender")),
                Picture = reader.IsDBNull(reader.GetOrdinal("picture")) ? null : (byte[])reader["picture"],
                TestId = reader.IsDBNull(reader.GetOrdinal("test_id")) ? null : reader.GetInt32(reader.GetOrdinal("test_id")),
                TestName = reader.IsDBNull(reader.GetOrdinal("test_name")) ? null : reader.GetString(reader.GetOrdinal("test_name")),
                CollegeId = reader.IsDBNull(reader.GetOrdinal("college_id")) ? null : reader.GetInt32(reader.GetOrdinal("college_id")),
                CollegeName = reader.IsDBNull(reader.GetOrdinal("college_name")) ? null : reader.GetString(reader.GetOrdinal("college_name")),
                FingerprintTemplate = reader.IsDBNull(reader.GetOrdinal("fingerprint_template")) ? null : (byte[])reader["fingerprint_template"],
                FingerprintImage = reader.IsDBNull(reader.GetOrdinal("fingerprint_image")) ? null : (byte[])reader["fingerprint_image"],
                FingerprintQuality = reader.IsDBNull(reader.GetOrdinal("fingerprint_quality")) ? null : reader.GetInt32(reader.GetOrdinal("fingerprint_quality")),
                FingerprintRegisteredAt = reader.IsDBNull(reader.GetOrdinal("fingerprint_registered_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("fingerprint_registered_at"))),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status"))
            };
        }

        #endregion

        #region Queue Management Operations

        /// <summary>
        /// Add operation to queue with priority and scheduling support
        /// </summary>
        public async Task<int> AddQueuedOperationAsync(string operationType, object operationData, int priority = 1, DateTime? scheduledAt = null)
        {
            return await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO queued_operations (
                        operation_type, operation_data, priority, scheduled_at, sync_status
                    ) VALUES (
                        @operation_type, @operation_data, @priority, @scheduled_at, 'pending'
                    );
                    SELECT last_insert_rowid();
                ";

                command.Parameters.AddWithValue("@operation_type", operationType);
                command.Parameters.AddWithValue("@operation_data", System.Text.Json.JsonSerializer.Serialize(operationData));
                command.Parameters.AddWithValue("@priority", priority);
                command.Parameters.AddWithValue("@scheduled_at", (scheduledAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss"));

                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }

        /// <summary>
        /// Get pending operations ordered by priority and schedule
        /// </summary>
        public async Task<List<QueuedOperation>> GetPendingOperationsAsync(int limit = 50)
        {
            return await ExecuteAsync(async connection =>
            {
                var operations = new List<QueuedOperation>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM queued_operations 
                    WHERE sync_status = 'pending' 
                        AND scheduled_at <= @now
                        AND sync_attempts < max_attempts
                    ORDER BY priority DESC, created_at ASC 
                    LIMIT @limit
                ";
                command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    operations.Add(MapQueuedOperation(reader));
                }

                return operations;
            });
        }

        /// <summary>
        /// Update operation status with retry tracking
        /// </summary>
        public async Task UpdateOperationStatusAsync(int operationId, string status, string error = null)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE queued_operations 
                    SET sync_status = @status,
                        last_error = @error,
                        sync_attempts = sync_attempts + 1,
                        last_sync_attempt = @last_attempt,
                        synced_at = CASE WHEN @status = 'synced' THEN @last_attempt ELSE synced_at END
                    WHERE id = @id
                ";

                command.Parameters.AddWithValue("@status", status);
                command.Parameters.AddWithValue("@error", error ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@last_attempt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@id", operationId);

                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Clean up completed operations older than specified days
        /// </summary>
        public async Task<int> CleanupCompletedOperationsAsync(int olderThanDays = 7)
        {
            return await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    DELETE FROM queued_operations 
                    WHERE sync_status IN ('synced', 'cancelled') 
                        AND synced_at < @cutoff_date
                ";

                var cutoffDate = DateTime.Now.AddDays(-olderThanDays);
                command.Parameters.AddWithValue("@cutoff_date", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));

                var deletedCount = await command.ExecuteNonQueryAsync();
                return deletedCount;
            });
        }

        /// <summary>
        /// Get failed operations that can be retried
        /// </summary>
        public async Task<List<QueuedOperation>> GetRetryableOperationsAsync()
        {
            return await ExecuteAsync(async connection =>
            {
                var operations = new List<QueuedOperation>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM queued_operations 
                    WHERE sync_status = 'failed' 
                        AND sync_attempts < max_attempts
                        AND scheduled_at <= @now
                    ORDER BY priority DESC, last_sync_attempt ASC
                ";
                command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    operations.Add(MapQueuedOperation(reader));
                }

                return operations;
            });
        }

        /// <summary>
        /// Map database reader to QueuedOperation object
        /// </summary>
        private QueuedOperation MapQueuedOperation(SqliteDataReader reader)
        {
            return new QueuedOperation
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                OperationType = reader.GetString(reader.GetOrdinal("operation_type")),
                OperationData = reader.GetString(reader.GetOrdinal("operation_data")),
                Priority = reader.GetInt32(reader.GetOrdinal("priority")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                ScheduledAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("scheduled_at"))),
                SyncAttempts = reader.GetInt32(reader.GetOrdinal("sync_attempts")),
                MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
                LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status")),
                LastSyncAttempt = reader.IsDBNull(reader.GetOrdinal("last_sync_attempt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_sync_attempt"))),
                SyncedAt = reader.IsDBNull(reader.GetOrdinal("synced_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("synced_at")))
            };
        }

        #endregion

        #region Registration Operations

        /// <summary>
        /// Add registration to queue using enhanced queue system
        /// </summary>
        public async Task AddPendingRegistrationAsync(Registration registration)
        {
            await AddQueuedOperationAsync("FingerprintRegistration", registration, priority: 2);
        }

        /// <summary>
        /// Get all pending registrations from queue
        /// </summary>
        public async Task<List<Registration>> GetPendingRegistrationsAsync()
        {
            var operations = await GetPendingOperationsAsync();
            var registrations = new List<Registration>();

            foreach (var operation in operations.Where(o => o.OperationType == "FingerprintRegistration"))
            {
                try
                {
                    var registration = System.Text.Json.JsonSerializer.Deserialize<Registration>(operation.OperationData);
                    registration.Id = operation.Id; // Use queue ID
                    registration.SyncStatus = operation.SyncStatus;
                    registration.SyncAttempts = operation.SyncAttempts;
                    registration.SyncError = operation.LastError;
                    registrations.Add(registration);
                }
                catch (Exception ex)
                {
                    // Log deserialization error
                    await LogErrorAsync("RegistrationDeserialization", ex.Message, ex.StackTrace);
                }
            }

            return registrations;
        }

        /// <summary>
        /// Update registration sync status using queue system
        /// </summary>
        public async Task UpdateRegistrationStatusAsync(int id, string status, string error = null)
        {
            await UpdateOperationStatusAsync(id, status, error);
        }

        #endregion

        #region Verification Operations

        /// <summary>
        /// Add verification to queue using enhanced queue system
        /// </summary>
        public async Task AddPendingVerificationAsync(Verification verification)
        {
            await AddQueuedOperationAsync("FingerprintVerification", verification, priority: 1);
        }

        /// <summary>
        /// Get all pending verifications from queue
        /// </summary>
        public async Task<List<Verification>> GetPendingVerificationsAsync()
        {
            var operations = await GetPendingOperationsAsync();
            var verifications = new List<Verification>();

            foreach (var operation in operations.Where(o => o.OperationType == "FingerprintVerification"))
            {
                try
                {
                    var verification = System.Text.Json.JsonSerializer.Deserialize<Verification>(operation.OperationData);
                    verification.Id = operation.Id; // Use queue ID
                    verification.SyncStatus = operation.SyncStatus;
                    verification.SyncAttempts = operation.SyncAttempts;
                    verification.SyncError = operation.LastError;
                    verifications.Add(verification);
                }
                catch (Exception ex)
                {
                    // Log deserialization error
                    await LogErrorAsync("VerificationDeserialization", ex.Message, ex.StackTrace);
                }
            }

            return verifications;
        }

        /// <summary>
        /// Update verification sync status using queue system
        /// </summary>
        public async Task UpdateVerificationStatusAsync(int id, string status, string error = null)
        {
            await UpdateOperationStatusAsync(id, status, error);
        }

        /// <summary>
        /// Save verification result to local cache
        /// </summary>
        public async Task SaveVerificationResultAsync(Verification verification)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO verification_results (
                        roll_number, student_id, match_result, confidence_score, entry_allowed,
                        verified_at, verified_by_id, verified_by_name, remarks, sync_status
                    ) VALUES (
                        @roll_number, @student_id, @match_result, @confidence_score, @entry_allowed,
                        @verified_at, @verified_by_id, @verified_by_name, @remarks, @sync_status
                    )
                ";

                command.Parameters.AddWithValue("@roll_number", verification.RollNumber);
                command.Parameters.AddWithValue("@student_id", verification.StudentId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@match_result", verification.MatchResult);
                command.Parameters.AddWithValue("@confidence_score", verification.ConfidenceScore);
                command.Parameters.AddWithValue("@entry_allowed", verification.EntryAllowed ? 1 : 0);
                command.Parameters.AddWithValue("@verified_at", verification.VerifiedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@verified_by_id", verification.VerifierId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@verified_by_name", verification.VerifierName ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@remarks", verification.Notes ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@sync_status", verification.SyncStatus ?? "pending");

                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Get verification history for a student
        /// </summary>
        public async Task<List<Verification>> GetVerificationHistoryAsync(string rollNumber)
        {
            return await ExecuteAsync(async connection =>
            {
                var verifications = new List<Verification>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM verification_results 
                    WHERE roll_number = @roll_number 
                    ORDER BY verified_at DESC
                ";
                command.Parameters.AddWithValue("@roll_number", rollNumber);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    verifications.Add(MapVerificationResult(reader));
                }

                return verifications;
            });
        }

        /// <summary>
        /// Map database reader to Verification object from results table
        /// </summary>
        private Verification MapVerificationResult(SqliteDataReader reader)
        {
            return new Verification
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                RollNumber = reader.GetString(reader.GetOrdinal("roll_number")),
                StudentId = reader.IsDBNull(reader.GetOrdinal("student_id")) ? null : reader.GetInt32(reader.GetOrdinal("student_id")),
                MatchResult = reader.GetString(reader.GetOrdinal("match_result")),
                ConfidenceScore = reader.GetDouble(reader.GetOrdinal("confidence_score")),
                EntryAllowed = reader.GetInt32(reader.GetOrdinal("entry_allowed")) == 1,
                VerifiedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("verified_at"))),
                VerifierId = reader.IsDBNull(reader.GetOrdinal("verified_by_id")) ? null : reader.GetInt32(reader.GetOrdinal("verified_by_id")),
                VerifierName = reader.IsDBNull(reader.GetOrdinal("verified_by_name")) ? null : reader.GetString(reader.GetOrdinal("verified_by_name")),
                Notes = reader.IsDBNull(reader.GetOrdinal("remarks")) ? null : reader.GetString(reader.GetOrdinal("remarks")),
                SyncStatus = reader.GetString(reader.GetOrdinal("sync_status"))
            };
        }

        #endregion

        #region Settings Operations

        /// <summary>
        /// Get setting value by key with async support
        /// </summary>
        public async Task<string> GetSettingAsync(string key)
        {
            return await ExecuteAsync(async connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM app_settings WHERE key = @key";
                command.Parameters.AddWithValue("@key", key);

                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            });
        }

        /// <summary>
        /// Set setting value with async support
        /// </summary>
        public async Task SetSettingAsync(string key, string value)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
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

                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Get all settings as AppSettings object with async support
        /// </summary>
        public async Task<AppSettings> GetAppSettingsAsync()
        {
            var settings = new AppSettings
            {
                ApiUrl = await GetSettingAsync("api_url") ?? "http://localhost:8000/api",
                QualityThreshold = int.Parse(await GetSettingAsync("quality_threshold") ?? "50"),
                ScannerTimeoutSeconds = int.Parse(await GetSettingAsync("scanner_timeout_seconds") ?? "30"),
                MatchThreshold = int.Parse(await GetSettingAsync("match_threshold") ?? "70"),
                AutoSyncEnabled = bool.Parse(await GetSettingAsync("auto_sync_enabled") ?? "true"),
                SyncIntervalMinutes = int.Parse(await GetSettingAsync("sync_interval_minutes") ?? "5"),
                LastLoginEmail = await GetSettingAsync("last_login_email") ?? "",
                RememberCredentials = bool.Parse(await GetSettingAsync("remember_credentials") ?? "false"),
                InactivityTimeoutMinutes = int.Parse(await GetSettingAsync("inactivity_timeout_minutes") ?? "30")
            };

            return settings;
        }

        /// <summary>
        /// Save AppSettings object to database with async support
        /// </summary>
        public async Task SaveAppSettingsAsync(AppSettings settings)
        {
            await SetSettingAsync("api_url", settings.ApiUrl);
            await SetSettingAsync("quality_threshold", settings.QualityThreshold.ToString());
            await SetSettingAsync("scanner_timeout_seconds", settings.ScannerTimeoutSeconds.ToString());
            await SetSettingAsync("match_threshold", settings.MatchThreshold.ToString());
            await SetSettingAsync("auto_sync_enabled", settings.AutoSyncEnabled.ToString());
            await SetSettingAsync("sync_interval_minutes", settings.SyncIntervalMinutes.ToString());
            await SetSettingAsync("last_login_email", settings.LastLoginEmail);
            await SetSettingAsync("remember_credentials", settings.RememberCredentials.ToString());
            await SetSettingAsync("inactivity_timeout_minutes", settings.InactivityTimeoutMinutes.ToString());
        }

        #endregion

        #region College Operations

        /// <summary>
        /// Save colleges to cache
        /// </summary>
        public async Task SaveCollegesAsync(IEnumerable<College> colleges)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                foreach (var college in colleges)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO colleges (id, name, district, is_active, updated_at)
                        VALUES (@id, @name, @district, @is_active, @updated_at)
                        ON CONFLICT(id) DO UPDATE SET
                            name = @name,
                            district = @district,
                            is_active = @is_active,
                            updated_at = @updated_at
                    ";

                    command.Parameters.AddWithValue("@id", college.Id);
                    command.Parameters.AddWithValue("@name", college.Name);
                    command.Parameters.AddWithValue("@district", college.District ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@is_active", college.IsActive ? 1 : 0);
                    command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    await command.ExecuteNonQueryAsync();
                }
                return true;
            });
        }

        /// <summary>
        /// Get colleges assigned to a user
        /// </summary>
        public async Task<List<College>> GetUserCollegesAsync(int userId)
        {
            return await ExecuteAsync(async connection =>
            {
                var colleges = new List<College>();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT c.* FROM colleges c
                    JOIN user_colleges uc ON c.id = uc.college_id
                    WHERE uc.user_id = @user_id AND c.is_active = 1
                    ORDER BY c.name
                ";
                command.Parameters.AddWithValue("@user_id", userId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    colleges.Add(MapCollege(reader));
                }

                return colleges;
            });
        }

        /// <summary>
        /// Map database reader to College object
        /// </summary>
        private College MapCollege(SqliteDataReader reader)
        {
            return new College
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                District = reader.IsDBNull(reader.GetOrdinal("district")) ? null : reader.GetString(reader.GetOrdinal("district")),
                IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1
            };
        }

        #endregion

        #region User Operations

        /// <summary>
        /// Save user information to cache
        /// </summary>
        public async Task SaveUserAsync(User user)
        {
            await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO users (id, name, email, role, assigned_college_id, token, token_expires_at, last_login_at, updated_at)
                    VALUES (@id, @name, @email, @role, @assigned_college_id, @token, @token_expires_at, @last_login_at, @updated_at)
                    ON CONFLICT(id) DO UPDATE SET
                        name = @name,
                        email = @email,
                        role = @role,
                        assigned_college_id = @assigned_college_id,
                        token = @token,
                        token_expires_at = @token_expires_at,
                        last_login_at = @last_login_at,
                        updated_at = @updated_at
                ";

                command.Parameters.AddWithValue("@id", user.Id);
                command.Parameters.AddWithValue("@name", user.Name);
                command.Parameters.AddWithValue("@email", user.Email);
                command.Parameters.AddWithValue("@role", user.Role);
                command.Parameters.AddWithValue("@assigned_college_id", user.AssignedCollegeId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@token", user.Token ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@token_expires_at", user.TokenExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@last_login_at", user.LastLoginAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                await command.ExecuteNonQueryAsync();
                return true;
            });
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await ExecuteAsync(async connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM users WHERE id = @id";
                command.Parameters.AddWithValue("@id", userId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapUser(reader);
                }

                return null;
            });
        }

        /// <summary>
        /// Map database reader to User object
        /// </summary>
        private User MapUser(SqliteDataReader reader)
        {
            return new User
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Email = reader.GetString(reader.GetOrdinal("email")),
                Role = reader.GetString(reader.GetOrdinal("role")),
                AssignedCollegeId = reader.IsDBNull(reader.GetOrdinal("assigned_college_id")) ? null : reader.GetInt32(reader.GetOrdinal("assigned_college_id")),
                Token = reader.IsDBNull(reader.GetOrdinal("token")) ? null : reader.GetString(reader.GetOrdinal("token")),
                TokenExpiresAt = reader.IsDBNull(reader.GetOrdinal("token_expires_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("token_expires_at"))),
                LastLoginAt = reader.IsDBNull(reader.GetOrdinal("last_login_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_login_at")))
            };
        }

        #endregion

        #region Logging Operations

        /// <summary>
        /// Log error to database
        /// </summary>
        public async Task LogErrorAsync(string errorType, string errorMessage, string stackTrace = null, int? userId = null, string contextData = null)
        {
            try
            {
                await ExecuteInTransactionAsync(async (connection, transaction) =>
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO error_logs (error_type, error_message, stack_trace, user_id, context_data)
                        VALUES (@error_type, @error_message, @stack_trace, @user_id, @context_data)
                    ";

                    command.Parameters.AddWithValue("@error_type", errorType);
                    command.Parameters.AddWithValue("@error_message", errorMessage);
                    command.Parameters.AddWithValue("@stack_trace", stackTrace ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@user_id", userId ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@context_data", contextData ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                    return true;
                });
            }
            catch
            {
                // Ignore logging errors to prevent infinite loops
            }
        }

        /// <summary>
        /// Log sync operation
        /// </summary>
        public async Task LogSyncOperationAsync(string syncType, string direction, int recordsCount, int successCount, int failedCount, string errorMessage = null, DateTime? startedAt = null, TimeSpan? duration = null)
        {
            try
            {
                await ExecuteInTransactionAsync(async (connection, transaction) =>
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO sync_logs (sync_type, direction, records_count, success_count, failed_count, error_message, started_at, completed_at, duration_seconds)
                        VALUES (@sync_type, @direction, @records_count, @success_count, @failed_count, @error_message, @started_at, @completed_at, @duration_seconds)
                    ";

                    var startTime = startedAt ?? DateTime.Now;
                    var completedAt = duration.HasValue ? startTime.Add(duration.Value) : DateTime.Now;

                    command.Parameters.AddWithValue("@sync_type", syncType);
                    command.Parameters.AddWithValue("@direction", direction);
                    command.Parameters.AddWithValue("@records_count", recordsCount);
                    command.Parameters.AddWithValue("@success_count", successCount);
                    command.Parameters.AddWithValue("@failed_count", failedCount);
                    command.Parameters.AddWithValue("@error_message", errorMessage ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@started_at", startTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@completed_at", completedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@duration_seconds", duration?.TotalSeconds ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                    return true;
                });
            }
            catch
            {
                // Ignore logging errors to prevent infinite loops
            }
        }

        #endregion

        #region Statistics and Monitoring

        /// <summary>
        /// Get comprehensive pending sync counts
        /// </summary>
        public async Task<(int registrations, int verifications, int totalOperations)> GetPendingCountsAsync()
        {
            return await ExecuteAsync(async connection =>
            {
                int regCount = 0;
                int verCount = 0;
                int totalCount = 0;

                // Count registrations
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM queued_operations WHERE operation_type = 'FingerprintRegistration' AND sync_status = 'pending'";
                    regCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Count verifications
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM queued_operations WHERE operation_type = 'FingerprintVerification' AND sync_status = 'pending'";
                    verCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Count total pending operations
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM queued_operations WHERE sync_status = 'pending'";
                    totalCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                return (regCount, verCount, totalCount);
            });
        }

        /// <summary>
        /// Get database statistics for monitoring
        /// </summary>
        public async Task<DatabaseStatistics> GetDatabaseStatisticsAsync()
        {
            return await ExecuteAsync(async connection =>
            {
                var stats = new DatabaseStatistics();

                // Student counts
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM students";
                    stats.TotalStudents = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM students WHERE fingerprint_template IS NOT NULL";
                    stats.StudentsWithFingerprints = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Queue statistics
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM queued_operations WHERE sync_status = 'pending'";
                    stats.PendingOperations = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM queued_operations WHERE sync_status = 'failed'";
                    stats.FailedOperations = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Cache statistics
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM students WHERE cache_expires_at < @now";
                    command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    stats.ExpiredCacheEntries = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                // Database size
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA page_count";
                    var pageCount = Convert.ToInt32(await command.ExecuteScalarAsync());
                    
                    command.CommandText = "PRAGMA page_size";
                    var pageSize = Convert.ToInt32(await command.ExecuteScalarAsync());
                    
                    stats.DatabaseSizeBytes = pageCount * pageSize;
                }

                return stats;
            });
        }

        /// <summary>
        /// Cleanup old data to maintain performance
        /// </summary>
        public async Task<int> CleanupOldDataAsync(int daysToKeep = 30)
        {
            return await ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                int totalDeleted = 0;
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var cutoffString = cutoffDate.ToString("yyyy-MM-dd HH:mm:ss");

                // Clean old error logs
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM error_logs WHERE occurred_at < @cutoff";
                    command.Parameters.AddWithValue("@cutoff", cutoffString);
                    totalDeleted += await command.ExecuteNonQueryAsync();
                }

                // Clean old sync logs
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM sync_logs WHERE started_at < @cutoff";
                    command.Parameters.AddWithValue("@cutoff", cutoffString);
                    totalDeleted += await command.ExecuteNonQueryAsync();
                }

                // Clean completed operations
                await CleanupCompletedOperationsAsync(daysToKeep);

                // Vacuum database to reclaim space
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "VACUUM";
                    await command.ExecuteNonQueryAsync();
                }

                return totalDeleted;
            });
        }

        #endregion

        #region Synchronous Wrapper Methods (for backward compatibility)

        /// <summary>
        /// Get setting value by key (synchronous wrapper)
        /// </summary>
        public string GetSetting(string key)
        {
            return GetSettingAsync(key).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Set setting value (synchronous wrapper)
        /// </summary>
        public void SetSetting(string key, string value)
        {
            SetSettingAsync(key, value).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get all settings as AppSettings object (synchronous wrapper)
        /// </summary>
        public AppSettings GetAppSettings()
        {
            try
            {
                return GetAppSettingsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get app settings from database, using defaults", ex);
                // Return default settings if database fails
                return new AppSettings();
            }
        }

        /// <summary>
        /// Save AppSettings object to database (synchronous wrapper)
        /// </summary>
        public void SaveAppSettings(AppSettings settings)
        {
            SaveAppSettingsAsync(settings).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Save user session to database (synchronous wrapper)
        /// </summary>
        public void SaveUserSession(User user)
        {
            SaveUserAsync(user).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get active user session from database (synchronous wrapper)
        /// </summary>
        public User GetActiveSession()
        {
            return ExecuteAsync(async connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM users 
                    WHERE token IS NOT NULL 
                        AND token_expires_at > @now 
                    ORDER BY last_login_at DESC 
                    LIMIT 1
                ";
                command.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapUser(reader);
                }

                return null;
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Clear all user sessions (synchronous wrapper)
        /// </summary>
        public void ClearAllSessions()
        {
            ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE users 
                    SET token = NULL, 
                        token_expires_at = NULL 
                    WHERE token IS NOT NULL
                ";

                await command.ExecuteNonQueryAsync();
                return true;
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get comprehensive pending sync counts (synchronous wrapper)
        /// </summary>
        public (int registrations, int verifications, int totalOperations) GetPendingCounts()
        {
            return GetPendingCountsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get all colleges (synchronous wrapper for role-based UI)
        /// </summary>
        public List<College> GetAllColleges()
        {
            return GetCollegesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get college by ID (synchronous wrapper for role-based UI)
        /// </summary>
        public College GetCollegeById(int collegeId)
        {
            return ExecuteAsync(async connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM colleges WHERE id = @id AND is_active = 1";
                command.Parameters.AddWithValue("@id", collegeId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapCollege(reader);
                }

                return null;
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Search students with college filtering (synchronous wrapper for role-based UI)
        /// </summary>
        public List<Student> SearchStudents(string searchTerm = null, List<int> collegeIds = null)
        {
            return ExecuteAsync(async connection =>
            {
                var students = new List<Student>();
                var sql = new StringBuilder("SELECT * FROM students WHERE 1=1");
                var parameters = new List<SqliteParameter>();

                // Add search term filter
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    sql.Append(" AND (roll_number LIKE @search OR name LIKE @search OR father_name LIKE @search OR cnic LIKE @search)");
                    parameters.Add(new SqliteParameter("@search", $"%{searchTerm}%"));
                }

                // Add college filter
                if (collegeIds != null && collegeIds.Any())
                {
                    var collegeParams = string.Join(",", collegeIds.Select((id, index) => $"@college{index}"));
                    sql.Append($" AND college_id IN ({collegeParams})");
                    
                    for (int i = 0; i < collegeIds.Count; i++)
                    {
                        parameters.Add(new SqliteParameter($"@college{i}", collegeIds[i]));
                    }
                }

                sql.Append(" ORDER BY name LIMIT 100");

                using var command = connection.CreateCommand();
                command.CommandText = sql.ToString();
                foreach (var param in parameters)
                {
                    command.Parameters.Add(param);
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    students.Add(MapStudent(reader));
                }

                return students;
            }).GetAwaiter().GetResult();
        }

        #endregion

        #region Cache Management Methods

        /// <summary>
        /// Save colleges batch for caching
        /// </summary>
        public async Task SaveCollegesBatchAsync(IEnumerable<Models.College> colleges)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();
                try
                {
                    // Clear existing colleges first
                    await connection.ExecuteAsync("DELETE FROM colleges", transaction: transaction);

                    // Insert new colleges
                    const string insertSql = @"
                        INSERT INTO colleges (id, name, district, address, contact_number, email, is_active, created_at, updated_at, sync_status)
                        VALUES (@Id, @Name, @District, @Address, @ContactNumber, @Email, @IsActive, @CreatedAt, @UpdatedAt, @SyncStatus)";

                    foreach (var college in colleges)
                    {
                        await connection.ExecuteAsync(insertSql, new
                        {
                            college.Id,
                            college.Name,
                            college.District,
                            college.Address,
                            college.ContactNumber,
                            college.Email,
                            college.IsActive,
                            college.CreatedAt,
                            college.UpdatedAt,
                            college.SyncStatus
                        }, transaction);
                    }

                    transaction.Commit();
                    Logger.Info($"Saved {colleges.Count()} colleges to cache");
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save colleges batch", ex);
                throw;
            }
        }

        /// <summary>
        /// Get cached colleges
        /// </summary>
        public async Task<List<Models.College>> GetCollegesAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT id, name, district, address, contact_number, email, is_active, created_at, updated_at, sync_status
                    FROM colleges 
                    WHERE is_active = 1
                    ORDER BY name";

                var colleges = await connection.QueryAsync<Models.College>(sql);
                return colleges.ToList();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get colleges", ex);
                return new List<Models.College>();
            }
        }

        /// <summary>
        /// Clear student cache
        /// </summary>
        public async Task ClearStudentCacheAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync("DELETE FROM students WHERE sync_status = 'cached'");
                Logger.Info("Student cache cleared");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear student cache", ex);
                throw;
            }
        }

        /// <summary>
        /// Clear college cache
        /// </summary>
        public async Task ClearCollegeCacheAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync("DELETE FROM colleges");
                Logger.Info("College cache cleared");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear college cache", ex);
                throw;
            }
        }

        /// <summary>
        /// Get cache metadata
        /// </summary>
        public async Task<CacheMetadata> GetCacheMetadataAsync(string cacheType)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    SELECT cache_type, record_count, last_updated, created_at
                    FROM cache_metadata 
                    WHERE cache_type = @CacheType";

                var metadata = await connection.QueryFirstOrDefaultAsync<CacheMetadata>(sql, new { CacheType = cacheType });
                return metadata;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get cache metadata for {cacheType}", ex);
                return null;
            }
        }

        /// <summary>
        /// Update cache metadata
        /// </summary>
        public async Task UpdateCacheMetadataAsync(string cacheType, int recordCount)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                const string sql = @"
                    INSERT OR REPLACE INTO cache_metadata (cache_type, record_count, last_updated, created_at)
                    VALUES (@CacheType, @RecordCount, @LastUpdated, COALESCE((SELECT created_at FROM cache_metadata WHERE cache_type = @CacheType), @LastUpdated))";

                await connection.ExecuteAsync(sql, new
                {
                    CacheType = cacheType,
                    RecordCount = recordCount,
                    LastUpdated = DateTime.Now
                });

                Logger.Info($"Updated cache metadata for {cacheType}: {recordCount} records");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to update cache metadata for {cacheType}", ex);
                throw;
            }
        }

        /// <summary>
        /// Clear cache metadata
        /// </summary>
        public async Task ClearCacheMetadataAsync()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync("DELETE FROM cache_metadata");
                Logger.Info("Cache metadata cleared");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear cache metadata", ex);
                throw;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Cleanup any managed resources if needed
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Database statistics for monitoring
    /// </summary>
    public class DatabaseStatistics
    {
        public int TotalStudents { get; set; }
        public int StudentsWithFingerprints { get; set; }
        public int PendingOperations { get; set; }
        public int FailedOperations { get; set; }
        public int ExpiredCacheEntries { get; set; }
        public long DatabaseSizeBytes { get; set; }
    }

    /// <summary>
    /// Queued operation model for enhanced queue management
    /// </summary>
    public class QueuedOperation
    {
        public int Id { get; set; }
        public string OperationType { get; set; }
        public string OperationData { get; set; }
        public int Priority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ScheduledAt { get; set; }
        public int SyncAttempts { get; set; }
        public int MaxAttempts { get; set; }
        public string LastError { get; set; }
        public string SyncStatus { get; set; }
        public DateTime? LastSyncAttempt { get; set; }
        public DateTime? SyncedAt { get; set; }
    }
}