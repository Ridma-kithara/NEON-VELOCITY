#!/usr/bin/env python3
"""Integration tests for the operator CLI's Phase 03 admin commands."""

from __future__ import annotations

import contextlib
import io
import os
import pathlib
import sys
import unittest

TEST_DIR = pathlib.Path(__file__).resolve().parent
BACKEND_DIR = TEST_DIR.parent
for candidate in (str(TEST_DIR), str(BACKEND_DIR)):
    if candidate not in sys.path:
        sys.path.insert(0, candidate)

from test_premium_server import ADMIN_PASSWORD, ADMIN_USERNAME, PremiumApiTestBase


class AdminCliTests(PremiumApiTestBase):
    def setUp(self) -> None:
        super().setUp()
        self._saved_env = {
            key: os.environ.get(key)
            for key in (
                "NV_PREMIUM_URL",
                "NV_ADMIN_USERNAME",
                "NV_ADMIN_PASSWORD",
            )
        }
        os.environ["NV_PREMIUM_URL"] = self.base_url
        os.environ["NV_ADMIN_USERNAME"] = ADMIN_USERNAME
        os.environ["NV_ADMIN_PASSWORD"] = ADMIN_PASSWORD

    def tearDown(self) -> None:
        for key, value in self._saved_env.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value
        super().tearDown()

    def run_cli(self, *args: str) -> str:
        from premium_admin import main

        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            code = main(list(args))
        self.assertEqual(code, 0, output.getvalue())
        return output.getvalue()

    def test_catalog_commands_show_and_update_packages(self) -> None:
        listing = self.run_cli("catalog")
        self.assertIn("STARTER CACHE", listing)
        self.assertIn("gems_80", listing)

        updated = self.run_cli(
            "set-package", "gems_80", "--name", "CLI STARTER", "--price", "555", "--enable"
        )
        self.assertIn("CLI STARTER", updated)
        self.assertIn("price=555", updated)

        catalog = self.assert_ok(self.get("/api/v1/store/catalog"))
        starter = next(
            item for item in catalog["packages"] if item["id"] == "gems_80"
        )
        self.assertEqual(starter["name"], "CLI STARTER")
        self.assertEqual(starter["priceLkr"], 555)
        self.assertEqual(starter["gems"], 80)
        self.assertTrue(starter["enabled"])

        product = self.run_cli("set-product", "skin_phantom_void", "--disable")
        self.assertIn("enabled=no", product)

    def test_accounts_and_transaction_views(self) -> None:
        session = self.register("cli-review@example.com")
        listing = self.run_cli("accounts", "--query", "cli-review@example.com")
        self.assertIn(session["accountId"], listing)
        self.assertIn("cli-review@example.com", listing)

        detail = self.run_cli("account", session["accountId"])
        self.assertIn(session["accountId"], detail)
        self.assertIn("TRANSACTIONS", detail)

        transactions = self.run_cli("transactions")
        self.assertIn("0 transaction(s)", transactions)

    def test_destructive_commands_require_explicit_arguments(self) -> None:
        from premium_admin import main

        def rejected(*args: str) -> None:
            with contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as caught:
                    main(list(args))
            self.assertEqual(caught.exception.code, 2)

        rejected("refund", "1")
        rejected("set-product", "skin_phantom_void", "--price", "0")
        rejected("transactions", "--limit", "0")


if __name__ == "__main__":
    unittest.main()
