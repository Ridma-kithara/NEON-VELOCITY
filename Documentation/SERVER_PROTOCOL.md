# NEON VELOCITY — Server Protocol (`nv1`)

**Authority:** `Backend/server.py`  
**Protocol version:** `nv1`  
**Server version:** `1.0.0`  
**Transport:** plain TCP, UTF-8, one JSON object per line  
**Default endpoint:** `0.0.0.0:7777`

This document describes the wire contract implemented by the Python authority and the Unity
`RaceClient`. The server is the source of truth for matchmaking, the countdown, race seeds,
snapshot validation, final ranking, rewards and the persistent leaderboard. Clients never
submit placement or reward amounts.

> Online play is available to native builds only. WebGL has no raw TCP socket API and
> `RaceClient` refuses online connections in browser builds; offline modes remain available.

---

## 1. Framing and connection rules

| Rule | Value |
|---|---|
| Transport | TCP, no TLS, no WebSocket |
| Encoding | UTF-8 JSON object per line (`\n` terminated) |
| Maximum line | 64 KiB (65,536 bytes) |
| Inbound message rate | 90 messages per connection per second |
| Server-added fields | Every server message carries `protocol: "nv1"` and `serverTimeMs` |
| Request correlation | Clients may send `requestId`; direct replies echo it |
| Default client snapshot rate | 10 Hz (`100 ms`) |
| Server state broadcast | About 4 Hz (`250 ms`) during a race |
| Idle keepalive | Client sends `ping` every 3 s; server replies `pong` |
| Results retention | 120 s after finalisation, then the room is deleted |
| Reconnect grace | 45 s |

A message larger than 64 KiB, or more than 90 messages in a one-second window, produces an
`error` and the server closes the connection. Malformed JSON produces `error` without closing
the connection. A non-object JSON value is rejected with `MESSAGE MUST BE AN OBJECT`.

There is no explicit protocol negotiation. A client and server built from the same release
should be used together.

### Message envelope

Client requests contain at least:

```json
{ "type": "quick_match", "requestId": "c12", "playerId": "p_...", "token": "...", "name": "KIRA" }
```

`requestId`, `playerId`, `token` and `name` are not trusted for routing after authentication;
the socket's authenticated account identifies the sender. Server messages add `protocol` and
`serverTimeMs`, plus `requestId` when the message is a direct reply.

```json
{ "type": "pong", "requestId": "c12", "protocol": "nv1", "serverTimeMs": 1790000000000 }
```

`serverTimeMs` is Unix epoch milliseconds. Clients use it, together with `pong` round-trip
timing, to estimate a server clock offset and display the countdown against `startAtMs`.

---

## 2. Message summary

### Client → server

| Type | Fields | Authentication | Purpose |
|---|---|---|---|
| `ping` | — | No | Clock/keepalive probe; answered with `pong` |
| `hello` | `name`, `clientVersion` | No | Create an ephemeral account and receive a token |
| `reconnect` | `playerId`, `token`, `name` | No (token proves identity) | Restore a session and live room |
| `leaderboard` | `mode` | No | Query the persistent online ladder |
| `quick_match` | — | Yes | Join an open public lobby or create one |
| `create_room` | — | Yes | Create a five-character private room |
| `join_room` | `roomCode` | Yes | Join a private or public room by code |
| `ready` | `ready` (bool) | Yes | Set lobby readiness |
| `start_race` | — | Yes, host only | Validate readiness, add bots, start countdown |
| `snapshot` | Race fields, see §6 | Yes, racing only | Report local runner state |
| `finish` | Race fields plus `finished`, see §7 | Yes | Submit end-of-run state |
| `leave_room` | — | Yes | Leave the current room |

### Server → client

