#!/usr/bin/env python3
"""Unit tests for the account, identity and cloud-sync service."""

from __future__ import annotations

import json
import pathlib
import sys
import tempfile
import unittest

TEST_DIR = pathlib.Path(__file__).resolve().parent
BACKEND_DIR = TEST_DIR.parent
if str(BACKEND_DIR) not in sys.path:
    sys.path.insert(0, str(BACKEND_DIR))

from account_store import AccountError, AccountStore  # noqa: E402
from premium_store import PremiumStore  # noqa: E402


class RecordingMailer:
    def __init__(self) -> None:
        self.messages = []

    def __call__(self, to_email: str, subject: str, body: str) -> bool:
        self.messages.append((to_email, subject, body))
        return True

    def token_for(self, kind: str) -> str:
        marker = "Verification code: " if kind == "verify" else "Reset code: "
        for _to, _subject, body in reversed(self.messages):
            if marker in body:
                return body.split(marker, 1)[1].split("\n", 1)[0].strip()
        raise AssertionError("no {0} email was sent".format(kind))


class AccountStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory(prefix="nv-account-test-")
        self.data_dir = pathlib.Path(self.temp_dir.name)
        self.premium = PremiumStore(self.data_dir)
        self.mailer = RecordingMailer()
        self.sms = []
        self.store = AccountStore(
            self.data_dir,
            self.premium,
            email_sender=self.mailer,
            sms_sender=lambda phone, body: self.sms.append((phone, body)) or True,
            dev_mode=True,
        )

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def register(self, email: str = "runner@example.com", password: str = "GridRunner42") -> dict:
        return self.store.register(email, password, "RUNNER-01")

    def test_register_login_verify_and_profile(self) -> None:
        session = self.register()
        self.assertTrue(session["ok"])
        self.assertFalse(session["emailVerified"])
        self.assertEqual(session["email"], "runner@example.com")
        self.assertEqual(session["displayName"], "RUNNER-01")
        self.assertTrue(session["token"])
        self.assertEqual(session["delivery"]["reason"], "")

        profile = self.store.profile(session["accountId"])
        self.assertEqual(profile["email"], "runner@example.com")
        self.assertFalse(profile["emailVerified"])

        token = self.mailer.token_for("verify")
        verified = self.store.verify_email(token)
        self.assertTrue(verified["emailVerified"])

        logged_in = self.store.login("RUNNER@example.com", "GridRunner42")
        self.assertEqual(logged_in["accountId"], session["accountId"])
        self.assertTrue(logged_in["emailVerified"])

        with self.assertRaises(AccountError) as rejected:
            self.store.login("runner@example.com", "WrongPassword99")
        self.assertEqual(rejected.exception.code, "INVALID_CREDENTIALS")

        with self.assertRaises(AccountError) as replay:
            self.store.verify_email(token)
        self.assertEqual(replay.exception.code, "INVALID_TOKEN")

    def test_duplicate_email_and_password_policy(self) -> None:
        self.register()
        with self.assertRaises(AccountError) as duplicate:
            self.register()
        self.assertEqual(duplicate.exception.status, 409)
        self.assertEqual(duplicate.exception.code, "EMAIL_IN_USE")

        with self.assertRaises(AccountError) as short:
            self.store.register("other@example.com", "short1", "OTHER")
        self.assertEqual(short.exception.code, "INVALID_PASSWORD")
        with self.assertRaises(AccountError) as nodigit:
            self.store.register("other@example.com", "OnlyLettersHere", "OTHER")
        self.assertEqual(nodigit.exception.code, "INVALID_PASSWORD")

    def test_password_reset_revokes_tokens(self) -> None:
        session = self.register()
        response = self.store.request_password_reset("runner@example.com")
        self.assertTrue(response["ok"])
        reset_token = self.mailer.token_for("reset")
        self.assertEqual(self.store.reset_password(reset_token, "NewGridPass99")["passwordReset"], True)

        with self.assertRaises(Exception):
            self.premium.account_for_token(session["token"])

        login = self.store.login("runner@example.com", "NewGridPass99")
        self.assertTrue(login["token"])

    def test_unknown_reset_is_generic_and_does_not_send(self) -> None:
        before = len(self.mailer.messages)
        response = self.store.request_password_reset("nobody@example.com")
        self.assertTrue(response["ok"])
        self.assertEqual(response["delivery"]["reason"], "ACCOUNT_NOT_FOUND")
        self.assertEqual(len(self.mailer.messages), before)

    def test_phone_otp_sign_in_and_replay_protection(self) -> None:
        start = self.store.request_phone_code("+94 77 123 4567")
        self.assertTrue(start["ok"])
        self.assertEqual(start["phone"], "+94771234567")
        self.assertEqual(start["delivery"]["channel"], "sms")
        code = start["delivery"]["devCode"]
        verified = self.store.verify_phone_code(start["challengeId"], code, "PHONE RUNNER")
        self.assertEqual(verified["phone"], "+94771234567")
        self.assertTrue(verified["phoneVerified"])

        with self.assertRaises(AccountError) as replay:
            self.store.verify_phone_code(start["challengeId"], code)
        self.assertEqual(replay.exception.code, "INVALID_OTP")

        with self.assertRaises(AccountError) as limited:
            self.store.request_phone_code("+94771234567")
        self.assertEqual(limited.exception.code, "RATE_LIMITED")

    def test_phone_code_attempt_limit(self) -> None:
        start = self.store.request_phone_code("+15551234567")
        challenge = start["challengeId"]
        wrong = "000000" if start["delivery"]["devCode"] != "000000" else "111111"
        for _ in range(5):
            with self.assertRaises(AccountError):
                self.store.verify_phone_code(challenge, wrong)
        with self.assertRaises(AccountError) as limited:
            self.store.verify_phone_code(challenge, wrong)
        self.assertEqual(limited.exception.code, "OTP_ATTEMPTS_EXCEEDED")

    def test_provider_login_is_disabled_without_verifier(self) -> None:
        with self.assertRaises(AccountError) as disabled:
            self.store.oauth_login("apple", "x" * 64)
        self.assertEqual(disabled.exception.status, 503)
        self.assertEqual(disabled.exception.code, "PROVIDER_NOT_CONFIGURED")

        self.store.oauth_verifier = lambda provider, token: {
            "subject": "apple-subject-001",
            "email": "relay@privaterelay.appleid.com",
            "emailVerified": True,
            "name": "APPLE RUNNER",
        }
        session = self.store.oauth_login("apple", "y" * 64)
        self.assertEqual(session["provider"], "apple")
        self.assertTrue(session["emailVerified"])
        again = self.store.oauth_login("apple", "z" * 64)
        self.assertEqual(again["accountId"], session["accountId"])

    def test_guest_link_credentials(self) -> None:
        guest = self.premium.authenticate_guest("device-" + "a" * 32, "GUEST", 3600)
        linked = self.store.link_credentials(guest["accountId"], "guest@example.com", "LinkedPass77")
        self.assertEqual(linked["accountId"], guest["accountId"])
        self.assertFalse(linked["emailVerified"])
        login = self.store.login("guest@example.com", "LinkedPass77")
        self.assertEqual(login["accountId"], guest["accountId"])

    def test_sync_is_monotonic_and_never_trusts_gems(self) -> None:
        session = self.register()
        account_id = session["accountId"]
        first = self.store.merge_sync(
            account_id,
            {
                "schema": 1,
                "totalXp": 900,
                "credits": 1200,
                "bestDistance": 4000,
                "premium": {"gems": 999999, "token": "stolen"},
                "discoveredLocations": ["skyway", "reactor"],
                "missions": [
                    {"id": "m1", "progress": 2, "claimed": False, "rewardCredits": 100}
                ],
                "settings": {"musicVolume": 0.4, "premiumServerUrl": "https://evil.example"},
            },
            0,
            "sync-1",
        )
        self.assertEqual(first["revision"], 1)
        self.assertEqual(first["gems"], 0)
        self.assertNotIn("premium", first["snapshot"])
        self.assertEqual(first["snapshot"]["totalXp"], 900)

        second = self.store.merge_sync(
            account_id,
            {
                "schema": 1,
                "totalXp": 500,
                "credits": 2000,
                "bestDistance": 3000,
                "discoveredLocations": ["tunnel"],
                "missions": [
                    {"id": "m1", "progress": 4, "claimed": True, "rewardCredits": 100}
                ],
            },
            1,
            "sync-2",
        )
        self.assertEqual(second["revision"], 2)
        self.assertEqual(second["snapshot"]["totalXp"], 900)
        self.assertEqual(second["snapshot"]["credits"], 2000)
        self.assertEqual(second["snapshot"]["bestDistance"], 4000)
        self.assertEqual(
            sorted(second["snapshot"]["discoveredLocations"]),
            ["reactor", "skyway", "tunnel"],
        )
        mission = second["snapshot"]["missions"][0]
        self.assertEqual(mission["progress"], 4)
        self.assertTrue(mission["claimed"])
        self.assertIn("totalXp", second["conflicts"])

        replay = self.store.merge_sync(
            account_id,
            {"schema": 1, "totalXp": 900},
            1,
            "sync-2",
        )
        self.assertEqual(replay["revision"], 2)

        with self.assertRaises(AccountError) as ahead:
            self.store.merge_sync(account_id, {"schema": 1}, 99, "sync-3")
        self.assertEqual(ahead.exception.code, "SYNC_REVISION_AHEAD")

    def test_sync_rejects_unsupported_schema(self) -> None:
        session = self.register()
        with self.assertRaises(AccountError) as error:
            self.store.merge_sync(session["accountId"], {"schema": 99}, 0, "sync-x")
        self.assertEqual(error.exception.code, "UNSUPPORTED_SYNC_SCHEMA")

    def test_transaction_records_are_idempotent(self) -> None:
        session = self.register()
        created, first = self.store.record_transaction(
            "stripe", "evt_001", session["accountId"], "gems_80", 80, 490, "LKR",
            "SUCCEEDED", "runner@example.com", "checkout.session.completed",
        )
        self.assertTrue(created)
        again, duplicate = self.store.record_transaction(
            "stripe", "evt_001", session["accountId"], "gems_80", 80, 490, "LKR",
            "SUCCEEDED", "runner@example.com", "retry",
        )
        self.assertFalse(again)
        self.assertEqual(first["id"], duplicate["id"])
        history = self.store.transactions(session["accountId"])
        self.assertEqual(len(history), 1)
        self.assertEqual(history[0]["gems"], 80)


if __name__ == "__main__":
    unittest.main()
