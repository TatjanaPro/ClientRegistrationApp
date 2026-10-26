# US-02: User login

Login and refresh reuse the Identity endpoints implemented during US-01.
No additional login route or JWT implementation is introduced.
Cookie logout is documented in [US-03](user-logout.md).

## Recommended browser flow

For a browser frontend hosted on the same site, use HTTPS and Identity cookies.
The application cookie is HttpOnly (JavaScript cannot read it), Secure
(HTTPS only) and SameSite=Lax. This follows Microsoft's preference for cookies
in browser applications:
https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0

1. Send `POST /api/auth/login?useSessionCookies=true` with JSON:

   ```json
   { "email": "alice@example.com", "password": "StrongPass1!" }
   ```

2. A successful login returns 200 with an Identity session cookie and an empty body.
   Use `?useCookies=true` instead only when a persistent login is wanted.
3. Call `GET /api/auth/me`. The browser sends its cookie automatically for same-origin
   requests. The response contains only `id`, `firstName`, `lastName`, `email`, and
   `phoneNumber`, `roles`, and is marked `Cache-Control: no-store`.

The frontend should handle HTTP 401 by displaying the login screen. Protected API
endpoints must use `[Authorize]` or `RequireAuthorization()`; middleware alone
does not protect every route. The weather sample remains public.

Wrong credentials and unknown accounts both return 401 with no account-specific
details. Failed attempts count toward Identity lockout. No email confirmation is
required. Accounts that enable two-factor authentication still need their code.

## Existing bearer support

Login without cookie query options preserves the existing behavior: Identity
issues opaque bearer access and refresh tokens. Set `Authorization: Bearer <accessToken>`
when calling `/api/auth/me`. The existing `POST /api/auth/refresh` remains available.
Both cookie and bearer principals retain assigned roles. `/me` reads the user's
current profile and roles from the store; role claims in an existing token/cookie
may remain older until that credential is refreshed or reissued.

## Deployment and remaining work

Use the HTTPS launch profile locally. A frontend on another origin needs an explicit
credentials/CORS configuration; a frontend on another site may also need a different
SameSite policy. No cross-origin policy is enabled in this story because the frontend
origin is not defined.

Cookies need CSRF protection on state-changing browser operations, including login;
SameSite=Lax is defense in depth, not a replacement for an antiforgery flow. Wire up
and test that flow with the frontend before production. Do not store bearer tokens
in local storage as a shortcut for the browser flow.

Logout is implemented in US-03. Live MySQL testing remains pending; the automated suite
uses isolated SQLite databases and does not access existing MySQL data.

## Verification

```sh
dotnet build TravelBookingSystem.sln
dotnet test TravelBookingSystem.Tests/TravelBookingSystem.Tests.csproj
```

Tests cover cookie and bearer profile access, role claims, safe response fields,
unauthenticated/invalid-token 401 responses, cookie security flags, identical invalid
credential responses, and the existing registration, refresh and rollback behavior.
