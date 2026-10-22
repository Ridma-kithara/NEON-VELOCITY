"""Test support for the NEON VELOCITY authority.

A tiny asyncio line-JSON client that mirrors the Unity `RaceClient` framing. The tests run
the real `RaceServer` in-process on an ephemeral port and drive it over TCP, so request
ordering, broadcasts, countdown timing and disconnect handling are all exercised for real.
"""

from __future__ import annotations

import asyncio
import contextlib
import itertools
import json
import pathlib
import sys
import tempfile
import unittest
from typing import Any, Dict, List, Optional

BACKEND = pathlib.Path(__file__).resolve().parents[1]
if str(BACKEND) not in sys.path:
    sys.path.insert(0, str(BACKEND))

import server as authority  # noqa: E402  (path set above)


class Peer:
    """One TCP client with a background reader and a replayable message backlog."""

    _sequence = itertools.count(1)

    def __init__(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        self.reader = reader
        self.writer = writer
        self.queue: asyncio.Queue = asyncio.Queue()
        self.backlog: List[Dict[str, Any]] = []
        self.reader_task: Optional[asyncio.Task] = None
        self.player_id = ""
        self.token = ""
        self.name = "PEER"
        self.welcome: Dict[str, Any] = {}
        self.index = next(self._sequence)
        self.request_index = 0
        self._closed = False

    @classmethod
    async def open(cls, port: int, name: str) -> "Peer":
        reader, writer = await asyncio.open_connection("127.0.0.1", port)
        peer = cls(reader, writer)
        peer.name = name
        peer.reader_task = asyncio.create_task(peer._read_loop())
        await peer.send("hello", name=name)
        welcome = await peer.expect("welcome", timeout=3.0)
        assert welcome.get("ok") is True, welcome
        peer.player_id = str(welcome.get("playerId") or "")
        peer.token = str(welcome.get("token") or "")
        peer.welcome = welcome
        assert peer.player_id, welcome
        assert peer.token, welcome
        return peer

    async def _read_loop(self) -> None:
        try:
            while True:
                line = await self.reader.readline()
                if not line:
                    break
                try:
                    message = json.loads(line.decode("utf-8"))
                except (UnicodeDecodeError, ValueError):
                    continue
                if isinstance(message, dict):
                    await self.queue.put(message)
        except (ConnectionError, OSError, asyncio.IncompleteReadError):
            pass
        finally:
            await self.queue.put(None)

    async def send(self, message_type: str, **fields: Any) -> str:
        self.request_index += 1
        request_id = f"t{self.index}-{self.request_index}"
        payload = {"type": message_type, "requestId": request_id}
        payload.update(fields)
        await self.send_raw(json.dumps(payload, separators=(",", ":")))
        return request_id

    async def send_raw(self, text: str) -> None:
        self.writer.write((text + "\n").encode("utf-8"))
        await self.writer.drain()

    async def recv(self, timeout: float = 3.0) -> Dict[str, Any]:
        try:
            item = await asyncio.wait_for(self.queue.get(), timeout)
        except asyncio.TimeoutError as exc:
            raise AssertionError(
                f"{self.name}: timed out after {timeout:.2f}s waiting for any message; "
                f"backlog={self.summary()}"
            ) from exc
        if item is None:
            raise EOFError(f"{self.name}: server closed the connection")
        return item

    async def expect(self, message_type: str, timeout: float = 3.0,
                     where=None) -> Dict[str, Any]:
        loop = asyncio.get_running_loop()
        deadline = loop.time() + timeout
        for index, message in enumerate(self.backlog):
            if message.get("type") == message_type and (where is None or where(message)):
                return self.backlog.pop(index)
        while True:
            remaining = deadline - loop.time()
            if remaining <= 0:
                raise AssertionError(
                    f"{self.name}: timed out waiting for '{message_type}'; backlog={self.summary()}"
                )
            try:
                message = await self.recv(remaining)
            except AssertionError:
                raise
            if message.get("type") == message_type and (where is None or where(message)):
                return message
            self.backlog.append(message)
            if len(self.backlog) > 300:
                self.backlog = self.backlog[-150:]

    async def maybe(self, message_type: str, timeout: float = 0.25,
                    where=None) -> Optional[Dict[str, Any]]:
        try:
            return await self.expect(message_type, timeout=timeout, where=where)
        except (AssertionError, EOFError):
            return None

    def has_queued(self, message_type: str, where=None) -> bool:
        for message in self.backlog:
            if message.get("type") == message_type and (where is None or where(message)):
                return True
        return False

    def summary(self) -> List[str]:
        return [str(message.get("type")) for message in self.backlog[-20:]]

    async def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        with contextlib.suppress(Exception):
            self.writer.close()
            await self.writer.wait_closed()
        if self.reader_task is not None:
            self.reader_task.cancel()
            with contextlib.suppress(BaseException):
                await self.reader_task


class AuthorityTest(unittest.IsolatedAsyncioTestCase):
    """Base class: fresh server, data directory and peer registry per test."""

    countdown_seconds = 0.35
    race_duration = 2.0

    async def asyncSetUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(prefix="nv-authority-")
        self.data_dir = pathlib.Path(self.temp_dir.name)
        self.server: Optional[authority.RaceServer] = None
        self.peers: List[Peer] = []
        await self.start_server()

    async def start_server(self) -> None:
        self.server = authority.RaceServer(
            "127.0.0.1", 0, self.data_dir,
            countdown_seconds=self.countdown_seconds,
            race_duration=self.race_duration,
        )
        await self.server.start()
        self.port = self.server.port

    async def stop_server(self) -> None:
        if self.server is not None:
            await self.server.shutdown()
            self.server = None

    async def asyncTearDown(self) -> None:
        for peer in self.peers:
            await peer.close()
        await self.stop_server()
        self.temp_dir.cleanup()

    async def peer(self, name: str) -> Peer:
        assert self.server is not None
        client = await Peer.open(self.port, name)
        self.peers.append(client)
        return client

    async def close_peers(self) -> None:
        for peer in self.peers:
            await peer.close()
        self.peers.clear()

    # ---- shared match helpers -----------------------------------------------------------

    async def create_public_lobby(self, peers: List[Peer]) -> Dict[str, Any]:
        """Quick-match the first peer, then let the rest join the same public lobby."""
        first = peers[0]
        await first.send("quick_match", mode="online")
        room = await first.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        for index, peer in enumerate(peers[1:], start=2):
            await peer.send("quick_match", mode="online")
            room = await first.expect(
                "room_state",
                where=lambda m, count=index: len(m.get("players") or []) >= count,
            )
            await peer.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        return room

    async def set_ready(self, peers: List[Peer]) -> None:
        for peer in peers:
            await peer.send("ready", ready=True)
        await peers[0].expect(
            "room_state",
            timeout=4.0,
            where=lambda m: all(
                player.get("ready") for player in (m.get("players") or []) if not player.get("bot")
            ) and len([p for p in (m.get("players") or []) if not p.get("bot")]) == len(peers),
        )

    async def start_match(self, peers: List[Peer]) -> Dict[str, Any]:
        await self.set_ready(peers)
        await peers[0].send("start_race", mode="online")
        countdowns = await asyncio.gather(
            *(peer.expect("countdown", timeout=5.0) for peer in peers)
        )
        starts = await asyncio.gather(
            *(peer.expect("race_start", timeout=6.0) for peer in peers)
        )
        for message in countdowns[1:]:
            self.assertEqual(message.get("raceId"), countdowns[0].get("raceId"))
            self.assertEqual(message.get("seed"), countdowns[0].get("seed"))
        for message in starts:
            self.assertEqual(message.get("raceId"), countdowns[0].get("raceId"))
        return countdowns[0]

    async def play_timed(self, peer: Peer, start_at_ms: int, metres_per_second: float) -> None:
        """Send live snapshots for a whole timed circuit, then claim the finish."""
        assert self.server is not None
        end_at = start_at_ms + int(self.race_duration * 1000)
        snapshot_deadline = end_at - 120
        while authority.now_ms() < snapshot_deadline:
            elapsed = max(0.0, (authority.now_ms() - start_at_ms) / 1000.0)
            distance = metres_per_second * elapsed + 5.0
            await peer.send(
                "snapshot",
                distance=round(distance, 2),
                speed=metres_per_second,
                score=int(distance * 10),
                coins=int(distance / 40.0),
                lane=1,
                runnerState="run",
                lives=1,
                nearMisses=0,
                powerUpsUsed=0,
                survivalTime=round(elapsed, 2),
            )
            await asyncio.sleep(0.12)
        wait = max(0.0, (end_at + 40 - authority.now_ms()) / 1000.0)
        await asyncio.sleep(wait)
        elapsed = max(0.0, (authority.now_ms() - start_at_ms) / 1000.0)
        distance = metres_per_second * self.race_duration + 5.0
        await peer.send(
            "finish",
            distance=round(distance, 2),
            speed=0,
            score=int(distance * 10),
            coins=int(distance / 40.0),
            lane=1,
            runnerState="finished",
            lives=1,
            nearMisses=0,
            powerUpsUsed=0,
            survivalTime=round(elapsed, 2),
            finished=True,
        )

    async def run_full_race(self, peers: List[Peer], speeds: List[float]) -> List[Dict[str, Any]]:
        countdown = await self.start_match(peers)
        await asyncio.gather(
            *(self.play_timed(peer, int(countdown["startAtMs"]), speed)
              for peer, speed in zip(peers, speeds))
        )
        results = await asyncio.gather(
            *(peer.expect("race_results", timeout=7.0) for peer in peers)
        )
        return list(results)