| Type | Purpose |
|---|---|
| `welcome` | New session: `playerId`, `token`, sanitised `name` |
| `welcome_back` | Re-auth or repeated `hello` |
| `session_expired` | Reconnect token invalid/expired; server closes the socket |
| `pong` | Keepalive/clock reply |
| `error` | Command or integrity failure, with human-readable `error` |
| `room_state` | Full room roster and phase (lobby/countdown/racing/finished) |
| `countdown` | Race seed and server start time; sent at start |
| `race_start` | Race is live; seed and start time confirmed |
| `state_sync` | Periodic race roster snapshot, also used for late/reconnected clients |
| `race_results` | Per-player placement, verified rewards and final roster |
| `leaderboard` | Query result entries |
| `left_room` | Confirmation of leaving a room |

### `WirePlayer` roster object

All roster messages use this shape:

| Field | Type | Meaning |
|---|---|---|
| `playerId` | string | Server-issued human ID (`p_…`) or bot ID (`bot_<room>_<n>`) |
| `name` | string | Sanitised display name |
| `ready` | bool | Lobby readiness; bots are ready |
| `host` | bool | Current room host |
| `bot` | bool | Server-controlled pacing agent |
| `connected` | bool | Human socket currently attached |
| `finished` | bool | Runner completed the timed circuit |
| `distance` | float | Last validated distance in metres |
| `speed` | float | Last reported/validated speed |
| `score` | int | Capped score |
| `coins` | int | Capped credit pickups |
| `lane` | int | `0`, `1`, `2` |
| `runnerState` | string | `run`, `jump`, `slide`, `dead` or `finished` |
| `placement` | int | `1`–`5`, or `0` before finalisation |
| `lastSeenMs` | long | Server epoch milliseconds of last update |
| `survivalTime` | float | Validated survival seconds |
| `lives` | int | Reported lives, clamped `0`–`10` |
| `rewardsCredits` | int | Server-computed reward (results/finished state only) |
| `rewardsXp` | int | Server-computed XP reward |

Bots never receive messages and never appear in the persistent leaderboard.

---

## 3. Authentication and reconnection

### New session

```text
C: {"type":"hello","requestId":"c1","name":"KIRA","clientVersion":"1.0.0"}
S: {"type":"welcome","ok":true,"requestId":"c1","playerId":"p_1a2b3c4d5e6f",
    "token":"…","name":"KIRA","serverTimeMs":…,"protocol":"nv1","clientVersion":"1.0.0"}
```

- Player IDs are `p_` plus 12 random hex characters.
- Tokens are `secrets.token_urlsafe(24)` — 24 random bytes represented as 32 URL-safe characters.
- Accounts are in memory only. A server restart invalidates every ID, token and room.
- Names are sanitised to at most 20 characters using letters, digits, space, `_` and `-`;
  an empty result becomes `RUNNER`.
- Repeating `hello` on an authenticated connection returns `welcome_back` instead of a new
  account. It may update the account name.

### Reconnect

```text
C: {"type":"reconnect","requestId":"c9","playerId":"p_1a2b3c4d5e6f","token":"…","name":"KIRA"}
S: {"type":"welcome_back","ok":true,"requestId":"c9","playerId":"p_…","token":"…","name":"KIRA", …}
```

After `welcome_back`, the server restores room context when the account is still in a room:

1. sends `room_state`;
2. if the room is in `countdown` or `racing`, sends `race_start`;
3. if the room is `racing`, sends `state_sync`;
4. if the room is `finished`, replays that player's stored `race_results`;
5. broadcasts the updated roster to the other clients.

Rules:

- A token is accepted only while its account exists and the token matches
  (constant-time comparison).
- On invalid or expired credentials the server sends `session_expired` and closes.
- A newer connection for the same account replaces the old socket.
- The account remains in its room for **45 seconds** after a socket drop. During that time the
  runner is marked `connected: false`, speed is zeroed and its last validated distance is kept.
- A disconnected runner does not block finalisation. If still absent at finalisation, the
  runner is not eligible for competitive rewards or a leaderboard entry.
- The Unity client makes up to three automatic reconnect attempts, two seconds apart, after
  the first one-second delay. Restarting the application loses the in-memory token.

### Host reassignment

If the host leaves or expires while humans remain, the first connected human in roster order
becomes host. If no humans remain, the room is deleted.

---

## 4. Matchmaking and room lifecycle

