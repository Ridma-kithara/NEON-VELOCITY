#!/usr/bin/env python3
"""NEON VELOCITY authoritative race server.

A dependency-free asyncio TCP service for the Unity client. One JSON object per line using
the shared `nv1` wire format. The server owns matchmaking, the countdown, deterministic race
seeds, snapshot validation, final ranking, rewards and the persistent global leaderboard.

It is deliberately self-contained: Python 3.9+ standard library only. Run:

    python3 Backend/server.py --host 0.0.0.0 --port 7777

Security boundaries:
  * line size is capped before JSON parsing;
  * names and room codes are sanitised;
  * snapshot distances are clamped against a per-client speed envelope;
  * only the host can start a room, only ready humans are launched;
  * rewards are computed by the server and are never accepted from a client;
  * reconnect tokens expire after a configurable grace period;
  * WebGL cannot use this raw TCP service; native clients can.
"""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import dataclasses
import datetime as dt
import json
import logging
import math
import os
import pathlib
import random
import secrets
import signal
import sys
import time
from typing import Any, Dict, Iterable, List, Optional, Tuple

PROTOCOL = "nv1"
SERVER_VERSION = "1.0.0"
MAX_PLAYERS = 5
SNAPSHOT_INTERVAL = 0.10
MAX_SPEED = 45.0
MAX_REPORTED_SPEED = 60.0
SNAPSHOT_SLACK = 35.0
FINISH_EARLY_GRACE_SECONDS = 0.5
MAX_SCORE_PER_METRE = 30.0
MAX_COINS_PER_METRE = 2.0
MAX_LINE_BYTES = 64 * 1024
MAX_MESSAGES_PER_SECOND = 90
RECONNECT_GRACE_SECONDS = 45.0
RESULTS_RETENTION_SECONDS = 120.0
LEADERBOARD_LIMIT = 50
ROOM_CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
BOT_NAMES = ("NOVA-7", "KITE", "ECHO-3", "VANTA", "JAX-0", "RUNE", "HEX", "MIRA")

PLACEMENT_CREDITS = {1: 500, 2: 320, 3: 220, 4: 150, 5: 100}

logger = logging.getLogger("neon-velocity")


def now_ms() -> int:
    return int(time.time() * 1000)


def utc_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def sanitise_name(value: Any) -> str:
    if not isinstance(value, str):
        return "RUNNER"
    cleaned = "".join(ch for ch in value.strip()[:20] if ch.isalnum() or ch in " _-").strip()
    return cleaned or "RUNNER"


def sanitise_room_code(value: Any) -> str:
    if not isinstance(value, str):
        return ""
    return "".join(ch for ch in value.upper() if ch.isalnum())[:8]


def number(value: Any, default: float = 0.0, limit: float = 1_000_000.0) -> float:
    try:
        parsed = float(value)
    except (TypeError, ValueError):
        return default
    if not math.isfinite(parsed):
        return default
    return clamp(parsed, -limit, limit)


def integer(value: Any, default: int = 0, limit: int = 2_000_000_000) -> int:
    try:
        parsed = int(value)
    except (TypeError, ValueError):
        return default
    return max(-limit, min(limit, parsed))


def plausible_delta(previous: float, reported: float, seconds: float, max_speed: float = MAX_SPEED) -> bool:
    if not math.isfinite(previous) or not math.isfinite(reported):
        return False
    if reported + 8.0 < previous:
        return False
    return reported - previous <= max_speed * max(0.0, seconds) + SNAPSHOT_SLACK


def rank_key(player: "Player") -> Tuple[float, int, int, float, int, str]:
    """Matches NeonVelocity.Core.RaceRules.CompareRank (distance, finish, score, survival, coins)."""
    return (
        -player.distance,
        0 if player.finished else 1,
        -player.score,
        -player.survival_time,
        -player.coins,
        player.name.lower(),
    )


@dataclasses.dataclass
class Player:
    player_id: str
    name: str
    bot: bool = False
    host: bool = False
    connected: bool = True
    ready: bool = False
    finished: bool = False
    dead: bool = False
    disqualified: bool = False
    finish_received: bool = False
    distance: float = 0.0
    speed: float = 0.0
    score: int = 0
    coins: int = 0
    lane: int = 1
    runner_state: str = "run"
    lives: int = 1
    near_misses: int = 0
    power_ups: int = 0
    survival_time: float = 0.0
    placement: int = 0
    last_seen_ms: int = dataclasses.field(default_factory=now_ms)
    last_snapshot_at: float = 0.0
    snapshots: int = 0
    violations: int = 0
    finish_time_ms: int = 0
    rewards_credits: int = 0
    rewards_xp: int = 0
    base_speed: float = 18.0

    def reset_for_race(self, seed: int) -> None:
        self.ready = True
        self.finished = False
        self.dead = False
        self.disqualified = False
        self.finish_received = False
        self.distance = 0.0
        self.speed = 0.0
        self.score = 0
        self.coins = 0
        self.lane = 1
        self.runner_state = "run"
        self.lives = 1
        self.near_misses = 0
        self.power_ups = 0
        self.survival_time = 0.0
        self.placement = 0
        self.last_seen_ms = now_ms()
        self.last_snapshot_at = time.monotonic()
        self.snapshots = 0
        self.violations = 0
        self.finish_time_ms = 0
        self.rewards_credits = 0
        self.rewards_xp = 0
        if self.bot:
            rng = random.Random(f"{seed}:{self.player_id}")
            self.base_speed = rng.uniform(17.0, 23.5)
            self.speed = self.base_speed
            self.runner_state = "run"

    def wire(self) -> dict:
        return {
            "playerId": self.player_id,
            "name": self.name,
            "ready": self.ready,
            "host": self.host,
            "bot": self.bot,
            "connected": self.connected,
            "finished": self.finished,
            "distance": round(self.distance, 2),
            "speed": round(self.speed, 2),
            "score": self.score,
            "coins": self.coins,
            "lane": self.lane,
            "runnerState": self.runner_state,
            "placement": self.placement,
            "lastSeenMs": self.last_seen_ms,
            "survivalTime": round(self.survival_time, 2),
            "lives": self.lives,
            "rewardsCredits": self.rewards_credits,
            "rewardsXp": self.rewards_xp,
        }


