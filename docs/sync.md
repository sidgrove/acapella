# Acapella sync (Acapella by Sidgrove Intelligence)

Everything Acapella learns (dictionary, learnt fixes and suggestions, history, settings and
corrected recordings) syncs between Dave's PCs through Sidgrove Intelligence
(`https://intelligence.sidgrove.com`). The app stays local-first: it works offline and syncs
in the background when it can.

The server never sees the database key; the app holds only its own device token.

## Signing in (loopback + PKCE, RFC 8252)

1. The app picks a free port, listens on `http://127.0.0.1:{port}/callback/` and makes a
   random `state` and a `code_verifier` (43+ chars, base64url). `code_challenge` is
   base64url(SHA-256(verifier)) with no padding.
2. It opens the browser at
   `GET /api/acapella/connect?redirect_uri=http://127.0.0.1:{port}/callback/&state={state}&code_challenge={challenge}&device={machine name}`.
   Sidgrove Intelligence signs the user in if needed (internal @sidgrove.com accounts only),
   then redirects to `{redirect_uri}?code={code}&state={state}`. Codes last 5 minutes and work
   once. On refusal it redirects with `?error={reason}&state={state}` instead.
3. The app checks `state`, then calls `POST /api/acapella/token` with JSON
   `{"code","code_verifier","redirect_uri"}` and gets `{"token","email"}`. The token looks like
   `sg_acapella_` + 43 base64url chars and is stored with DPAPI, never in settings.json.
4. Every other call sends `Authorization: Bearer {token}`. A 401 means signed out: drop the
   token and show "Sign in again". `DELETE /api/acapella/token` revokes the caller's token
   (used by Sign out).

## Sync

`POST /api/acapella/sync` with JSON:

```json
{ "cursor": 0, "device": "SIDGROVE_9950X",
  "changes": [ { "kind": "history", "key": "3ca2387d-...", "data": { }, "deleted": false,
                 "updatedAt": "2026-10-02T13:17:45.712Z" } ] }
```

Reply:

```json
{ "cursor": 1234, "more": false,
  "changes": [ { "kind": "...", "key": "...", "data": { }, "deleted": false,
                 "updatedAt": "...", "device": "..." } ] }
```

* `kind` is one of `dictionary`, `suggestion`, `history`, `settings`, `recording`.
* `key` is 1 to 300 characters; `data` is a JSON object (or null when deleted), at most 64 KB.
* At most 500 changes per call, 4 MB per body.
* The server keeps one row per (user, kind, key). A push wins only if its `updatedAt` is
  later than the stored one (last writer wins); deletes are kept as tombstones.
* The reply holds rows changed since `cursor` (up to 500 and about 3 MB, in order), including
  the caller's own writes coming back. Call again with the new cursor while `more` is true.
* A pushed change that lost (an equal or later copy was already there, for example because
  this PC's clock is behind) comes back in `changes` as the server's copy, whatever the
  cursor. The app takes it: the server has decided.
* A user's pushes take turns on the server, so seqs commit in order and a cursor never skips
  a row that committed late.
* A device token unused for 180 days stops working; the app asks Dave to sign in again.
* The server treats `data` as opaque; its shape is the app's business (see below).

## Recordings

`POST /api/acapella/recordings` with `{"key": "{history record Id}", "action": "upload" | "download"}`.

* `upload` returns `{"url"}`: `PUT` the WAV bytes there with `Content-Type: audio/wav`
  (overwrites). Then push a `recording` item with the same key so other PCs know it exists.
* `download` returns `{"url"}` (valid 5 minutes) to `GET`; 404 when there is no file.
* WAVs are at most 50 MB.

## Keys

Dave keeps every AI provider key in Sidgrove Intelligence. A signed-in PC is handed them and
keeps its own encrypted copy, so a dictation never waits on the server for a key.

`GET /api/acapella/keys` with the bearer token. Reply:

```json
{ "keys": { "gemini": "...", "elevenLabs": "...", "anthropic": null, "aiGateway": "..." } }
```

* A `null` means the server holds no such key. Properties the app does not know are ignored.
* 401 is the same as on every other route: signed out, so the token and the stored keys go.
* 404 is an older server without the route: no managed keys, said in the log and not to
  the user. Keys already stored are left as they are.
* Any other failure is a warning in the log; the stored keys stay in use and the app asks
  again a few minutes later.

The app asks straight after a sign-in, once at start-up when already signed in, and then at
most once every 12 hours, beside a sync run and never inside one. Being signed in is enough;
sync need not be switched on. Sign-out deletes the stored keys.

They live in `managed-keys.bin` in the data folder, encrypted with DPAPI for this Windows
user like the device token, never in `settings.json` and never in the log (which only says
how many of the four arrived). A reply replaces all four, so a key removed on the server is
removed here at the next refresh.

Which key a call uses, decided afresh on every call so a new key needs no restart:

1. the key typed into Settings on this PC, if there is one;
2. otherwise the key from Sidgrove Intelligence;
3. otherwise the environment variable on this PC (`GEMINI_API_KEY`, `ELEVENLABS_API_KEY`,
   `ANTHROPIC_API_KEY`, `AI_GATEWAY_API_KEY`).

| Reply property | Used for | Settings field it sits behind |
|---|---|---|
| `gemini` | AI clean-up, and cloud hearing when Gemini is the provider | Gemini API key |
| `elevenLabs` | "Also hear me in the cloud" | none in Settings (`ElevenLabsApiKey` in the file) |
| `anthropic` | AI clean-up with a Claude model | Anthropic API key |
| `aiGateway` | Jev decisions | Vercel AI Gateway key |

## What syncs and how (app side)

| Kind | Key | Data | Notes |
|---|---|---|---|
| `dictionary` | `Term\|{write}` or `Correction\|{hear lower-cased}\|{write}` | `{ "line": "<dictionary.txt line>" }` | Editing an entry is a delete of the old key plus a new key. The enabled flag lives in the line. |
| `suggestion` | suggestion Id | the `DictionarySuggestion` JSON | |
| `history` | record Id | the `TranscriptRecord` JSON | |
| `settings` | `preferences` | synced settings fields, API keys included | Machine-specific fields never sync: microphone, model folder, hotkey, onboarding, on/off. |
| `recording` | history record Id | `{ "at": "<record At, ISO>" }` | Only corrected recordings sync; the file goes to `recordings\corrected\` named from `At` in local time. |

Change detection is by snapshot: `sync-state.json` keeps the cursor and a hash per
(kind, key) as last synced. Each run builds the current items from the stores, pushes what is
new or changed (and a tombstone for what has gone), then applies what came back. A pulled
change whose hash matches what is held locally is ignored.