### Quick match

- If already in a room, returns the existing `room_state`.
- Otherwise chooses the public `lobby` room with the most humans that still has fewer than
  five humans; ties are broken by room code.
- If no candidate exists, creates a new public room with the caller as host.
- Public rooms are not joined by bots until the host starts the race.

### Private rooms

`create_room` always creates a new private room and moves the caller into it. Room codes are
five characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (ambiguous `I`, `O`, `0`, `1` are
excluded). `join_room` sanitises the supplied code to uppercase alphanumerics, looks it up
exactly, and rejects:

- unknown code → `ROOM NOT FOUND`;
- room not in `lobby` → `RACE ALREADY IN PROGRESS`;
- five humans already present → `ROOM IS FULL`.

A player already in the target room simply receives its current `room_state`. Joining another
room removes the player from the previous room first.

### Phases

```text
lobby ──host start_race──▶ countdown ──server clock──▶ racing ──finish/time──▶ finished
                                                          │                     │
                                                          └── results retained ─┘
                                                              120 s, then deleted
```

| Phase | Behaviour |
|---|---|
| `lobby` | Humans join, toggle readiness, chat is not implemented; bots are not present yet |
| `countdown` | Seed, `startAtMs` and final roster are fixed; no new joiners; clients count down |
| `racing` | Snapshots accepted; bots simulated and state broadcast every 250 ms |
| `finished` | Final ranking and rewards fixed; results retained and replayed to reconnects |

### Start validation

`start_race` is accepted only when:

- the caller is the room host;
- the room is still in `lobby`;
- every human is connected and `ready`;
- at least one human exists.

Otherwise the server returns one of `ONLY THE HOST CAN START`, `RACE ALREADY STARTED`,
`A RUNNER IS RECONNECTING`, `ALL RUNNERS MUST BE READY` or `NO RUNNERS IN ROOM`.

On success the server:

1. generates a seed in `1..2,000,000,000`;
2. sets `startAtMs = now + countdown × 1000` (default 5 s);
3. sets `raceId = "<roomCode>-<startAtMs>"`;
4. fills empty slots with bots up to five total runners;
5. broadcasts `room_state` followed by `countdown` with the final roster;
6. launches the countdown/race task.

At `startAtMs` the phase becomes `racing`; `race_start` and a new `room_state` are broadcast.
The race ends at `startAtMs + raceDurationSeconds` (default 120 s), or earlier once every
connected human has submitted a finish/death. A 650 ms collection grace allows late finish
packets after the server clock end.

### Bots

Bots fill empty slots only at start. Each bot gets a deterministic base speed in
`17.0–23.5 m/s` derived from `seed:playerId`, with a small sinusoidal distance wobble. Bots do
not simulate obstacles and do not crash; they are pacing agents that produce final distances,
scores, coins and placements. They never receive rewards and never enter the leaderboard.

### Room messages

`room_state`:

```json
{
  "type": "room_state", "roomCode": "K7X2Q", "hostId": "p_…", "phase": "lobby",
  "privateRoom": true, "maxPlayers": 5, "seed": 0, "startAtMs": 0, "raceId": "",
  "raceDurationSeconds": 120.0, "serverTimeMs": 1790000000000,
  "players": [ { "playerId": "p_…", "name": "KIRA", "ready": false, "host": true,
                 "bot": false, "connected": true, "finished": false, "distance": 0.0,
                 "speed": 0.0, "score": 0, "coins": 0, "lane": 1, "runnerState": "run",
                 "placement": 0, "lastSeenMs": 1790000000000, "survivalTime": 0.0,
                 "lives": 1, "rewardsCredits": 0, "rewardsXp": 0 } ]
}
```

`countdown` and `race_start` carry `roomCode`, `phase`, `privateRoom`, `seed`, `startAtMs`,
`raceId`, `raceDurationSeconds` and `players`. `countdown` also carries `hostId` and
`maxPlayers`. `state_sync` carries `hostId`, `roomCode`, `phase`, `privateRoom`, `seed`,
`startAtMs`, `raceId`, `raceDurationSeconds` and `players`.