@dataclasses.dataclass
class Account:
    player_id: str
    token: str
    name: str
    room_code: str = ""
    connection: Optional["ClientConnection"] = None
    disconnected_at: float = 0.0
    cleanup_task: Optional[asyncio.Task] = None


@dataclasses.dataclass
class Room:
    code: str
    private: bool
    host_id: str
    phase: str = "lobby"
    seed: int = 0
    race_id: str = ""
    start_at_ms: int = 0
    duration: float = 120.0
    players: Dict[str, Player] = dataclasses.field(default_factory=dict)
    race_task: Optional[asyncio.Task] = None
    results_players: Optional[List[dict]] = None
    results_by_player: Dict[str, dict] = dataclasses.field(default_factory=dict)
    cleanup_task: Optional[asyncio.Task] = None

    def human_players(self) -> List[Player]:
        return [p for p in self.players.values() if not p.bot]

    def connected_humans(self) -> List[Player]:
        return [p for p in self.players.values() if not p.bot and p.connected]

    def ordered_players(self) -> List[Player]:
        return sorted(self.players.values(), key=lambda p: (not p.host, p.bot, p.name.lower()))

    def wire(self) -> dict:
        return {
            "type": "room_state",
            "roomCode": self.code,
            "hostId": self.host_id,
            "phase": self.phase,
            "privateRoom": self.private,
            "maxPlayers": MAX_PLAYERS,
            "seed": self.seed,
            "startAtMs": self.start_at_ms,
            "raceId": self.race_id,
            "raceDurationSeconds": self.duration,
            "serverTimeMs": now_ms(),
            "players": [p.wire() for p in self.ordered_players()],
        }


class LeaderboardStore:
    """Small, atomic, best-per-player persistent ladder."""

    def __init__(self, path: pathlib.Path):
        self.path = path
        self.entries: List[dict] = []
        self._load()

    def _load(self) -> None:
        if not self.path.exists():
            return
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
            entries = raw.get("entries", []) if isinstance(raw, dict) else raw
            if isinstance(entries, list):
                self.entries = [e for e in entries if isinstance(e, dict)][:1000]
        except (OSError, ValueError, TypeError) as exc:
            logger.warning("leaderboard load failed: %s", exc)
            self.entries = []

    def _save(self) -> None:
        try:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            temp = self.path.with_suffix(self.path.suffix + ".tmp")
            payload = {"version": 1, "savedAtUtc": utc_iso(), "entries": self.entries[-1000:]}
            temp.write_text(json.dumps(payload, separators=(",", ":"), ensure_ascii=True), encoding="utf-8")
            os.replace(temp, self.path)
        except OSError as exc:
            logger.warning("leaderboard save failed: %s", exc)

    def record(self, entries: Iterable[dict]) -> None:
        changed = False
        for entry in entries:
            if not isinstance(entry, dict):
                continue
            verified = bool(entry.get("verified", True))
            distance = number(entry.get("distance"), 0.0, 1_000_000.0)
            if not verified or distance <= 0.0:
                continue
            self.entries.append({
                "name": sanitise_name(entry.get("name")),
                "distance": round(distance, 2),
                "score": max(0, integer(entry.get("score"))),
                "coins": max(0, integer(entry.get("coins"))),
                "mode": str(entry.get("mode") or "OnlineRace")[:32],
                "achievedUtc": str(entry.get("achievedUtc") or utc_iso())[:40],
                "verified": True,
            })
            changed = True
        if changed:
            # Keep storage bounded; the query folds duplicates by name+mode.
            self.entries = self.entries[-1000:]
            self._save()

    def query(self, mode: Optional[str] = None, limit: int = LEADERBOARD_LIMIT) -> List[dict]:
        best: Dict[Tuple[str, str], dict] = {}
        for entry in self.entries:
            entry_mode = str(entry.get("mode") or "OnlineRace")
            if mode and mode not in ("circuit", "", "all") and entry_mode.lower() != mode.lower():
                continue
            key = (sanitise_name(entry.get("name")).lower(), entry_mode)
            current = best.get(key)
            if current is None or number(entry.get("distance"), 0.0) > number(current.get("distance"), 0.0):
                best[key] = entry
        ordered = sorted(best.values(), key=lambda e: (-number(e.get("distance"), 0.0),
                                                       -integer(e.get("score")), str(e.get("name", ""))))
        result = []
        for rank, entry in enumerate(ordered[:max(1, limit)], start=1):
            result.append({
                "rank": rank,
                "name": sanitise_name(entry.get("name")),
                "distance": round(number(entry.get("distance"), 0.0), 2),
                "score": max(0, integer(entry.get("score"))),
                "coins": max(0, integer(entry.get("coins"))),
                "mode": str(entry.get("mode") or "OnlineRace"),
                "achievedUtc": str(entry.get("achievedUtc") or utc_iso())[:40],
                "verified": True,
            })
        return result


