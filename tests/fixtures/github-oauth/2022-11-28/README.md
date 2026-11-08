# github-oauth fixtures (2022-11-28)

Recorded 2026-11-08 against github.com with the public client ID of the D3SK Updates GitHub App; no secret, no sign-in completed.

Real answers (device and user codes replaced by fixed fakes): `device-code.json`, `authorization-pending.json`, `slow-down.json`
(a second poll right after the first), `refresh-rejected.json` (a refresh grant for a fake refresh token and no client secret:
GitHub answers `incorrect_client_credentials`), `api-401.json` (`GET /user` with a fake bearer token, body only).

Synthesized from GitHub's documented shape, every token fake (`ghu_FAKE...`, `ghr_FAKE...`): `token.json`, `refresh.json`, `user.json`.
