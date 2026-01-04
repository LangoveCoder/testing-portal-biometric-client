# Laravel API Endpoints for BACT Biometric Client

## Required API Endpoints

You need to create these endpoints in your Laravel backend to support the Windows desktop app.

---

## 1. Authentication Endpoints

### POST /api/auth/login
**Purpose:** Authenticate user and return JWT token

**Request Body:**
```json
{
  "email": "operator@example.com",
  "password": "password123",
  "role": "operator"
}
```

**Response (Success - 200):**
```json
{
  "token": "eyJ0eXAiOiJKV1QiLCJhbGc...",
  "user": {
    "id": 1,
    "name": "John Doe",
    "email": "operator@example.com",
    "role": "operator",
    "assigned_tests": [1, 2, 3]
  }
}
```

---

### POST /api/auth/refresh
**Purpose:** Refresh expired JWT token

**Headers:**
```
Authorization: Bearer {token}
```

**Response (Success - 200):**
```json
{
  "token": "eyJ0eXAiOiJKV1QiLCJhbGc..."
}
```

---

## 2. Download Data Endpoints

### GET /api/operator/download-data
**Purpose:** Download all students assigned to this operator

**Headers:**
```
Authorization: Bearer {token}
```

**Response (Success - 200):**
```json
{
  "tests": [
    {
      "id": 1,
      "name": "Entry Test 2025",
      "test_date": "2025-03-15"
    }
  ],
  "students": [
    {
      "id": 123,
      "roll_number": "00123",
      "name": "Ahmed Ali",
      "father_name": "Ali Khan",
      "cnic": "42101-1234567-1",
      "gender": "male",
      "picture": "base64_encoded_image_here",
      "test_photo": "base64_encoded_image_here",
      "test_id": 1,
      "test_name": "Entry Test 2025",
      "college_id": 5,
      "college_name": "Govt College",
      "venue": "Venue A",
      "hall": "Hall 1",
      "zone": "Zone A",
      "row": "1",
      "seat": "3",
      "fingerprint_template": "base64_template_or_null",
      "fingerprint_image": "base64_image_or_null"
    }
  ]
}
```

---

### GET /api/college/download-data
**Purpose:** Download all students for this college admin

**Headers:**
```
Authorization: Bearer {token}
```

**Response:** Same structure as operator endpoint

---

## 3. Upload Registrations Endpoint

### POST /api/operator/sync-registrations
**Purpose:** Upload fingerprint registrations from desktop app

**Headers:**
```
Authorization: Bearer {token}
Content-Type: application/json
```

**Request Body:**
```json
[
  {
    "student_id": 123,
    "roll_number": "00123",
    "fingerprint_template": "base64_template_string",
    "fingerprint_image": "base64_image_string",
    "quality_score": 85,
    "captured_at": "2025-12-22 14:30:00"
  },
  {
    "student_id": 124,
    "roll_number": "00124",
    "fingerprint_template": "base64_template_string",
    "fingerprint_image": "base64_image_string",
    "quality_score": 92,
    "captured_at": "2025-12-22 14:32:00"
  }
]
```

**Response (Success - 200):**
```json
{
  "success": true,
  "synced_count": 2,
  "failed": []
}
```

**Response (Partial Success - 200):**
```json
{
  "success": true,
  "synced_count": 1,
  "failed": [
    {
      "roll_number": "00124",
      "error": "Student not found"
    }
  ]
}
```

---

## 4. Upload Verifications Endpoint

### POST /api/college/sync-verifications
**Purpose:** Upload verification logs from desktop app

**Headers:**
```
Authorization: Bearer {token}
Content-Type: application/json
```

**Request Body:**
```json
[
  {
    "student_id": 123,
    "roll_number": "00123",
    "match_result": "match",
    "confidence_score": 95.5,
    "entry_allowed": true,
    "verified_at": "2025-12-22 08:15:00"
  }
]
```

**Response (Success - 200):**
```json
{
  "success": true,
  "synced_count": 1,
  "failed": []
}
```

---

## 5. Sync Status Endpoint

### GET /api/sync/status
**Purpose:** Get sync status information

**Headers:**
```
Authorization: Bearer {token}
```

**Response (Success - 200):**
```json
{
  "last_sync": "2025-12-22T14:30:00Z",
  "pending_registrations": 5,
  "pending_verifications": 10
}
```

---

## Implementation Notes

### 1. Database Schema Updates
You need to add these columns to your `students` table (if not already present):
- `fingerprint_template` (text, nullable)
- `fingerprint_image` (longtext or blob, nullable)
- `fingerprint_quality` (integer, nullable)
- `fingerprint_registered_at` (timestamp, nullable)

### 2. Create Verification Logs Table
```sql
CREATE TABLE biometric_verifications (
    id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    student_id BIGINT UNSIGNED NOT NULL,
    roll_number VARCHAR(50) NOT NULL,
    match_result ENUM('match', 'no_match') NOT NULL,
    confidence_score DECIMAL(5,2),
    entry_allowed BOOLEAN DEFAULT false,
    verified_at TIMESTAMP NOT NULL,
    verifier_id BIGINT UNSIGNED,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (student_id) REFERENCES students(id)
);
```

### 3. Authentication
- Use Laravel Sanctum or JWT for API authentication
- Token should expire after 8 hours
- Support token refresh

### 4. Authorization
- Operators can only access students assigned to their tests
- College admins can only access students from their college
- Use Laravel policies for authorization

### 5. Image Handling
- Accept images as Base64 strings
- Decode and store in database or file storage
- Return as Base64 in download endpoints

### 6. Error Handling
Return proper HTTP status codes:
- 200: Success
- 401: Unauthorized (invalid/expired token)
- 403: Forbidden (no permission)
- 404: Not Found
- 422: Validation Error
- 500: Server Error

---

## Testing the API

Use these files in your `Services` folder:
1. **ApiService.cs** - Add to Services folder
2. **SyncService.cs** - Add to Services folder

Then update your `AuthService.cs` to use the real API instead of simulated login.

---

## Next Steps

1. Build these API endpoints in Laravel
2. Test with Postman/Insomnia
3. Update API URL in desktop app settings
4. Test download/upload functionality
5. Enable auto-sync in desktop app