class ClientConnection:
    def __init__(self, server: "RaceServer", reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        self.server = server
        self.reader = reader
        self.writer = writer
        self.account: Optional[Account] = None
        self.closed = False
        self._send_lock = asyncio.Lock()
        self._window_started = time.monotonic()
        self._window_count = 0
        self._close_lock = asyncio.Lock()
        peer = writer.get_extra_info("peername")
        self.peer = f"{peer[0]}:{peer[1]}" if isinstance(peer, tuple) else str(peer)

    @property
    def player_id(self) -> str:
        return self.account.player_id if self.account else ""

    async def send(self, message: dict) -> None:
        if self.closed:
            return
        message.setdefault("protocol", PROTOCOL)
        message.setdefault("serverTimeMs", now_ms())
        try:
            payload = json.dumps(message, separators=(",", ":"), ensure_ascii=True) + "\n"
        except (TypeError, ValueError) as exc:
            logger.warning("serialisation failed for %s: %s", self.peer, exc)
            return
        try:
            async with self._send_lock:
                if self.closed:
                    return
                self.writer.write(payload.encode("utf-8"))
                await self.writer.drain()
        except (ConnectionError, OSError, RuntimeError):
            await self.close()

    async def send_error(self, text: str, request_id: str = "") -> None:
        await self.send({"type": "error", "ok": False, "error": text[:240], "requestId": request_id})

    async def run(self) -> None:
        logger.info("connection open %s", self.peer)
        try:
            while not self.closed:
                try:
                    line = await self.reader.readline()
                except (ConnectionError, asyncio.IncompleteReadError, OSError, ValueError):
                    await self.send_error("MESSAGE TOO LARGE")
                    break
                if not line:
                    break
                if len(line) > MAX_LINE_BYTES:
                    await self.send_error("MESSAGE TOO LARGE")
                    break
                if not self._allow_message():
                    await self.send_error("RATE LIMIT EXCEEDED")
                    break
                try:
                    message = json.loads(line.decode("utf-8"))
                except (UnicodeDecodeError, ValueError):
                    await self.send_error("MALFORMED JSON")
                    continue
                if not isinstance(message, dict):
                    await self.send_error("MESSAGE MUST BE AN OBJECT")
                    continue
                try:
                    await self.server.dispatch(self, message)
                except Exception:  # noqa: BLE001 - a bad message must never kill the server
                    logger.exception("dispatch failure from %s", self.peer)
                    await self.send_error("INTERNAL SERVER ERROR")
        finally:
            await self.close()

    def _allow_message(self) -> bool:
        now = time.monotonic()
        if now - self._window_started >= 1.0:
            self._window_started = now
            self._window_count = 0
        self._window_count += 1
        return self._window_count <= MAX_MESSAGES_PER_SECOND

    async def close(self) -> None:
        async with self._close_lock:
            if self.closed:
                return
            self.closed = True
        with contextlib.suppress(Exception):
            self.writer.close()
            await self.writer.wait_closed()
        await self.server.on_connection_closed(self)
        logger.info("connection closed %s", self.peer)


class RaceServer:
    def __init__(self, host: str, port: int, data_dir: pathlib.Path,
                 countdown_seconds: float = 5.0, race_duration: float = 120.0):
        self.host = host
        self.port = port
        self.countdown_seconds = max(0.1, countdown_seconds)
        self.race_duration = max(2.0, race_duration)
        self.accounts: Dict[str, Account] = {}
        self.rooms: Dict[str, Room] = {}
        self.connections: set[ClientConnection] = set()
        self.leaderboard = LeaderboardStore(data_dir / "leaderboard.json")
        self._server: Optional[asyncio.base_events.Server] = None
        self._stop = asyncio.Event()
        self._tasks: set[asyncio.Task] = set()

    # ---- lifecycle -----------------------------------------------------------------

    async def start(self) -> None:
        self._server = await asyncio.start_server(self._accept, self.host, self.port, limit=MAX_LINE_BYTES)
        sockets = self._server.sockets or []
        if sockets:
            self.port = sockets[0].getsockname()[1]
        logger.info("NEON VELOCITY authority listening on %s:%s", self.host, self.port)
        logger.info("countdown %.1fs, race %.1fs, max players %d", self.countdown_seconds,
                    self.race_duration, MAX_PLAYERS)

    async def serve_forever(self) -> None:
        if self._server is None:
            await self.start()
        async with self._server:
            await self._stop.wait()

    def stop(self) -> None:
        self._stop.set()

    async def shutdown(self) -> None:
        self._stop.set()
        for conn in list(self.connections):
            await conn.close()
        for room in list(self.rooms.values()):
            self._cancel_room_tasks(room)
        for task in list(self._tasks):
            task.cancel()
        if self._tasks:
            await asyncio.gather(*self._tasks, return_exceptions=True)
        if self._server is not None:
            self._server.close()
            with contextlib.suppress(Exception):
                await self._server.wait_closed()
        logger.info("authority stopped")

    def _spawn(self, coro) -> asyncio.Task:
        task = asyncio.create_task(coro)
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)
        return task

    async def _accept(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        conn = ClientConnection(self, reader, writer)
        self.connections.add(conn)
        await conn.run()

    async def on_connection_closed(self, conn: ClientConnection) -> None:
        self.connections.discard(conn)
        account = conn.account
        if account is None or account.connection is not conn:
            return
        account.connection = None
        account.disconnected_at = time.monotonic()
        room = self.rooms.get(account.room_code)
        if room and account.player_id in room.players:
            player = room.players[account.player_id]
            player.connected = False
            player.speed = 0.0
            player.last_seen_ms = now_ms()
            self.broadcast_room(room)
            logger.info("%s disconnected during %s; %s grace", account.name, room.phase,
                        RECONNECT_GRACE_SECONDS)
        if account.cleanup_task is None or account.cleanup_task.done():
            account.cleanup_task = self._spawn(self._expire_account(account.player_id, account.token))

    async def _expire_account(self, player_id: str, token: str) -> None:
        try:
            await asyncio.sleep(RECONNECT_GRACE_SECONDS)
        except asyncio.CancelledError:
            return
        account = self.accounts.get(player_id)
        if account is None or account.token != token:
            return
        if account.connection is not None:
            return
        logger.info("reconnect window expired for %s", account.name)
        room = self.rooms.get(account.room_code)
        if room is not None:
            self.remove_player(room, player_id, reason="expired")
        self.accounts.pop(player_id, None)

    # ---- dispatch ------------------------------------------------------------------

    async def dispatch(self, conn: ClientConnection, message: dict) -> None:
        msg_type = message.get("type")
        request_id = str(message.get("requestId") or "")[:64]
        if not isinstance(msg_type, str) or not msg_type:
            await conn.send_error("MISSING MESSAGE TYPE", request_id)
            return
        if msg_type == "ping":
            await conn.send({"type": "pong", "requestId": request_id, "serverTimeMs": now_ms()})
            return
        if msg_type == "hello":
            await self._hello(conn, message, request_id)
            return
        if msg_type == "reconnect":
            await self._reconnect(conn, message, request_id)
            return
        if msg_type == "leaderboard":
            entries = self.leaderboard.query(str(message.get("mode") or "")[:32])
            await conn.send({"type": "leaderboard", "requestId": request_id, "ok": True,
                             "leaderboard": entries})
            return
        if conn.account is None:
            await conn.send_error("NOT AUTHENTICATED", request_id)
            return
        account = conn.account
        account.name = sanitise_name(message.get("name") or account.name)
        if msg_type == "quick_match":
            await self._quick_match(account, request_id)
        elif msg_type == "create_room":
            await self._handle_create_room(account, private=True, request_id=request_id)
        elif msg_type == "join_room":
            await self._join_room(account, message, request_id)
        elif msg_type == "ready":
            await self._ready(account, message, request_id)
        elif msg_type == "start_race":
            await self._start_race(account, request_id)
        elif msg_type == "snapshot":
            await self._snapshot(account, message, request_id)
        elif msg_type == "finish":
            await self._finish(account, message, request_id)
        elif msg_type == "leave_room":
            await self._leave_room(account)
        else:
            await conn.send_error("UNKNOWN MESSAGE TYPE", request_id)

    async def _hello(self, conn: ClientConnection, message: dict, request_id: str) -> None:
        if conn.account is not None:
            conn.account.name = sanitise_name(message.get("name") or conn.account.name)
            await conn.send({"type": "welcome_back", "requestId": request_id, "ok": True,
                             "playerId": conn.account.player_id, "token": conn.account.token,
                             "name": conn.account.name, "serverTimeMs": now_ms()})
            return
        player_id = "p_" + secrets.token_hex(6)
        token = secrets.token_urlsafe(24)
        name = sanitise_name(message.get("name"))
        account = Account(player_id=player_id, token=token, name=name, connection=conn)
        self.accounts[player_id] = account
        conn.account = account
        logger.info("hello %s (%s) from %s", name, player_id, conn.peer)
        await conn.send({
            "type": "welcome", "ok": True, "requestId": request_id,
            "playerId": player_id, "token": token, "name": name,
            "serverTimeMs": now_ms(), "protocol": PROTOCOL,
            "clientVersion": SERVER_VERSION,
        })

    async def _reconnect(self, conn: ClientConnection, message: dict, request_id: str) -> None:
        token = str(message.get("token") or "")
        player_id = str(message.get("playerId") or "")
        account = self.accounts.get(player_id)
        if account is None or not token or not secrets.compare_digest(account.token, token):
            logger.warning("rejected reconnect for %s from %s", player_id, conn.peer)
            await conn.send({"type": "session_expired", "ok": False, "requestId": request_id,
                             "error": "SESSION EXPIRED - START A NEW CIRCUIT",
                             "serverTimeMs": now_ms()})
            await conn.close()
            return
        if account.connection is not None and account.connection is not conn:
            old = account.connection
            account.connection = None
            with contextlib.suppress(Exception):
                await old.close()
        if account.cleanup_task is not None:
            account.cleanup_task.cancel()
            account.cleanup_task = None
        conn.account = account
        account.connection = conn
        account.name = sanitise_name(message.get("name") or account.name)
        account.disconnected_at = 0.0
        room = self.rooms.get(account.room_code)
        if room is not None and account.player_id in room.players:
            player = room.players[account.player_id]
            player.connected = True
            player.last_seen_ms = now_ms()
        logger.info("reconnected %s (%s)", account.name, player_id)
        await conn.send({
            "type": "welcome_back", "ok": True, "requestId": request_id,
            "playerId": account.player_id, "token": account.token, "name": account.name,
            "serverTimeMs": now_ms(),
        })
        if room is not None and account.player_id in room.players:
            await conn.send(room.wire())
            self.broadcast_room(room)
            if room.phase in ("countdown", "racing"):
                await conn.send(self._race_start_message(room))
            if room.phase == "finished":
                stored = room.results_by_player.get(account.player_id)
                if stored is not None:
                    await conn.send(stored)
            elif room.phase == "racing":
                await conn.send(self._state_sync(room))

    # ---- matchmaking ---------------------------------------------------------------

    async def _quick_match(self, account: Account, request_id: str) -> None:
        room = self.rooms.get(account.room_code)
        if room is not None and account.player_id in room.players:
            await self._send_room_state(account)
            return
        room = self._find_public_lobby()
        if room is None:
            room = self._new_room(account, private=False)
        else:
            self._add_player(room, account)
        logger.info("%s quick-matched into %s", account.name, room.code)
        self.broadcast_room(room)

    async def _handle_create_room(self, account: Account, private: bool, request_id: str = "") -> None:
        self._detach_from_room(account)
        room = self._new_room(account, private=private)
        logger.info("%s created room %s", account.name, room.code)
        self.broadcast_room(room)

    async def _join_room(self, account: Account, message: dict, request_id: str) -> None:
        code = sanitise_room_code(message.get("roomCode"))
        room = self.rooms.get(code)
        if room is None:
            await account.connection.send_error("ROOM NOT FOUND", request_id)
            return
        if account.player_id in room.players:
            await self._send_room_state(account)
            return
        if room.phase != "lobby":
            await account.connection.send_error("RACE ALREADY IN PROGRESS", request_id)
            return
        if len(room.human_players()) >= MAX_PLAYERS:
            await account.connection.send_error("ROOM IS FULL", request_id)
            return
        self._detach_from_room(account)
        self._add_player(room, account)
        logger.info("%s joined room %s", account.name, room.code)
        self.broadcast_room(room)

    def _find_public_lobby(self) -> Optional[Room]:
        candidates = [r for r in self.rooms.values()
                      if not r.private and r.phase == "lobby" and len(r.human_players()) < MAX_PLAYERS]
        if not candidates:
            return None
        candidates.sort(key=lambda r: (-len(r.human_players()), r.code))
        return candidates[0]

    def _new_room(self, account: Account, private: bool) -> Room:
        code = self._new_room_code()
        room = Room(code=code, private=private, host_id=account.player_id, duration=self.race_duration)
        self.rooms[code] = room
        self._add_player(room, account)
        return room

    def _new_room_code(self) -> str:
        for _ in range(128):
            code = "".join(secrets.choice(ROOM_CODE_ALPHABET) for _ in range(5))
            if code not in self.rooms:
                return code
        raise RuntimeError("room code space exhausted")

    def _add_player(self, room: Room, account: Account) -> Player:
        player = Player(player_id=account.player_id, name=account.name)
        player.host = room.host_id == account.player_id or not room.players
        if player.host:
            room.host_id = account.player_id
            for other in room.players.values():
                other.host = False
        room.players[account.player_id] = player
        account.room_code = room.code
        return player

    def _detach_from_room(self, account: Account) -> None:
        room = self.rooms.get(account.room_code)
        if room is not None:
            self.remove_player(room, account.player_id, reason="left")
        account.room_code = ""

    async def _leave_room(self, account: Account) -> None:
        room = self.rooms.get(account.room_code)
        if room is not None:
            logger.info("%s left room %s", account.name, room.code)
            self.remove_player(room, account.player_id, reason="left")
        account.room_code = ""
        if account.connection is not None:
            await account.connection.send({"type": "left_room", "ok": True})

    def remove_player(self, room: Room, player_id: str, reason: str = "left") -> None:
        player = room.players.pop(player_id, None)
        if player is None:
            return
        if player.host:
            humans = [p for p in room.human_players() if p.connected]
            if not humans:
                humans = room.human_players()
            if humans:
                room.host_id = humans[0].player_id
                humans[0].host = True
            else:
                room.host_id = ""
        account = self.accounts.get(player_id)
        if account is not None and account.room_code == room.code:
            account.room_code = ""
        if not room.players or not room.human_players():
            self._delete_room(room)
            return
        self.broadcast_room(room)
        if room.phase == "racing" and self._all_humans_done(room):
            self._finalize_room(room)

    async def _ready(self, account: Account, message: dict, request_id: str) -> None:
        room = self.rooms.get(account.room_code)
        if room is None or room.phase != "lobby":
            await account.connection.send_error("NOT IN A LOBBY", request_id)
            return
        player = room.players.get(account.player_id)
        if player is None:
            await account.connection.send_error("PLAYER NOT IN ROOM", request_id)
            return
        player.ready = bool(message.get("ready", True))
        player.last_seen_ms = now_ms()
        self.broadcast_room(room)

    # ---- race start and simulation -------------------------------------------------

    async def _start_race(self, account: Account, request_id: str) -> None:
        room = self.rooms.get(account.room_code)
        if room is None:
            await account.connection.send_error("NOT IN A ROOM", request_id)
            return
        if room.phase != "lobby":
            await account.connection.send_error("RACE ALREADY STARTED", request_id)
            return
        if room.host_id != account.player_id:
            await account.connection.send_error("ONLY THE HOST CAN START", request_id)
            return
        humans = room.human_players()
        if not humans:
            await account.connection.send_error("NO RUNNERS IN ROOM", request_id)
            return
        for player in humans:
            if not player.connected:
                await account.connection.send_error("A RUNNER IS RECONNECTING", request_id)
                return
            if not player.ready:
                await account.connection.send_error("ALL RUNNERS MUST BE READY", request_id)
                return
        if len(room.players) > MAX_PLAYERS:
            self._trim_room(room)

        seed = secrets.randbelow(2_000_000_000) + 1
        start_at = now_ms() + int(self.countdown_seconds * 1000)
        room.seed = seed
        room.start_at_ms = start_at
        room.race_id = f"{room.code}-{start_at}"
        room.phase = "countdown"
        room.results_players = None
        room.results_by_player = {}
        self._fill_bots(room, seed)
        for player in room.players.values():
            player.reset_for_race(seed)
        logger.info("room %s start requested: seed=%s players=%d", room.code, seed, len(room.players))
        # Send the roster first so every client has bots and final identities before launching.
        self.broadcast_room(room)
        countdown = {
            "type": "countdown",
            "roomCode": room.code,
            "hostId": room.host_id,
            "phase": "countdown",
            "privateRoom": room.private,
            "maxPlayers": MAX_PLAYERS,
            "seed": seed,
            "startAtMs": start_at,
            "raceId": room.race_id,
            "raceDurationSeconds": room.duration,
            "players": [p.wire() for p in room.ordered_players()],
        }
        self.broadcast(room, countdown)
        if room.race_task is not None:
            room.race_task.cancel()
        room.race_task = self._spawn(self._run_race(room))

    def _fill_bots(self, room: Room, seed: int) -> None:
        humans = room.human_players()
        bots_needed = MAX_PLAYERS - len(humans)
        if bots_needed <= 0:
            return
        rng = random.Random(seed)
        names = list(BOT_NAMES)
        rng.shuffle(names)
        for index in range(bots_needed):
            bot_id = f"bot_{room.code}_{index}"
            name = names[index % len(names)]
            bot = Player(player_id=bot_id, name=name, bot=True, connected=True, ready=True,
                         base_speed=18.0)
            room.players[bot_id] = bot
        logger.info("room %s filled with %d bots", room.code, bots_needed)

    def _trim_room(self, room: Room) -> None:
        humans = room.human_players()
        if len(humans) <= MAX_PLAYERS:
            return
        for player in humans[MAX_PLAYERS:]:
            room.players.pop(player.player_id, None)
            account = self.accounts.get(player.player_id)
            if account is not None:
                account.room_code = ""

    async def _run_race(self, room: Room) -> None:
        try:
            delay = max(0.0, (room.start_at_ms - now_ms()) / 1000.0)
            await asyncio.sleep(delay)
            if self.rooms.get(room.code) is not room or room.phase != "countdown":
                return
            room.phase = "racing"
            for player in room.players.values():
                player.last_snapshot_at = time.monotonic()
            self.broadcast(room, self._race_start_message(room))
            self.broadcast_room(room)
            logger.info("room %s race started", room.code)
            end_at = room.start_at_ms + int(room.duration * 1000)
            while now_ms() < end_at:
                if self.rooms.get(room.code) is not room:
                    return
                if self._all_humans_done(room):
                    break
                self._tick_bots(room)
                self.broadcast(room, self._state_sync(room))
                await asyncio.sleep(0.25)
            # Brief collection grace so finish messages can arrive after the local clock end.
            await asyncio.sleep(0.65)
            self._tick_bots(room, final=True)
            self._finalize_room(room)
        except asyncio.CancelledError:
            raise
        except Exception:  # noqa: BLE001
            logger.exception("race loop failed for room %s", room.code)

    def _tick_bots(self, room: Room, final: bool = False) -> None:
        if room.phase not in ("racing", "finished"):
            return
        elapsed = max(0.0, (now_ms() - room.start_at_ms) / 1000.0)
        elapsed = min(elapsed, room.duration)
        for bot in room.players.values():
            if not bot.bot:
                continue
            wobble = math.sin(elapsed * 0.19 + (hash(bot.player_id) % 7)) * 0.7
            bot.distance = max(bot.distance, bot.base_speed * elapsed + wobble)
            bot.speed = bot.base_speed if elapsed < room.duration else 0.0
            bot.survival_time = elapsed
            bot.score = int(bot.distance * 10.0)
            bot.coins = int(bot.distance / 17.0)
            if final or elapsed >= room.duration:
                bot.finished = True
                bot.runner_state = "finished"

    def _all_humans_done(self, room: Room) -> bool:
        humans = room.human_players()
        if not humans:
            return True
        for player in humans:
            if player.finish_received:
                continue
            if not player.connected:
                # A disconnected runner is either reconnecting or expired; do not hold the
                # room hostage, but keep their last validated distance in the ranking.
                continue
            return False
        return True

    # ---- snapshots and finish ------------------------------------------------------

    async def _snapshot(self, account: Account, message: dict, request_id: str) -> None:
        room = self.rooms.get(account.room_code)
        if room is None or room.phase != "racing":
            return
        player = room.players.get(account.player_id)
        if player is None or player.bot or player.finish_received:
            return
        wall = time.monotonic()
        delta = max(0.001, wall - player.last_snapshot_at)
        elapsed = max(0.0, (now_ms() - room.start_at_ms) / 1000.0)
        reported = number(message.get("distance"), player.distance, 1_000_000.0)
        reported = max(0.0, reported)
        hard_cap = MAX_SPEED * elapsed + 80.0
        reported = min(reported, hard_cap)
        state = str(message.get("runnerState") or "run").lower()
        if state not in ("run", "jump", "slide", "dead", "finished"):
            state = "run"

        valid = plausible_delta(player.distance, reported, delta)
        if not valid:
            player.violations += 1
            reported = player.distance
            logger.warning("clamped snapshot for %s in %s (violation %d)", player.name, room.code,
                           player.violations)
            if player.violations in (1, 5):
                await account.connection.send({"type": "error", "ok": False,
                                               "error": "SNAPSHOT REJECTED - SERVER CLAMPED DISTANCE",
                                               "requestId": request_id,
                                               "serverTimeMs": now_ms()})
            if player.violations >= 8:
                player.disqualified = True
                player.dead = True
                player.finished = False
                player.finish_received = True
                player.runner_state = "dead"
                await account.connection.send({"type": "error", "ok": False,
                                               "error": "RACE INTEGRITY VIOLATION - RESULT INVALIDATED",
                                               "requestId": request_id,
                                               "serverTimeMs": now_ms()})
                self.broadcast_room(room)
                if self._all_humans_done(room):
                    self._finalize_room(room)
                return

        player.distance = max(player.distance, reported)
        player.speed = clamp(number(message.get("speed"), player.speed, 10_000.0), 0.0, MAX_REPORTED_SPEED)
        score_cap = int(player.distance * MAX_SCORE_PER_METRE) + 10_000
        reported_score = max(0, integer(message.get("score"), player.score, 5_000_000))
        player.score = max(player.score, min(reported_score, score_cap))
        coin_cap = int(player.distance * MAX_COINS_PER_METRE) + 200
        reported_coins = max(0, integer(message.get("coins"), player.coins, 10_000))
        player.coins = max(player.coins, min(reported_coins, coin_cap))
        lane_raw = integer(message.get("lane"), player.lane, 99)
        player.lane = lane_raw if 0 <= lane_raw <= 2 else 1
        player.runner_state = state
        if state == "dead":
            player.dead = True
        player.lives = max(0, min(10, integer(message.get("lives"), player.lives, 10)))
        player.near_misses = max(0, min(50_000, integer(message.get("nearMisses"), player.near_misses, 50_000)))
        player.power_ups = max(0, min(500, integer(message.get("powerUpsUsed"), player.power_ups, 500)))
        player.survival_time = clamp(number(message.get("survivalTime"), elapsed, 10_000.0),
                                     0.0, elapsed + 10.0)
        player.last_snapshot_at = wall
        player.last_seen_ms = now_ms()
        player.snapshots += 1

    async def _finish(self, account: Account, message: dict, request_id: str) -> None:
        room = self.rooms.get(account.room_code)
        if room is None:
            await account.connection.send_error("ROOM NO LONGER EXISTS", request_id)
            return
        if room.phase not in ("racing", "countdown"):
            await account.connection.send_error("RACE IS NOT RUNNING", request_id)
            return
        player = room.players.get(account.player_id)
        if player is None:
            await account.connection.send_error("PLAYER NOT IN RACE", request_id)
            return
        if player.finish_received:
            return
        elapsed = max(0.0, (now_ms() - room.start_at_ms) / 1000.0)
        claimed_finished = bool(message.get("finished"))
        if claimed_finished and elapsed + FINISH_EARLY_GRACE_SECONDS < room.duration:
            # A timed circuit cannot be completed before the server clock reaches the finish.
            # Reject the packet without ending the run; snapshots still count, and the normal
            # finalisation path will award the last validated distance.
            await account.connection.send_error("RACE STILL RUNNING - FINISH REJECTED", request_id)
            return
        reported = number(message.get("distance"), player.distance, 1_000_000.0)
        survival = number(message.get("survivalTime"), elapsed, 100_000.0)
        distance_cap = MAX_SPEED * max(0.0, min(elapsed, room.duration + 5.0)) + 100.0
        verified = True
        if reported < 0 or reported > distance_cap:
            verified = False
            reported = min(max(0.0, player.distance), distance_cap)
            logger.warning("finish distance rejected for %s: %.1f > %.1f", player.name, reported, distance_cap)
        if survival < 0 or survival > elapsed + 10.0:
            survival = min(max(0.0, survival), elapsed + 10.0)
        since_snapshot = max(0.001, time.monotonic() - player.last_snapshot_at)
        if not plausible_delta(player.distance, reported, since_snapshot):
            # The last validated snapshot is the authoritative floor; a finish may not leap
            # arbitrarily far ahead even if it arrives after a long network silence.
            if reported - player.distance > SNAPSHOT_SLACK + MAX_SPEED * max(0.5, since_snapshot):
                verified = False
                reported = player.distance
        if player.violations >= 8:
            verified = False
        player.distance = max(player.distance, reported)
        player.finish_received = True
        player.finished = claimed_finished and verified
        player.dead = not player.finished or not verified
        player.disqualified = not verified
        player.finish_time_ms = now_ms()
        player.survival_time = clamp(survival, 0.0, elapsed + 10.0)
        score_cap = int(player.distance * MAX_SCORE_PER_METRE) + 10_000
        reported_score = max(0, integer(message.get("score"), player.score, 5_000_000))
        player.score = max(player.score, min(reported_score, score_cap))
        coin_cap = int(player.distance * MAX_COINS_PER_METRE) + 200
        reported_coins = max(0, integer(message.get("coins"), player.coins, 10_000))
        player.coins = max(player.coins, min(reported_coins, coin_cap))
        player.speed = 0.0
        player.runner_state = "finished" if player.finished else "dead"
        player.last_seen_ms = now_ms()
        player.last_snapshot_at = time.monotonic()
        logger.info("finish %s in %s: %.1fm state=%s verified=%s", player.name, room.code,
                    player.distance, player.runner_state, verified)
        if room.phase == "racing":
            self.broadcast(room, self._state_sync(room))
            if self._all_humans_done(room):
                self._finalize_room(room)
            else:
                await account.connection.send({"type": "state_sync", "phase": "racing",
                                               "players": [p.wire() for p in room.ordered_players()]})

    # ---- messages ------------------------------------------------------------------

    def _race_start_message(self, room: Room) -> dict:
        return {
            "type": "race_start",
            "roomCode": room.code,
            "phase": "racing",
            "privateRoom": room.private,
            "seed": room.seed,
            "startAtMs": room.start_at_ms,
            "raceId": room.race_id,
            "raceDurationSeconds": room.duration,
            "players": [p.wire() for p in room.ordered_players()],
        }

    def _state_sync(self, room: Room) -> dict:
        return {
            "type": "state_sync",
            "roomCode": room.code,
            "hostId": room.host_id,
            "phase": room.phase,
            "privateRoom": room.private,
            "seed": room.seed,
            "startAtMs": room.start_at_ms,
            "raceId": room.race_id,
            "raceDurationSeconds": room.duration,
            "players": [p.wire() for p in room.ordered_players()],
        }

    async def _send_room_state(self, account: Account) -> None:
        room = self.rooms.get(account.room_code)
        if room is not None and account.connection is not None:
            await account.connection.send(room.wire())

    def broadcast(self, room: Room, message: dict) -> None:
        for player in room.players.values():
            if player.bot:
                continue
            account = self.accounts.get(player.player_id)
            if account is None or account.connection is None:
                continue
            self._spawn(self._safe_send(account.connection, message))

    def broadcast_room(self, room: Room) -> None:
        message = room.wire()
        for player in room.players.values():
            if player.bot:
                continue
            account = self.accounts.get(player.player_id)
            if account is None or account.connection is None:
                continue
            self._spawn(self._safe_send(account.connection, message))

    async def _safe_send(self, conn: ClientConnection, message: dict) -> None:
        await conn.send(message)

    # ---- finalisation --------------------------------------------------------------

    def _finalize_room(self, room: Room) -> None:
        if room.phase == "finished":
            return
        if self.rooms.get(room.code) is not room:
            return
        room.phase = "finished"
        self._tick_bots(room, final=True)
        humans = room.human_players()
        for player in room.players.values():
            if player.bot:
                player.finished = True
            elif not player.finish_received and player.connected and player.snapshots > 0:
                # The server already validated every snapshot; a missed finish packet does not
                # invalidate a live runner's distance at the final whistle.
                player.finish_received = True
                player.finished = not player.dead
                player.runner_state = "finished" if player.finished else "dead"
                player.survival_time = min(room.duration, player.survival_time)

        ordered = sorted(room.players.values(), key=rank_key)
        for index, player in enumerate(ordered, start=1):
            player.placement = index
        total = len(ordered)

        for player in humans:
            verified = (not player.bot and not player.disqualified and player.finished
                        and player.finish_received and player.connected and player.distance >= 20.0)
            if verified:
                player.rewards_credits = PLACEMENT_CREDITS.get(player.placement, 100)
                bonus = int(min(250, player.distance / 8.0))
                player.rewards_credits += bonus
                player.rewards_xp = 60 + int(min(1500, player.distance * 0.35)) + \
                    max(0, (MAX_PLAYERS - player.placement + 1) * 20)
            else:
                player.rewards_credits = 0
                player.rewards_xp = 0

        wire_players = [p.wire() for p in ordered]
        room.results_players = wire_players
        mode = "PrivateRace" if room.private else "OnlineRace"
        leaderboard_entries = []
        for player in humans:
            if player.disqualified or not player.finish_received or player.distance < 20.0:
                continue
            leaderboard_entries.append({
                "name": player.name,
                "distance": player.distance,
                "score": player.score,
                "coins": player.coins,
                "mode": mode,
                "achievedUtc": utc_iso(),
                "verified": player.connected,
            })
        if leaderboard_entries:
            self.leaderboard.record(leaderboard_entries)

        for player in humans:
            message = {
                "type": "race_results",
                "ok": True,
                "raceId": room.race_id,
                "phase": "finished",
                "roomCode": room.code,
                "place": player.placement,
                "rewardsCredits": player.rewards_credits,
                "rewardsXp": player.rewards_xp,
                "rewardGranted": player.rewards_credits > 0,
                "serverTimeMs": now_ms(),
                "players": wire_players,
                "message": "SERVER-VERIFIED RESULT",
            }
            room.results_by_player[player.player_id] = message
            account = self.accounts.get(player.player_id)
            if account is not None and account.connection is not None:
                self._spawn(self._safe_send(account.connection, message))
        logger.info("room %s finalised: %s", room.code,
                    ", ".join(f"{p.name}=#{p.placement}" for p in ordered))
        self.broadcast_room(room)
        self._schedule_room_cleanup(room)

    def _schedule_room_cleanup(self, room: Room) -> None:
        async def cleanup():
            await asyncio.sleep(RESULTS_RETENTION_SECONDS)
            if self.rooms.get(room.code) is room:
                logger.info("room %s expired after results", room.code)
                self._delete_room(room)

        room.cleanup_task = self._spawn(cleanup())

    def _cancel_room_tasks(self, room: Room) -> None:
        for task in (room.race_task, room.cleanup_task):
            if task is not None and not task.done():
                task.cancel()

    def _delete_room(self, room: Room) -> None:
        self._cancel_room_tasks(room)
        self.rooms.pop(room.code, None)
        for player in room.players.values():
            account = self.accounts.get(player.player_id)
            if account is not None and account.room_code == room.code:
                account.room_code = ""


# ---------------------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------------------

def parse_args(argv: Optional[List[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="NEON VELOCITY authoritative race server")
    parser.add_argument("--host", default=os.environ.get("NV_HOST", "0.0.0.0"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("NV_PORT", "7777")))
    parser.add_argument("--data", default=os.environ.get("NV_DATA", str(pathlib.Path(__file__).parent / "data")))
    parser.add_argument("--countdown", type=float, default=float(os.environ.get("NV_COUNTDOWN", "5")))
    parser.add_argument("--duration", type=float, default=float(os.environ.get("NV_DURATION", "120")))
    parser.add_argument("--log-level", default=os.environ.get("NV_LOG_LEVEL", "INFO"))
    return parser.parse_args(argv)


def configure_logging(level: str) -> None:
    logging.basicConfig(
        level=getattr(logging, level.upper(), logging.INFO),
        format="%(asctime)s %(levelname)-7s %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )


async def run(args: argparse.Namespace) -> int:
    configure_logging(args.log_level)
    data_dir = pathlib.Path(args.data).expanduser().resolve()
    data_dir.mkdir(parents=True, exist_ok=True)
    server = RaceServer(args.host, args.port, data_dir, args.countdown, args.duration)
    await server.start()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        with contextlib.suppress(NotImplementedError):
            loop.add_signal_handler(sig, server.stop)
    try:
        await server.serve_forever()
    finally:
        await server.shutdown()
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    args = parse_args(argv)
    try:
        return asyncio.run(run(args))
    except KeyboardInterrupt:
        return 0


if __name__ == "__main__":
    sys.exit(main())
