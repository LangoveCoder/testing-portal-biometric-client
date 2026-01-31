# Windows Biometric Client - API Integration Guide

**Version:** 1.0
**Last Updated:** January 31, 2026
**API Base URL:** `https://your-domain.com/api/v1`

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Authentication](#2-authentication)
3. [Operator Flow - Fingerprint Registration](#3-operator-flow---fingerprint-registration)
4. [Admin Flow - Fingerprint Verification](#4-admin-flow---fingerprint-verification)
5. [Offline Sync](#5-offline-sync)
6. [Error Handling](#6-error-handling)
7. [Testing Checklist](#7-testing-checklist)

---

## 1. Getting Started

### 1.1 Base Configuration

```
API Base URL: https://your-domain.com/api/v1
Content-Type: application/json
Accept: application/json
```

### 1.2 Authentication Header

After login, include this header in ALL requests:
```
Authorization: Bearer {your_token_here}
```

### 1.3 Response Format

All API responses follow this structure:

**Success Response:**
```json
{
    "success": true,
    "message": "Operation successful",
    "data": { ... }
}
```

**Error Response:**
```json
{
    "success": false,
    "message": "Error description",
    "error_code": "ERROR_CODE",
    "errors": { ... }
}
```

### 1.4 HTTP Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Created |
| 400 | Bad Request |
| 401 | Unauthenticated |
| 403 | Forbidden (no permission) |
| 404 | Not Found |
| 422 | Validation Error |
| 429 | Too Many Requests (rate limited) |
| 500 | Server Error |

---

## 2. Authentication

### 2.1 Login

**Endpoint:** `POST /auth/login`
**Auth Required:** No
**Rate Limit:** 10 requests per minute

**Request:**
```json
{
    "email": "operator@example.com",
    "password": "your_password",
    "device_info": "Windows 10 Pro - BACT Biometric Client v1.0"
}
```

**Success Response (200):**
```json
{
    "success": true,
    "message": "Login successful",
    "data": {
        "token": "1|abcdef123456789...",
        "token_type": "Bearer",
        "expires_at": "2026-03-02T10:30:00.000000Z",
        "user": {
            "id": 1,
            "name": "Muhammad Ahmed",
            "email": "operator@example.com",
            "role": "operator",
            "status": "active",
            "assigned_college_id": null,
            "last_login_at": "2026-01-31T10:30:00.000000Z"
        },
        "colleges": [
            {
                "id": 1,
                "name": "Government College Quetta",
                "district": "Quetta"
            },
            {
                "id": 2,
                "name": "Tameer-e-Nau College",
                "district": "Quetta"
            }
        ],
        "permissions": {
            "can_register": true,
            "can_verify": false,
            "can_mark_attendance": true
        }
    }
}
```

**Error Response (401):**
```json
{
    "success": false,
    "message": "Invalid email or password",
    "error_code": "INVALID_CREDENTIALS"
}
```

**Error Response (403 - Inactive Account):**
```json
{
    "success": false,
    "message": "Your account is inactive. Please contact administrator.",
    "error_code": "ACCOUNT_INACTIVE"
}
```

**Implementation Notes:**
- Store the `token` securely (Windows Credential Manager recommended)
- Check `user.role` to determine which screens to show:
  - `"operator"` → Show Registration screens
  - `"college_admin"` → Show Verification screens
- Store `colleges` array for dropdown selection
- Token is valid for 30 days

---

### 2.2 Get Current User Info

**Endpoint:** `GET /auth/me`
**Auth Required:** Yes

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "user": {
            "id": 1,
            "name": "Muhammad Ahmed",
            "email": "operator@example.com",
            "role": "operator",
            "status": "active"
        }
    }
}
```

**Use Case:** Verify token is still valid on app startup.

---

### 2.3 Logout

**Endpoint:** `POST /auth/logout`
**Auth Required:** Yes

**Success Response (200):**
```json
{
    "success": true,
    "message": "Logged out successfully"
}
```

**Implementation Notes:**
- Call this when user clicks logout
- Clear stored token after successful logout

---

### 2.4 Refresh Token

**Endpoint:** `POST /auth/refresh`
**Auth Required:** Yes

**Success Response (200):**
```json
{
    "success": true,
    "message": "Token refreshed",
    "data": {
        "token": "2|newtoken123456...",
        "expires_at": "2026-04-01T10:30:00.000000Z"
    }
}
```

**Use Case:** Refresh token before it expires (e.g., weekly).

---

## 3. Operator Flow - Fingerprint Registration

### 3.1 Get Available Tests

**Endpoint:** `GET /operator/tests/available`
**Auth Required:** Yes
**Role Required:** `operator`

**Optional Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| college_id | integer | Filter by specific college |

**Request Examples:**
```
GET /operator/tests/available
GET /operator/tests/available?college_id=1
```

**Success Response (200):**
```json
{
    "success": true,
    "message": "Available tests retrieved",
    "data": {
        "tests": [
            {
                "id": 1,
                "name": "College Lecturer Test 2026",
                "test_category": "college",
                "owner_name": "Government College Quetta",
                "test_date": "2026-02-15",
                "test_time": "09:00:00",
                "college_id": 1,
                "college_name": "Government College Quetta",
                "department_id": null,
                "department_name": null,
                "total_students": 150,
                "attendance_marked": 45,
                "attendance_pending": 105
            },
            {
                "id": 2,
                "name": "Education Department Recruitment",
                "test_category": "departmental",
                "owner_name": "Department of Education",
                "test_date": "2026-02-20",
                "test_time": "10:00:00",
                "college_id": null,
                "college_name": null,
                "department_id": 1,
                "department_name": "Department of Education",
                "total_students": 500,
                "attendance_marked": 0,
                "attendance_pending": 500
            }
        ]
    }
}
```

**Implementation Notes:**
- Show this as a dropdown: "Select Test"
- Display format: `{name} - {owner_name} ({test_date})`
- Store selected `test_id` for subsequent API calls
- Show `total_students` and progress stats in UI

---

### 3.2 Get Assigned Colleges

**Endpoint:** `GET /operator/colleges`
**Auth Required:** Yes
**Role Required:** `operator`

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "colleges": [
            {
                "id": 1,
                "name": "Government College Quetta",
                "district": "Quetta"
            },
            {
                "id": 2,
                "name": "Tameer-e-Nau College",
                "district": "Quetta"
            }
        ]
    }
}
```

**Use Case:** Populate college filter dropdown.

---

### 3.3 Search Student

**Endpoint:** `POST /operator/student/search`
**Auth Required:** Yes
**Role Required:** `operator`

**Request:**
```json
{
    "roll_number": "00001",
    "test_id": 1
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| roll_number | string | Yes | Student's roll number |
| test_id | integer | No | Filter by specific test (recommended) |

**Success Response (200):**
```json
{
    "success": true,
    "message": "Student found",
    "data": {
        "student": {
            "id": 123,
            "registration_id": 456,
            "roll_number": "00001",
            "name": "Ahmed Ali Khan",
            "father_name": "Mohammad Khan",
            "cnic": "5440012345678",
            "gender": "Male",
            "picture": "/9j/4AAQSkZJRg...(base64 encoded JPEG)...",
            "test_id": 1,
            "test_name": "College Lecturer Test 2026",
            "test_category": "college",
            "owner_name": "Government College Quetta",
            "college_id": 1,
            "college_name": "Government College Quetta",
            "department_id": null,
            "department_name": null,
            "venue": "City Campus",
            "hall": "A",
            "zone": 1,
            "row": 2,
            "seat": 15,
            "has_fingerprint": false,
            "fingerprint_registered_at": null,
            "fingerprint_quality": null
        }
    }
}
```

**Error Response (404):**
```json
{
    "success": false,
    "message": "Student not found in your assigned tests",
    "error_code": "STUDENT_NOT_FOUND"
}
```

**Implementation Notes:**
- Display student photo (decode base64 to image)
- Show student details in a card/panel
- Check `has_fingerprint`:
  - `false` → Show "Capture Fingerprint" button
  - `true` → Show "Re-capture Fingerprint" button with warning
- Display seating info: Hall {hall}, Zone {zone}, Row {row}, Seat {seat}

---

### 3.4 Save Fingerprint Registration

**Endpoint:** `POST /operator/fingerprint/save`
**Auth Required:** Yes
**Role Required:** `operator`
**Rate Limit:** 30 requests per minute

**Request:**
```json
{
    "roll_number": "00001",
    "fingerprint_template": "Rk1SACAyMAAAAAFcAAAB...(base64 encoded template)...",
    "fingerprint_image": "/9j/4AAQSkZJRg...(base64 encoded image, optional)...",
    "quality_score": 85,
    "captured_at": "2026-02-15T09:35:00Z"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| roll_number | string | Yes | Student's roll number |
| fingerprint_template | string | Yes | Base64 encoded fingerprint template |
| fingerprint_image | string | No | Base64 encoded fingerprint image (BMP/PNG) |
| quality_score | integer | Yes | Quality score 0-100 from scanner SDK |
| captured_at | datetime | No | ISO 8601 timestamp (defaults to server time) |

**Success Response (201):**
```json
{
    "success": true,
    "message": "Fingerprint registered successfully",
    "data": {
        "student_id": 123,
        "roll_number": "00001",
        "quality_score": 85,
        "registered_at": "2026-02-15T09:35:00Z",
        "registered_by": "Muhammad Ahmed"
    }
}
```

**Error Response (422 - Validation):**
```json
{
    "success": false,
    "message": "Validation failed",
    "errors": {
        "fingerprint_template": ["Invalid fingerprint template format"],
        "quality_score": ["Quality score must be at least 40"]
    }
}
```

**Error Response (422 - Low Quality):**
```json
{
    "success": false,
    "message": "Fingerprint quality too low. Minimum required: 40",
    "error_code": "LOW_QUALITY"
}
```

**Implementation Notes:**
- Minimum quality score: 40 (reject lower quality captures)
- Recommended quality score: 60+
- Template format: ISO 19794-2 or proprietary (depends on scanner)
- Show success message with green checkmark
- Auto-clear form and focus on roll number input for next student

---

### 3.5 Download Students for Offline Mode

**Endpoint:** `GET /operator/students`
**Auth Required:** Yes
**Role Required:** `operator`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| test_id | integer | No | Filter by test |
| college_id | integer | No | Filter by college |
| page | integer | No | Page number (default: 1) |
| per_page | integer | No | Items per page (default: 100, max: 500) |

**Request Example:**
```
GET /operator/students?test_id=1&per_page=500
```

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "students": [
            {
                "id": 123,
                "roll_number": "00001",
                "name": "Ahmed Ali Khan",
                "father_name": "Mohammad Khan",
                "cnic": "5440012345678",
                "test_id": 1,
                "has_fingerprint": false
            },
            {
                "id": 124,
                "roll_number": "00002",
                "name": "Fatima Bibi",
                "father_name": "Abdul Rashid",
                "cnic": "5440012345679",
                "test_id": 1,
                "has_fingerprint": true
            }
        ],
        "pagination": {
            "current_page": 1,
            "last_page": 3,
            "per_page": 500,
            "total": 1250
        }
    }
}
```

**Use Case:** Download all students for offline registration mode.

---

## 4. Admin Flow - Fingerprint Verification

### 4.1 Get College Info

**Endpoint:** `GET /admin/college`
**Auth Required:** Yes
**Role Required:** `college_admin`

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "college": {
            "id": 1,
            "name": "Government College Quetta",
            "district": "Quetta",
            "contact_person": "Dr. Ahmad Shah",
            "email": "gc.quetta@edu.pk",
            "phone": "081-9201234"
        }
    }
}
```

---

### 4.2 Get Students for Verification

**Endpoint:** `GET /admin/students`
**Auth Required:** Yes
**Role Required:** `college_admin`

**Query Parameters:**
| Parameter | Type | Description |
|-----------|------|-------------|
| test_id | integer | Filter by test |
| status | string | `all`, `verified`, `pending` |
| page | integer | Page number |

**Request Example:**
```
GET /admin/students?test_id=1&status=pending&page=1
```

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "students": [
            {
                "id": 123,
                "roll_number": "00001",
                "name": "Ahmed Ali Khan",
                "cnic": "5440012345678",
                "has_fingerprint": true,
                "is_verified": false,
                "verification_status": null
            },
            {
                "id": 124,
                "roll_number": "00002",
                "name": "Fatima Bibi",
                "cnic": "5440012345679",
                "has_fingerprint": true,
                "is_verified": true,
                "verification_status": "match"
            }
        ],
        "pagination": {
            "current_page": 1,
            "last_page": 2,
            "total": 150
        }
    }
}
```

---

### 4.3 Load Student for Verification (With Template)

**Endpoint:** `POST /admin/student/load`
**Auth Required:** Yes
**Role Required:** `college_admin`

**Request:**
```json
{
    "roll_number": "00001"
}
```

**Success Response (200):**
```json
{
    "success": true,
    "message": "Student loaded",
    "data": {
        "student": {
            "id": 123,
            "roll_number": "00001",
            "name": "Ahmed Ali Khan",
            "father_name": "Mohammad Khan",
            "cnic": "5440012345678",
            "gender": "Male",
            "picture": "/9j/4AAQSkZJRg...(base64)...",
            "test_id": 1,
            "test_name": "College Lecturer Test 2026",
            "fingerprint_template": "Rk1SACAyMAAAAAFc...(base64 template for matching)...",
            "fingerprint_image": "/9j/4AAQSkZJRg...(base64)...",
            "fingerprint_quality": 85,
            "fingerprint_registered_at": "2026-02-15T09:35:00Z"
        }
    }
}
```

**Error Response (400 - No Fingerprint):**
```json
{
    "success": false,
    "message": "Student does not have registered fingerprint",
    "error_code": "NO_FINGERPRINT"
}
```

**Implementation Notes:**
- Use `fingerprint_template` for local 1:1 matching
- Display `fingerprint_image` as reference
- Capture live fingerprint and compare with template
- Show match/no-match result to user

---

### 4.4 Save Verification Result

**Endpoint:** `POST /admin/verification/save`
**Auth Required:** Yes
**Role Required:** `college_admin`
**Rate Limit:** 60 requests per minute

**Request:**
```json
{
    "roll_number": "00001",
    "match_result": "match",
    "confidence_score": 92.5,
    "entry_allowed": true,
    "verified_at": "2026-03-01T10:15:00Z",
    "remarks": "Verified successfully - Entry granted"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| roll_number | string | Yes | Student's roll number |
| match_result | string | Yes | `"match"` or `"no_match"` |
| confidence_score | float | No | Match confidence 0-100 |
| entry_allowed | boolean | Yes | Was entry granted? |
| verified_at | datetime | No | ISO 8601 timestamp |
| remarks | string | No | Additional notes |

**Success Response (201):**
```json
{
    "success": true,
    "message": "Verification saved successfully",
    "data": {
        "verification_id": 789,
        "student_id": 123,
        "roll_number": "00001",
        "match_result": "match",
        "entry_allowed": true,
        "verified_at": "2026-03-01T10:15:00Z",
        "verified_by": "College Admin"
    }
}
```

---

### 4.5 Get Verification Statistics

**Endpoint:** `GET /admin/stats`
**Auth Required:** Yes
**Role Required:** `college_admin`

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "stats": {
            "total_students": 150,
            "with_fingerprint": 148,
            "verified_today": 45,
            "matches": 44,
            "no_matches": 1,
            "pending": 103
        }
    }
}
```

---

## 5. Offline Sync

### 5.1 Bulk Upload Fingerprint Registrations

**Endpoint:** `POST /sync/registrations`
**Auth Required:** Yes
**Role Required:** `operator`
**Rate Limit:** 10 requests per minute

**Request:**
```json
{
    "registrations": [
        {
            "roll_number": "00001",
            "fingerprint_template": "Rk1SACAyMAAAAAFc...",
            "fingerprint_image": "/9j/4AAQSkZJRg...",
            "quality_score": 85,
            "captured_at": "2026-02-15T09:35:00Z"
        },
        {
            "roll_number": "00002",
            "fingerprint_template": "Rk1SACAyMAAAAAGh...",
            "fingerprint_image": "/9j/4AAQSkZJRg...",
            "quality_score": 78,
            "captured_at": "2026-02-15T09:36:00Z"
        },
        {
            "roll_number": "00003",
            "fingerprint_template": "Rk1SACAyMAAAAAEk...",
            "quality_score": 92,
            "captured_at": "2026-02-15T09:37:00Z"
        }
    ]
}
```

**Success Response (200):**
```json
{
    "success": true,
    "message": "Sync completed",
    "data": {
        "total": 3,
        "successful": 3,
        "failed": 0,
        "details": [
            {
                "roll_number": "00001",
                "status": "success"
            },
            {
                "roll_number": "00002",
                "status": "success"
            },
            {
                "roll_number": "00003",
                "status": "success"
            }
        ]
    }
}
```

**Partial Success Response (200):**
```json
{
    "success": true,
    "message": "Sync completed with some failures",
    "data": {
        "total": 3,
        "successful": 2,
        "failed": 1,
        "details": [
            {
                "roll_number": "00001",
                "status": "success"
            },
            {
                "roll_number": "00002",
                "status": "failed",
                "error": "Student not found"
            },
            {
                "roll_number": "00003",
                "status": "success"
            }
        ]
    }
}
```

**Implementation Notes:**
- Maximum 100 registrations per request
- Store failed items locally for retry
- Show sync progress to user
- Retry failed items after some time

---

### 5.2 Bulk Upload Verifications

**Endpoint:** `POST /sync/verifications`
**Auth Required:** Yes
**Role Required:** `college_admin`
**Rate Limit:** 10 requests per minute

**Request:**
```json
{
    "verifications": [
        {
            "roll_number": "00001",
            "match_result": "match",
            "confidence_score": 92.5,
            "entry_allowed": true,
            "verified_at": "2026-03-01T10:15:00Z"
        },
        {
            "roll_number": "00002",
            "match_result": "no_match",
            "confidence_score": 25.0,
            "entry_allowed": false,
            "verified_at": "2026-03-01T10:16:00Z",
            "remarks": "Fingerprint did not match"
        }
    ]
}
```

---

### 5.3 Get Sync Status

**Endpoint:** `GET /sync/status`
**Auth Required:** Yes

**Success Response (200):**
```json
{
    "success": true,
    "data": {
        "pending_count": 5,
        "last_sync_at": "2026-02-15T10:00:00Z",
        "sync_status": "idle"
    }
}
```

---

## 6. Error Handling

### 6.1 Common Error Codes

| Error Code | HTTP Status | Description | Action |
|------------|-------------|-------------|--------|
| `UNAUTHENTICATED` | 401 | Token expired or invalid | Redirect to login |
| `INVALID_CREDENTIALS` | 401 | Wrong email/password | Show error message |
| `ACCOUNT_INACTIVE` | 403 | Account disabled | Contact admin |
| `INSUFFICIENT_PERMISSIONS` | 403 | Role mismatch | Show "Access Denied" |
| `STUDENT_NOT_FOUND` | 404 | Student doesn't exist | Check roll number |
| `NO_FINGERPRINT` | 400 | No registered fingerprint | Register first |
| `LOW_QUALITY` | 422 | Fingerprint quality < 40 | Recapture |
| `RATE_LIMITED` | 429 | Too many requests | Wait and retry |
| `VALIDATION_ERROR` | 422 | Invalid input | Check fields |

### 6.2 Error Handling Code Example (C#)

```csharp
public async Task<ApiResponse> HandleApiResponse(HttpResponseMessage response)
{
    var content = await response.Content.ReadAsStringAsync();
    var result = JsonConvert.DeserializeObject<ApiResponse>(content);

    switch (response.StatusCode)
    {
        case HttpStatusCode.OK:
        case HttpStatusCode.Created:
            return result;

        case HttpStatusCode.Unauthorized:
            // Token expired - redirect to login
            ClearStoredToken();
            NavigateToLogin();
            throw new AuthenticationException(result.Message);

        case HttpStatusCode.Forbidden:
            MessageBox.Show("Access Denied: " + result.Message);
            throw new UnauthorizedAccessException(result.Message);

        case HttpStatusCode.NotFound:
            return result; // Handle in calling code

        case HttpStatusCode.UnprocessableEntity:
            // Validation error - show field errors
            ShowValidationErrors(result.Errors);
            throw new ValidationException(result.Message);

        case HttpStatusCode.TooManyRequests:
            MessageBox.Show("Too many requests. Please wait a moment.");
            await Task.Delay(5000);
            throw new RateLimitException(result.Message);

        default:
            throw new ApiException(result.Message);
    }
}
```

---

## 7. Testing Checklist

### 7.1 Phase 1: Authentication

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 1 | Login with valid credentials | POST /auth/login | 200, token returned |
| 2 | Login with wrong password | POST /auth/login | 401, error message |
| 3 | Login with inactive account | POST /auth/login | 403, account inactive |
| 4 | Get user info with valid token | GET /auth/me | 200, user data |
| 5 | Get user info with invalid token | GET /auth/me | 401, unauthenticated |
| 6 | Logout | POST /auth/logout | 200, success |

### 7.2 Phase 2: Operator - Test Selection

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 7 | Get available tests | GET /operator/tests/available | 200, tests array |
| 8 | Get tests filtered by college | GET /operator/tests/available?college_id=1 | 200, filtered tests |
| 9 | Get assigned colleges | GET /operator/colleges | 200, colleges array |

### 7.3 Phase 3: Operator - Student Search

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 10 | Search existing student | POST /operator/student/search | 200, student data |
| 11 | Search with test_id filter | POST /operator/student/search | 200, student data |
| 12 | Search non-existent roll | POST /operator/student/search | 404, not found |
| 13 | Search student from other college | POST /operator/student/search | 404, not found |

### 7.4 Phase 4: Operator - Fingerprint Save

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 14 | Save valid fingerprint | POST /operator/fingerprint/save | 201, success |
| 15 | Save with low quality (< 40) | POST /operator/fingerprint/save | 422, low quality |
| 16 | Save without template | POST /operator/fingerprint/save | 422, validation error |
| 17 | Save for non-existent student | POST /operator/fingerprint/save | 404, not found |
| 18 | Re-save fingerprint (update) | POST /operator/fingerprint/save | 201, success |

### 7.5 Phase 5: Admin - Verification

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 19 | Load student with fingerprint | POST /admin/student/load | 200, template returned |
| 20 | Load student without fingerprint | POST /admin/student/load | 400, no fingerprint |
| 21 | Save match verification | POST /admin/verification/save | 201, success |
| 22 | Save no-match verification | POST /admin/verification/save | 201, success |
| 23 | Get verification stats | GET /admin/stats | 200, stats data |

### 7.6 Phase 6: Offline Sync

| # | Test Case | Endpoint | Expected |
|---|-----------|----------|----------|
| 24 | Bulk sync registrations | POST /sync/registrations | 200, results |
| 25 | Bulk sync with some failures | POST /sync/registrations | 200, partial success |
| 26 | Bulk sync verifications | POST /sync/verifications | 200, results |

---

## 8. Quick Reference Card

### Operator Flow
```
1. POST /auth/login           → Get token
2. GET  /operator/tests/available → Select test
3. POST /operator/student/search  → Find student
4. POST /operator/fingerprint/save → Save fingerprint
5. Repeat 3-4 for each student
6. POST /auth/logout          → End session
```

### Admin Flow
```
1. POST /auth/login           → Get token
2. GET  /admin/students       → List students
3. POST /admin/student/load   → Get template
4. [Local fingerprint matching]
5. POST /admin/verification/save → Save result
6. Repeat 3-5 for each student
7. POST /auth/logout          → End session
```

### Offline Sync Flow
```
1. POST /auth/login           → Get token
2. GET  /operator/students    → Download students
3. [Work offline - store locally]
4. POST /sync/registrations   → Upload when online
5. POST /auth/logout          → End session
```

---

**Document End**

For questions or issues, contact the backend development team.