A finished `room_state` is **not** a result packet. Clients apply rewards only from
`race_results`, including the copy replayed after reconnecting to a finished room.

---

## 5. Countdown and clock

The server sends wall-clock `startAtMs`; clients estimate the server offset from
`serverTimeMs` and `pong` round trips, then display the remaining seconds. The countdown
duration is configurable with `--countdown` and is echoed in the room/race messages only
implicitly through `startAtMs`; `raceDurationSeconds` is the race length, not the countdown.

`raceId` is stable for the life of the started room and is used by the client to make reward
application idempotent locally.

---

## 6. Race snapshots

The Unity client sends one `snapshot` every 100 ms while racing:

```json
{
  "type": "snapshot", "requestId": "c42", "distance": 812.4, "speed": 24.7,
  "score": 9140, "coins": 31, "lane": 1, "runnerState": "run", "lives": 1,
  "nearMisses": 4, "powerUpsUsed": 2, "survivalTime": 33.1
}
```

Snapshots are ignored outside the `racing` phase, for bots, and after a finish was received.

### Validation

| Check | Server rule |
|---|---|
| Finite values | Non-finite numbers fall back to the previous value |
| Distance range | Clamped to `0 … 1,000,000` |
| Hard distance cap | `45 m/s × elapsed + 80 m` |
| Rewind | More than 8 m behind the previous validated distance is rejected |
| Advance | More than `45 m/s × seconds since last snapshot + 35 m` is rejected |
| Speed | Clamped to `0 … 60 m/s` |
| Score | At most `distance × 30 + 10,000` |
| Coins | At most `distance × 2 + 200` |
| Lane | Integer `0`–`2`; anything else becomes `1` |
| Runner state | One of `run`, `jump`, `slide`, `dead`, `finished`; otherwise `run` |
| Lives | `0`–`10` |
| Near misses | `0`–`50,000` |
| Power-ups used | `0`–`500` |
| Survival | `0 … elapsed + 10 s` |

On a rejected advance/rewind, the previous distance is kept and `violations` increments.
The client is warned on violations 1 and 5. At **8 violations** the runner is disqualified:

- `disqualified = true`, `dead = true`, `finished = false`, `finish_received = true`;
- `runnerState` becomes `dead`;
- the client receives `RACE INTEGRITY VIOLATION - RESULT INVALIDATED`;
- the server broadcasts the roster and may finalise immediately if everyone else is done.

Accepted snapshots are monotonic: distance is `max(previous, reported)`, score and coins are
also raised but never beyond their distance-relative caps.

Snapshots are the authority for online distance, but they are not a replay of the track
simulation. A modified client can still operate within the envelope; see
[KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).

---

## 7. Finish handling

A `finish` uses the same race fields as a snapshot plus `finished: true|false`. The client
sends it when the local runner completes the timed circuit (`finished: true`) or crashes
(`finished: false`, `runnerState: "dead"`). Duplicate finishes are ignored.

Validation:

- The room must be in `countdown` or `racing`.
- If the client claims `finished` before `server elapsed + 0.5 s >= raceDuration`, the packet is
  rejected with `RACE STILL RUNNING - FINISH REJECTED`; the run continues.
- The distance cap is `45 m/s × min(elapsed, duration + 5 s) + 100 m`.
- A negative or over-cap distance marks the result unverified and falls back to the last
  validated distance.
- A finish that leaps more than `35 m + 45 m/s × max(0.5 s, time since last snapshot)` beyond
  the last validated distance is unverified.
- Eight or more prior snapshot violations force an unverified result.
- Survival is clamped to `elapsed + 10 s`; score and coins use the same caps as snapshots.

A verified finish sets `finished: true`. A death, rejected distance or disqualified runner
sets `dead: true` and receives no competitive rewards. In all cases `finish_received` becomes
true and speed is zeroed. If all connected humans are now done, the room finalises
immediately; otherwise a `state_sync` is broadcast.

### Missing finish packet

