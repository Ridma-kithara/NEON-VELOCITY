"""End-to-end integration tests for the NEON VELOCITY authority.

Run from the repository root:

    python3 -m unittest discover -s Backend/tests -v

The suite boots the real asyncio server on ephemeral ports, drives it with the same
line-delimited JSON protocol as the Unity client, and covers matchmaking, private rooms,
five-player lobbies, bots, countdown/race timing, ranking, rewards, leaderboard persistence,
snapshot integrity, early-finish rejection, reconnection and basic protocol errors.
"""

from __future__ import annotations

import asyncio
import json
import pathlib
import sys
import unittest

TEST_DIR = pathlib.Path(__file__).resolve().parent
if str(TEST_DIR) not in sys.path:
    sys.path.insert(0, str(TEST_DIR))

from support import AuthorityTest, Peer, authority  # noqa: E402


def human_players(message):
    return [player for player in (message.get("players") or []) if not player.get("bot")]


def find_player(message, player_id):
    for player in message.get("players") or []:
        if player.get("playerId") == player_id:
            return player
    return None


def placements(message):
    return sorted(int(player.get("placement", 0)) for player in message.get("players") or [])


class QuickMatchRaceTests(AuthorityTest):
    async def test_two_humans_bots_results_and_leaderboard_persistence(self):
        host = await self.peer("HOST")
        guest = await self.peer("GUEST")

        await host.send("quick_match", mode="online")
        first = await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        code = first["roomCode"]
        self.assertEqual(first["hostId"], host.player_id)
        self.assertFalse(first["privateRoom"])
        self.assertEqual(first["maxPlayers"], 5)

        await guest.send("quick_match", mode="online")
        guest_room = await guest.expect(
            "room_state", where=lambda m: m.get("roomCode") == code and len(human_players(m)) == 2
        )
        self.assertEqual(guest_room["hostId"], host.player_id)
        await host.expect("room_state", where=lambda m: len(human_players(m)) == 2)

        # Only the host starts, and every human must be ready.
        await guest.send("start_race")
        error = await guest.expect("error")
        self.assertIn("ONLY THE HOST", error["error"])
        await host.send("start_race")
        error = await host.expect("error")
        self.assertIn("ALL RUNNERS MUST BE READY", error["error"])

        countdown = await self.start_match([host, guest])
        roster = countdown["players"]
        self.assertEqual(len(roster), 5)
        self.assertEqual(len(human_players(countdown)), 2)
        self.assertEqual(len([p for p in roster if p.get("bot")]), 3)
        self.assertGreater(countdown["seed"], 0)
        self.assertGreater(countdown["startAtMs"], authority.now_ms() - 1000)
        self.assertTrue(countdown["raceId"].startswith(code + "-"))
        self.assertAlmostEqual(countdown["raceDurationSeconds"], self.race_duration, places=2)
        self.assertEqual(countdown["phase"], "countdown")

        await asyncio.gather(
            self.play_timed(host, int(countdown["startAtMs"]), 26.0),
            self.play_timed(guest, int(countdown["startAtMs"]), 23.0),
        )
        results = await asyncio.gather(
            host.expect("race_results", timeout=7.0),
            guest.expect("race_results", timeout=7.0),
        )

        for peer, result in zip((host, guest), results):
            self.assertTrue(result["ok"])
            self.assertEqual(result["raceId"], countdown["raceId"])
            self.assertEqual(result["phase"], "finished")
            self.assertEqual(len(result["players"]), 5)
            self.assertEqual(placements(result), [1, 2, 3, 4, 5])
            me = find_player(result, peer.player_id)
            self.assertIsNotNone(me)
            self.assertTrue(me["finished"])
            self.assertTrue(me["connected"])
            self.assertGreaterEqual(me["distance"], 20.0)
            self.assertGreater(me["placement"], 0)
            self.assertGreater(me["rewardsCredits"], 0)
            self.assertGreater(result["rewardsCredits"], 0)
            self.assertEqual(result["rewardsCredits"], me["rewardsCredits"])
            self.assertTrue(result["rewardGranted"])

        # Human distances of 57 m and 51 m outrank the 34-47 m bot field.
        self.assertEqual({find_player(r, host.player_id)["placement"] for r in results}, {1})
        self.assertEqual({find_player(r, guest.player_id)["placement"] for r in results}, {2})

        board_path = self.data_dir / "leaderboard.json"
        self.assertTrue(board_path.exists())
        payload = json.loads(board_path.read_text(encoding="utf-8"))
        self.assertEqual(payload.get("version"), 1)
        self.assertEqual(len(payload.get("entries") or []), 2)

        viewer = await self.peer("VIEWER")
        await viewer.send("leaderboard", mode="circuit")
        leaderboard = await viewer.expect("leaderboard")
        entries = leaderboard["leaderboard"]
        self.assertEqual(len(entries), 2)
        self.assertEqual([entry["name"] for entry in entries], ["HOST", "GUEST"])
        self.assertEqual([entry["rank"] for entry in entries], [1, 2])
        self.assertTrue(all(entry["verified"] for entry in entries))
        self.assertEqual(entries[0]["distance"], 57.0)

        # Restart the authority against the same data directory; the ladder must survive.
        await self.close_peers()
        await self.stop_server()
        await self.start_server()
        viewer = await self.peer("VIEWER2")
        await viewer.send("leaderboard", mode="circuit")
        replay = await viewer.expect("leaderboard")
        self.assertEqual(
            [(entry["name"], entry["distance"]) for entry in replay["leaderboard"]],
            [("HOST", 57.0), ("GUEST", 51.0)],
        )


