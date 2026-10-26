"""Discovery contract checks. Uses the same PyYAML dependency as the DTO generator."""
import json
import subprocess
import sys
import unittest
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parents[1]
SPEC = yaml.safe_load((ROOT / "docs/contracts/discovery-coffee.openapi.yaml").read_text(encoding="utf-8"))


class DiscoveryContractTests(unittest.TestCase):
    def test_generated_dtos_match_schemas(self):
        result = subprocess.run([sys.executable, str(ROOT / "dev/generate-discovery-dtos.py"), "--check"],
                                capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_removed_discovery_search_has_no_contract_dtos_or_gateway_route(self):
        self.assertNotIn("/api/v1/discovery/search", SPEC["paths"])
        self.assertIn("/api/v1/coffee-shops/search", SPEC["paths"])
        for name in ("DiscoverySearchRequest", "DiscoverySearchResult", "DiscoveryFilters", "DiscoveryBudget", "DiscoveryPaging"):
            self.assertNotIn(name, SPEC["components"]["schemas"])
            self.assertFalse((ROOT / "CoffeePeek.Contract/Dtos/Discovery" / (name + ".cs")).exists())
        gateway = json.loads((ROOT / "CoffeePeek.Gateway/appsettings.json").read_text(encoding="utf-8"))
        paths = [route["Match"]["Path"] for route in gateway["ReverseProxy"]["Routes"].values()]
        self.assertNotIn("/api/v1/discovery/search", paths)
        self.assertIn("/api/v1/coffee-shops/search", paths)

    def test_favorites_keep_legacy_fields_and_use_catalog_slug_for_drinks(self):
        schemas = SPEC["components"]["schemas"]
        favorite = schemas["DiscoveryFavoriteItem"]
        self.assertEqual({"kind", "address", "createdAtUtc"}, set(favorite["required"]))
        self.assertEqual({"coffee_shop", "roaster", "drink"}, set(favorite["properties"]["kind"]["enum"]))
        self.assertIn({"type": "null"}, favorite["properties"]["address"]["anyOf"])
        self.assertEqual({"slug"}, set(schemas["FavoriteDrinkReference"]["properties"]))
        self.assertNotIn("userId", favorite["properties"])
        self.assertNotIn("entityId", favorite["properties"])

    def test_public_read_and_owner_mutation_routes_are_distinct(self):
        paths = SPEC["paths"]
        public = paths["/api/v1/profiles/{authorSlug}/favorites"]
        self.assertEqual({"get"}, set(public))
        self.assertEqual([], public["get"]["security"])
        self.assertIn("404", public["get"]["responses"])
        for operation in paths["/api/v1/favorites/{kind}/{slug}"].values():
            self.assertEqual([{"bearerAuth": []}], operation["security"])
            self.assertIn("drink", operation["parameters"][0]["schema"]["enum"])


if __name__ == "__main__":
    unittest.main()
