# US-01: User registration

`POST /api/auth/register` accepts JSON:

```json
{
  "firstName": "Alice",
  "lastName": "Traveller",
  "email": "alice@example.com",
  "password": "StrongPass1!",
  "confirmPassword": "StrongPass1!",
  "phoneNumber": "+37120000000"
}
```

First name, last name, email, password and confirmation are required.
Names allow up to 100 characters, email 256, passwords 128 and phone 30.
Phone is optional; omit it or send null when absent.
Passwords need at least 8 characters, including uppercase, lowercase, a digit
and a special character. Confirmation must match exactly. Passwords are never trimmed.

Responses:

- `201`: registration successful; a message only, with no credentials or user entity.
- `400`: invalid input or a password that fails Identity validation.
- `409`: the email is already registered (case insensitive).
- `503`: registration could not be saved; retry later. No partial account is committed.

Identity hashes passwords and enforces unique emails. Email is also the username,
so Identity's existing unique normalized-username index protects concurrent inserts.
A simultaneous duplicate insert may return 503; retrying returns 409.
Startup seeds Client and Admin through RoleManager before HTTP requests are served.
Existing roles are preserved; seeding creates no user accounts. Startup fails if
required roles cannot be initialized. It does not create or migrate database tables.
Registration only assigns Client and never creates roles. User creation and
membership assignment share a transaction; either failure rolls back the account.
If Client is missing after startup, registration returns 503 without recreating it.
Email confirmation is not required, and no confirmation email is sent.

The built-in Identity endpoint mapping was removed to prevent a second registration
route bypassing required profile fields and role assignment. Focused login and
refresh endpoints preserve Identity authentication without mapping another registration.
Recovery and account-management endpoints remain outside this change.

## Identity login and refresh

`POST /api/auth/login` accepts `email` and `password`, plus optional
`twoFactorCode` or `twoFactorRecoveryCode` for accounts with two-factor authentication.
By default, Identity returns its own opaque bearer access and refresh tokens,
not JWTs. Invalid credentials and locked-out accounts return 401.
Email confirmation is not required for login in this version.

Use `?useCookies=true` for a persistent Identity cookie or
`?useSessionCookies=true` for a session cookie. Session-cookie selection takes
precedence when both flags are true. Browser clients using cookies must account
for CSRF protection on future state-changing endpoints.

`POST /api/auth/refresh` accepts `{ "refreshToken": "..." }` and returns
new Identity access and refresh tokens. Invalid, expired and security-stamp-revoked
refresh tokens return 401. Tokens are protected and issued by Identity's built-in
handlers. Access-token expiry follows Identity configuration; changing a security
stamp invalidates refresh tokens, not already issued access tokens immediately.

## Local setup and checks

Use .NET SDK 10 and supply `ConnectionStrings:DefaultConnection` through
User Secrets in Development or `ConnectionStrings__DefaultConnection` in the environment.
Use a MySQL account allowed to insert users, roles and memberships in the existing database.
Start the API with the HTTPS launch profile:

```sh
dotnet run --project TravelBookingSystem --launch-profile https
```

Build and run tests from the repository root:

```sh
dotnet build TravelBookingSystem.sln
dotnet test TravelBookingSystem.Tests/TravelBookingSystem.Tests.csproj
```

The integration tests use a separate in-memory SQLite database per test with real
Identity managers and password hashing. They never connect to MySQL or apply the
existing migrations. They cover profile persistence, optional phone, password
hash verification, validation, case-insensitive duplicate emails, Client-only
assignment, startup role seeding and failure, rollback after assignment exceptions
and unsuccessful Identity results, route uniqueness, login, cookies, lockout,
refresh-token expiry and security-stamp revocation. MySQL-specific behavior
still needs verification against a dedicated test database.
