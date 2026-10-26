# US-03: Cookie logout

`POST /api/auth/logout` requires an authenticated Identity application cookie and
a valid ASP.NET Core antiforgery token. It calls `SignInManager.SignOutAsync()` and
returns **204 No Content** with cookie-expiration headers. No database records are
changed, and no custom session store or bearer-token revocation is introduced.

## Frontend flow

After cookie login, fetch `GET /api/auth/antiforgery`. It returns
`{ "requestToken": "..." }` and sets a separate HttpOnly antiforgery cookie.
The response must not be cached. Get a fresh request token after each login or
account change; keep the request token in memory.

Send that token in the `X-CSRF-TOKEN` header on logout. The browser must send both
the authentication and antiforgery cookies. For a same-origin frontend:

```js
const tokenResponse = await fetch('/api/auth/antiforgery', {
  credentials: 'same-origin',
  cache: 'no-store'
});
if (!tokenResponse.ok) throw new Error('Could not get logout token');
const { requestToken } = await tokenResponse.json();

const response = await fetch('/api/auth/logout', {
  method: 'POST',
  credentials: 'same-origin',
  headers: { 'X-CSRF-TOKEN': requestToken }
});
if (response.status !== 204) throw new Error('Logout failed');
// Clear displayed account details and return to the login screen.
// A subsequent GET /api/auth/me with the updated cookies returns 401.
```

Use HTTPS. Cookie security remains Secure, HttpOnly and SameSite=Lax.
Antiforgery validation uses the framework's cookie/request-token pair; the request
token alone, a token from another browser cookie jar, or a missing token is insufficient.
See [ASP.NET Core antiforgery documentation](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Responses and scope

- **204**: sign-out succeeded; apply all Set-Cookie headers.
- **400**: authenticated cookie request with missing or invalid antiforgery token;
  the user stays signed in.
- **401**: no valid application cookie, including bearer-only requests. Both the
  token endpoint and logout are restricted to the Identity application-cookie scheme.
- A repeated logout after the browser removes its authentication cookie returns 401.

Logout removes cookies from the current browser. A saved copy of a previously
issued cookie is not globally revoked; other devices are not signed out. Access
and refresh tokens remain valid under their existing rules. If a caller retains a
bearer Authorization header, `/me` can still authenticate it after cookie logout.

The antiforgery endpoint is deliberately authenticated: fetch the token after login.
This story protects logout only. Login and future cookie-authenticated write
operations still need an appropriate frontend antiforgery flow before production.
Cross-origin frontend hosting also requires a deliberate credentials/CORS policy.

## Tests

The suite uses isolated in-memory SQLite databases. It checks session and persistent
cookie deletion, `/me` after logout, missing/invalid/mismatched tokens, cookie-only
authorization, route uniqueness, unchanged database records, and unaffected bearer
access/refresh tokens. No tests connect to the existing MySQL database.

```sh
dotnet build TravelBookingSystem.sln
dotnet test TravelBookingSystem.Tests/TravelBookingSystem.Tests.csproj
```
