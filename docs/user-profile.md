# US-04: User profile management

Use the existing `GET /api/auth/me` to retrieve the authenticated user's profile.
It returns only `id`, `firstName`, `lastName`, `email`, `phoneNumber`, and `roles`.
Phone number is the additional field introduced by this story. The response is not cached.

## Update flow

1. Log in with an Identity application cookie over HTTPS.
2. Fetch `GET /api/auth/antiforgery` and keep its `requestToken` in memory.
3. Send `PUT /api/users/me` with cookies and `X-CSRF-TOKEN: <requestToken>`:

   ```json
   {
     "firstName": "Alice",
     "lastName": "Traveller",
     "email": "new@example.com",
     "phoneNumber": "+37120000000"
   }
   ```

4. On success, apply the refreshed authentication cookie and reload `/api/auth/me`.
   Fetch a fresh antiforgery token before subsequent writes or logout.

This PUT replaces all four editable fields. Names and email are required. Names
allow 100 characters each, email 256 and phone 30. Phone is optional; omitted or
null clears it. Non-empty phone values must pass the framework Phone validator.
Names, email and phone are trimmed before storage.

The route uses the CookieSession policy, so bearer-only requests cannot update
profiles. Authentication comes from the cookie, never a body UserId. Extra JSON
fields are ignored and cannot alter roles, passwords, hashes, confirmation flags,
account IDs or another user's profile.

## Identity behavior

Only the four allowed profile fields are copied to the authenticated user.
Email remains the username used for login. `UserManager.UpdateAsync` validates
Identity uniqueness rules, normalizes both email and username, and persists the
profile in one database operation. An invalid or duplicate email cannot leave
only the name or phone saved. The existing unique normalized-username index also
guards concurrent attempts to use the same login email.

Changing email or phone clears its previous confirmation flag. Email confirmation
is still not required in this version. The current Identity cookie is refreshed
without changing whether it was persistent. Other sessions are not globally
revoked; roles and password hashes are unchanged.

Responses:

- **204**: saved; no response body.
- **400**: invalid fields, missing/invalid antiforgery token, or rejected Identity validation.
- **401**: no authenticated application cookie or the account no longer exists.
- **409**: duplicate email or Identity concurrency conflict; reload and retry as appropriate.
- **503**: database update unavailable; no exception details are exposed.

## Verification and limits

The full suite uses isolated SQLite databases and covers registration, login,
logout, profile persistence, login with a changed email, role/hash preservation,
CSRF rejection, validation, duplicate-email atomicity, targeting attempts and
database-failure handling. Models and migrations are unchanged. Live MySQL
integration and simultaneous-update testing remain pending; no existing MySQL
database is accessed by these tests.

For a production application, changing the login email should have a verified
email-change and reauthentication policy. Those flows are outside this story's
current no-email-confirmation scope.