class PrivateLobbyTests(AuthorityTest):
    async def test_private_room_code_readiness_and_bot_fill(self):
        host = await self.peer("HOST")
        guest = await self.peer("GUEST")

        await host.send("create_room", mode="private", privateRoom=True)
        room = await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        code = room["roomCode"]
        self.assertTrue(room["privateRoom"])
        self.assertEqual(len(code), 5)
        self.assertEqual(room["hostId"], host.player_id)

        await guest.send("join_room", roomCode="00000")
        error = await guest.expect("error")
        self.assertIn("ROOM NOT FOUND", error["error"])

        await guest.send("join_room", roomCode=code.lower())
        joined = await guest.expect("room_state", where=lambda m: m.get("roomCode") == code)
        self.assertEqual(len(human_players(joined)), 2)
        await host.expect("room_state", where=lambda m: len(human_players(m)) == 2)

        await host.send("ready", ready=True)
        await host.expect(
            "room_state",
            where=lambda m: any(p["playerId"] == host.player_id and p["ready"]
                                for p in (m.get("players") or [])),
        )
        await host.send("start_race")
        error = await host.expect("error")
        self.assertIn("ALL RUNNERS MUST BE READY", error["error"])
        await guest.send("start_race")
        error = await guest.expect("error")
        self.assertIn("ONLY THE HOST", error["error"])

        countdown = await self.start_match([host, guest])
        self.assertTrue(countdown["privateRoom"])
        self.assertEqual(len(human_players(countdown)), 2)
        self.assertEqual(len([p for p in countdown["players"] if p.get("bot")]), 3)
        self.assertEqual(countdown["roomCode"], code)

    async def test_five_human_room_rejects_a_sixth_and_races_without_bots(self):
        host = await self.peer("HOST")
        await host.send("create_room", mode="private", privateRoom=True)
        room = await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        code = room["roomCode"]

        humans = [host]
        for index in range(2, 6):
            peer = await self.peer(f"RUNNER{index}")
            await peer.send("join_room", roomCode=code)
            await peer.expect("room_state", where=lambda m, n=index: len(human_players(m)) == n)
            await host.expect("room_state", where=lambda m, n=index: len(human_players(m)) == n)
            humans.append(peer)

        sixth = await self.peer("SIXTH")
        await sixth.send("join_room", roomCode=code)
        error = await sixth.expect("error")
        self.assertIn("ROOM IS FULL", error["error"])

        countdown = await self.start_match(humans)
        self.assertEqual(len(countdown["players"]), 5)
        self.assertEqual(len(human_players(countdown)), 5)
        self.assertEqual([p for p in countdown["players"] if p.get("bot")], [])

        await asyncio.gather(
            *(self.play_timed(peer, int(countdown["startAtMs"]), speed)
              for peer, speed in zip(humans, (27.0, 25.0, 23.0, 21.0, 19.0)))
        )
        results = await asyncio.gather(
            *(peer.expect("race_results", timeout=7.0) for peer in humans)
        )
        shared = results[0]
        self.assertEqual(len(shared["players"]), 5)
        self.assertEqual(placements(shared), [1, 2, 3, 4, 5])
        for peer, result in zip(humans, results):
            me = find_player(result, peer.player_id)
            self.assertIsNotNone(me)
            self.assertTrue(me["finished"])
            self.assertGreater(me["rewardsCredits"], 0)
            self.assertTrue(result["rewardGranted"])
            self.assertEqual(result["raceId"], countdown["raceId"])