If a connected human has sent at least one snapshot but no finish packet by the final whistle,
the server treats the last validated snapshot as the final state, marks the runner finished if
it was not dead, and ranks that distance. No client-supplied final statistics are required.

---

## 8. Ranking, rewards and leaderboard

### Ranking

Final order, implemented identically in Python `rank_key` and C# `RaceRules.CompareRank`:

1. greater validated `distance`;
2. `finished` ahead of not finished;
3. greater `score`;
4. greater `survivalTime`;
5. greater `coins`;
6. display name, case-insensitive ascending.

Placements are assigned `1..N` over the final room roster, including bots.

### Competitive rewards

A human is reward-eligible only if **all** of the following hold:

- not a bot and not disqualified;
- `finished` and `finish_received`;
- still connected at finalisation;
- validated distance of at least **20 m**.

| Reward | Formula |
|---|---|
| Placement credits | `1st: 500`, `2nd: 320`, `3rd: 220`, `4th: 150`, `5th: 100` |
| Distance credits | `min(250, floor(distance / 8))` |
| Total credits | placement + distance credits |
| XP | `60 + min(1500, floor(distance × 0.35)) + max(0, (6 − placement) × 20)` |

Ineligible runners receive zero credits and zero XP. Rewards are computed only by the server
and are delivered in the individual `race_results` message; the client cannot submit amounts.

### Online leaderboard

Only server-verified results are recorded. An entry requires:

