#!/usr/bin/env python3
"""Tests for server-side GEM package tiers and operator configuration."""

from __future__ import annotations

import os
import pathlib
import sys
import unittest
from unittest import mock

TEST_DIR = pathlib.Path(__file__).resolve().parent
BACKEND_DIR = TEST_DIR.parent
if str(BACKEND_DIR) not in sys.path:
    sys.path.insert(0, str(BACKEND_DIR))

import premium_store  # noqa: E402


class GemPackageConfigurationTests(unittest.TestCase):
    def test_catalogue_has_all_five_required_tiers(self) -> None:
        packages = premium_store.GEM_PACKAGES
        self.assertEqual(
            [package.id for package in packages],
            ["gems_80", "gems_200", "gems_500", "gems_1200", "gems_2500"],
        )
        self.assertEqual(
            [package.tier for package in packages],
            ["STARTER", "BASIC", "PREMIUM", "MEGA", "ULTIMATE"],
        )
        for package in packages:
            self.assertGreater(package.gems, 0)
            self.assertGreaterEqual(package.bonus_gems, 0)
            self.assertGreater(package.price_lkr, 0)
            self.assertEqual(package.total_gems, package.gems + package.bonus_gems)

    def test_environment_overrides_amounts_and_price(self) -> None:
        overrides = {
            "NV_GEM_PACKAGE_GEMS_80_GEMS": "120",
            "NV_GEM_PACKAGE_GEMS_80_BONUS_GEMS": "15",
            "NV_GEM_PACKAGE_GEMS_80_PRICE_LKR": "777",
            "NV_GEM_PACKAGE_GEMS_80_NAME": "OPERATOR STARTER",
            "NV_GEM_PACKAGE_GEMS_80_DESCRIPTION": "Operator configured package.",
            "NV_GEM_PACKAGE_GEMS_80_ENABLED": "0",
        }
        with mock.patch.dict(os.environ, overrides, clear=False):
            package = premium_store._gem_package(
                "gems_80",
                "STARTER",
                "STARTER CACHE",
                80,
                0,
                490,
                "Default description.",
            )
        self.assertEqual(package.name, "OPERATOR STARTER")
        self.assertEqual(package.gems, 120)
        self.assertEqual(package.bonus_gems, 15)
        self.assertEqual(package.total_gems, 135)
        self.assertEqual(package.price_lkr, 777)
        self.assertEqual(package.description, "Operator configured package.")
        self.assertFalse(package.enabled)

    def test_invalid_environment_values_fall_back_to_defaults(self) -> None:
        overrides = {
            "NV_GEM_PACKAGE_GEMS_80_GEMS": "-1",
            "NV_GEM_PACKAGE_GEMS_80_PRICE_LKR": "free",
            "NV_GEM_PACKAGE_GEMS_80_ENABLED": "maybe",
        }
        with mock.patch.dict(os.environ, overrides, clear=False):
            package = premium_store._gem_package(
                "gems_80",
                "STARTER",
                "STARTER CACHE",
                80,
                0,
                490,
                "Default description.",
            )
        self.assertEqual(package.gems, 80)
        self.assertEqual(package.price_lkr, 490)
        self.assertTrue(package.enabled)


if __name__ == "__main__":
    unittest.main()