class IntegrityTests(AuthorityTest):
    async def test_early_finish_is_rejected_and_snapshot_attack_is_disqualified(self):
        host = await self.peer("HOST")
        await host.send("quick_match", mode="online")
        await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        countdown = await self.start_match([host])
        self.assertEqual(len([p for p in countdown["players"] if p.get("bot")]), 4)

        await asyncio.sleep(0.12)
        await host.send(
            "finish",
            distance=25.0,
            speed=0.0,
            score=250,
            coins=0,
            lane=1,
            runnerState="finished",
            lives=1,
            nearMisses=0,
            powerUpsUsed=0,
            survivalTime=0.15,
            finished=True,
        )
        error = await host.expect("error", where=lambda m: "FINISH REJECTED" in str(m.get("error")))
        self.assertIn("RACE STILL RUNNING", error["error"])
        self.assertIsNone(await host.maybe("race_results", timeout=0.25))

        for _ in range(8):
            await host.send(
                "snapshot",
                distance=999999.0,
                speed=500.0,
                score=5000000,
                coins=10000,
                lane=1,
                runnerState="run",
                lives=1,
                nearMisses=0,
                powerUpsUsed=0,
                survivalTime=0.2,
            )
        result = await host.expect("race_results", timeout=5.0)
        me = find_player(result, host.player_id)
        self.assertIsNotNone(me)
        self.assertFalse(me["finished"])
        self.assertEqual(me["rewardsCredits"], 0)
        self.assertFalse(result["rewardGranted"])
        self.assertEqual(result["rewardsCredits"], 0)
        self.assertLess(me["distance"], 500.0)

        viewer = await self.peer("VIEWER")
        await viewer.send("leaderboard", mode="circuit")
        leaderboard = await viewer.expect("leaderboard")
        self.assertFalse(any(entry["name"] == "HOST" for entry in leaderboard["leaderboard"]))

    async def test_missing_finish_packet_still_uses_validated_snapshots(self):
        host = await self.peer("HOST")
        await host.send("quick_match", mode="online")
        await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        countdown = await self.start_match([host])
        assert self.server is not None
        start_at = int(countdown["startAtMs"])
        end_at = start_at + int(self.race_duration * 1000)
        while authority.now_ms() < end_at - 120:
            elapsed = max(0.0, (authority.now_ms() - start_at) / 1000.0)
            distance = 22.0 * elapsed + 4.0
            await host.send(
                "snapshot",
                distance=round(distance, 2),
                speed=22.0,
                score=int(distance * 10),
                coins=0,
                lane=1,
                runnerState="run",
                lives=1,
                nearMisses=0,
                powerUpsUsed=0,
                survivalTime=round(elapsed, 2),
            )
            await asyncio.sleep(0.12)
        # Deliberately never send `finish`; the authority must close the timed race itself.
        result = await host.expect("race_results", timeout=6.0)
        me = find_player(result, host.player_id)
        self.assertIsNotNone(me)
        self.assertTrue(me["finished"])
        self.assertGreaterEqual(me["distance"], 40.0)
        self.assertGreater(me["rewardsCredits"], 0)
        self.assertTrue(result["rewardGranted"])

    async def test_dead_finish_receives_no_competitive_rewards(self):
        host = await self.peer("HOST")
        await host.send("quick_match", mode="online")
        await host.expect("room_state", where=lambda m: bool(m.get("roomCode")))
        countdown = await self.start_match([host])

        await asyncio.sleep(0.15)
        await host.send(
            "snapshot",
            distance=12.0,
            speed=20.0,
            score=120,
            coins=0,
            lane=1,
            runnerState="run",
            lives=1,
            nearMisses=0,
            powerUpsUsed=0,
            survivalTime=0.15,
        )
        await host.send(
            "finish",
            distance=12.0,
            speed=0.0,
            score=120,
            coins=0,
            lane=1,
            runnerState="dead",
            lives=0,
            nearMisses=0,
            powerUpsUsed=0,
            survivalTime=0.2,
            finished=False,
        )
        result = await host.expect("race_results", timeout=5.0)
        me = find_player(result, host.player_id)
        self.assertFalse(me["finished"])
        self.assertEqual(me["rewardsCredits"], 0)
        self.assertFalse(result["rewardGranted"])