- not disqualified;
- a received finish (including the server's missing-finish fallback);
- at least 20 m validated distance;
- `verified = connected` at finalisation — disconnected runners are skipped by the store.

Mode is `PrivateRace` for private rooms and `OnlineRace` for public matchmaking. Bots are
excluded.

Leaderboard queries are unauthenticated:

```json
{ "type": "leaderboard", "requestId": "c7", "mode": "circuit" }
```

- `mode` values `circuit`, empty or `all` return every mode; any other value is an exact,
  case-insensitive mode filter.
- Results are folded to the best entry per sanitised name + mode (there is no persistent
  account ID), sorted by distance descending, then score descending, then name ascending.
- The default and maximum returned count is 50.
- The Unity leaderboard screen requests `circuit`.

### `race_results`

Each human receives their own message; `place` and reward fields differ per recipient while
the roster is shared:

```json
{
  "type": "race_results", "ok": true, "raceId": "K7X2Q-1790000000000",
  "phase": "finished", "roomCode": "K7X2Q", "place": 2,
  "rewardsCredits": 421, "rewardsXp": 389, "rewardGranted": true,
  "serverTimeMs": 1790000120000,
  "players": [ … final WirePlayer roster with placement and reward fields … ],
  "message": "SERVER-VERIFIED RESULT"
}
```

`rewardGranted` is true only when `rewardsCredits > 0`.

### Persistence

The leaderboard is the only persisted state:

- default path: `Backend/data/leaderboard.json` (`--data` overrides the directory);
- JSON envelope: `{"version":1,"savedAtUtc":"…","entries":[…]}`;
- writes use a temporary file and atomic `os.replace`;
- storage is capped at the last 1,000 appended entries, while queries fold to the best
  entry per name + mode;
- a corrupt/unreadable file logs a warning and starts an empty ladder;
- a server restart keeps the leaderboard but loses accounts, tokens and rooms.

---

## 9. Errors and limits

### Common errors

| Error text | Trigger |
|---|---|
| `MISSING MESSAGE TYPE` | No non-empty string `type` |
| `MALFORMED JSON` | Invalid JSON line; connection stays open |
| `MESSAGE MUST BE AN OBJECT` | JSON array/scalar instead of object |
| `MESSAGE TOO LARGE` | Line above 64 KiB; connection closes |
| `RATE LIMIT EXCEEDED` | More than 90 messages in one second; connection closes |
| `UNKNOWN MESSAGE TYPE` | Unrecognised command |
| `NOT AUTHENTICATED` | Command requires an account |
| `INTERNAL SERVER ERROR` | Unexpected dispatcher exception |
| `SESSION EXPIRED - START A NEW CIRCUIT` | Invalid/expired reconnect token; socket closes |
| `ROOM NOT FOUND` | Unknown room code |
| `RACE ALREADY IN PROGRESS` | Joining a non-lobby room |
| `ROOM IS FULL` | Five humans already present |
| `NOT IN A LOBBY` / `PLAYER NOT IN ROOM` | Readiness outside a valid lobby |
| `NOT IN A ROOM` / `RACE ALREADY STARTED` | Start attempted outside a lobby |
| `ONLY THE HOST CAN START` | Non-host called `start_race` |
| `NO RUNNERS IN ROOM` | Empty human roster at start |
| `A RUNNER IS RECONNECTING` | A human is disconnected at start |
| `ALL RUNNERS MUST BE READY` | At least one human not ready |
| `SNAPSHOT REJECTED - SERVER CLAMPED DISTANCE` | Violation 1 or 5 |
| `RACE INTEGRITY VIOLATION - RESULT INVALIDATED` | Eighth snapshot violation |
| `ROOM NO LONGER EXISTS` | Finish after room deletion |
| `RACE IS NOT RUNNING` | Finish outside countdown/racing |
| `PLAYER NOT IN RACE` | Finish for a non-roster player |
| `RACE STILL RUNNING - FINISH REJECTED` | Early finish claim |

`error` messages use `ok: false` and include `requestId` where available. The connection
remains usable unless the text says it closes (line/rate/session errors).

### Hard limits

| Limit | Value |
|---|---|
| Total runners per room | 5 including bots |
| Humans per room | 5 |
| Room code | 5 characters, 32-character alphabet |
| Player name | 20 characters after sanitisation |
| Inbound line size | 64 KiB |
| Inbound rate | 90 messages/second/connection |
| Snapshot validation speed | 45 m/s envelope, 60 m/s reported speed cap |
| Race duration | 120 s default (`--duration`) |
| Countdown | 5 s default (`--countdown`) |
| Reconnect grace | 45 s |
| Results retention | 120 s |
| Leaderboard return count | 50 |
| Leaderboard stored entries | 1,000 |

There is no connection-count, per-IP or bandwidth limit beyond the per-connection rules, and
no TLS, account service or cluster coordination.

---

## 10. Configuration

```bash
python3 Backend/server.py --host 0.0.0.0 --port 7777 \
  --data Backend/data --countdown 5 --duration 120 --log-level INFO
```

| Flag | Environment | Default |
|---|---|---|
| `--host` | `NV_HOST` | `0.0.0.0` |
| `--port` | `NV_PORT` | `7777` |
| `--data` | `NV_DATA` | `Backend/data` |
| `--countdown` | `NV_COUNTDOWN` | `5` |
| `--duration` | `NV_DURATION` | `120` |
| `--log-level` | `NV_LOG_LEVEL` | `INFO` |

The server is a single `asyncio` process and requires Python 3.9+ with only the standard
library.

---

## 11. Security model and verification

- **No transport security.** `nv1` is plaintext TCP with bearer tokens. Use a trusted LAN,
  VPN or another protected path. The stock Unity client cannot use TLS.
- **Ephemeral identity.** Tokens live only in memory. There are no passwords, accounts,
  recovery, device linking or moderation tools.
- **Server-side outcomes.** Bots, countdown, seed, ranking, rewards and leaderboard writes are
  server-owned. Clients cannot choose a placement, reward or final distance outside the
  validated envelope.
- **Bounded anti-cheat.** The snapshot/finish rules reject impossible movement and repeated
  violations, but they are not cryptographic replay verification. See
  [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).
- **WebGL unsupported.** Browsers cannot open the raw TCP socket; online is intentionally
  blocked there.

Executed evidence for this contract is in `Backend/tests/test_server.py` (9 integration tests)
and [TEST_REPORT.md](TEST_REPORT.md). Tests cover authentication, two- and five-human races,
bots, private rooms, readiness/host rules, ranking/rewards, persistence, early-finish
rejection, snapshot attacks, dead-run rewards, missing finish fallback, reconnection and
malformed/unauthenticated input.