class ReconnectTests(AuthorityTest):
    async def test_reconnect_token_restores_the_live_race(self):
        host = await self.peer("HOST")
        guest = await self.peer("GUEST")
        room = await self.create_public_lobby([host, guest])
        self.assertEqual(len(human_players(room)), 2)
        countdown = await self.start_match([host, guest])

        await guest.send(
            "snapshot",
            distance=8.0,
            speed=20.0,
            score=80,
            coins=0,
            lane=1,
            runnerState="run",
            lives=1,
            nearMisses=0,
            powerUpsUsed=0,
            survivalTime=0.3,
        )
        await asyncio.sleep(0.1)
        old_id, old_token = guest.player_id, guest.token

        await guest.close()
        disconnected = await host.expect(
            "room_state",
            timeout=3.0,
            where=lambda m: any(
                p["playerId"] == old_id and p["connected"] is False
                for p in (m.get("players") or [])
            ),
        )
        self.assertEqual(disconnected["phase"], "racing")

        replacement = await self.peer("GUEST-RECONNECT")
        await replacement.send("reconnect", playerId=old_id, token=old_token, name="GUEST")
        welcome = await replacement.expect("welcome_back")
        self.assertTrue(welcome["ok"])
        self.assertEqual(welcome["playerId"], old_id)

        restored = await replacement.expect(
            "room_state",
            where=lambda m: any(
                p["playerId"] == old_id and p["connected"] is True
                for p in (m.get("players") or [])
            ),
        )
        self.assertEqual(restored["raceId"], countdown["raceId"])
        self.assertEqual(restored["phase"], "racing")
        me = find_player(restored, old_id)
        self.assertGreaterEqual(me["distance"], 7.0)
        await replacement.expect("race_start")
        await replacement.expect("state_sync")

        intruder = await self.peer("INTRUDER")
        await intruder.send("reconnect", playerId=old_id, token="not-the-token")
        expired = await intruder.expect("session_expired")
        self.assertIn("EXPIRED", expired["error"])
        with self.assertRaises(EOFError):
            await intruder.recv(timeout=2.0)


class ProtocolTests(AuthorityTest):
    async def test_sanitisation_unknown_messages_and_second_hello(self):
        peer = await self.peer("  <HELLO>!!  ")
        self.assertEqual(peer.welcome["name"], "HELLO")
        self.assertEqual(peer.welcome["protocol"], "nv1")
        self.assertEqual(peer.welcome["clientVersion"], authority.SERVER_VERSION)

        await peer.send_raw("{this is not json")
        error = await peer.expect("error", where=lambda m: m.get("error") == "MALFORMED JSON")
        self.assertFalse(error["ok"])

        await peer.send("dance")
        error = await peer.expect("error", where=lambda m: m.get("error") == "UNKNOWN MESSAGE TYPE")
        self.assertFalse(error["ok"])

        await peer.send("hello", name="SECOND")
        again = await peer.expect("welcome_back")
        self.assertEqual(again["playerId"], peer.player_id)
        self.assertEqual(again["name"], "SECOND")

        await peer.send("leaderboard", mode="circuit")
        board = await peer.expect("leaderboard")
        self.assertTrue(board["ok"])
        self.assertEqual(board["leaderboard"], [])

    async def test_unauthenticated_request_is_rejected(self):
        reader, writer = await asyncio.open_connection("127.0.0.1", self.port)
        try:
            writer.write(json.dumps({"type": "quick_match", "requestId": "raw"}).encode() + b"\n")
            await writer.drain()
            line = await asyncio.wait_for(reader.readline(), 3.0)
            message = json.loads(line)
            self.assertEqual(message["type"], "error")
            self.assertEqual(message["error"], "NOT AUTHENTICATED")
        finally:
            writer.close()
            await writer.wait_closed()


if __name__ == "__main__":
    unittest.main(verbosity=2)
