#!/usr/bin/env python3
"""Checks the production Compose file the Aspire AppHost generates (scripts/deploy/publish-compose.sh).

The file is generated once per run into artifacts/compose-test/ (or taken from JARVIS_GENERATED_COMPOSE_DIR, which
must hold base/ and all/ subdirectories) and rendered with `docker compose config` so assertions see what Docker
would run. Needs a .NET 10 SDK (or Docker) and the docker compose plugin.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
ALL_FEATURES = "browser github home-assistant coding tunnel"

REQUIRED_ENV = {
    "POSTGRES_PASSWORD": "postgres-test-secret",
    "TEMPORAL_PASSWORD": "temporal-test-secret",
    "S3_ACCESS_KEY": "jarvis_object_store",
    "S3_SECRET_KEY": "s3-test-secret",
    "GARAGE_CONFIG_FILE": "/tmp/garage.toml",
    "JARVIS_UID": "1000",
    "JARVIS_GID": "1000",
    "CODEX_HOME_DIR": "/tmp/codex-home",
    "JARVIS_DATA_PROTECTION_KEYS_DIR": "/tmp/jarvis-data-protection-keys",
    "JARVIS_BACKUP_DIR": "/tmp/jarvis-backups",
    "MCP_RUNNER_TOKEN": "mcp-runner-test-token-0123456789abcdef",
    "AUTH_ISSUER": "https://jarvis.example.com",
    "AUTH_AUDIENCE": "jarvis-api",
    "AUTH_SIGNING_KEY": "production-test-signing-key-32bytes!",
    "JARVIS_DOMAIN": "jarvis.example.com",
    "LIVEKIT_DOMAIN": "voice.example.com",
    "JARVIS_WEB_ORIGIN": "https://jarvis.example.com",
    "LIVEKIT_API_KEY": "devkey",
    "LIVEKIT_API_SECRET": "jarvis-local-livekit-development-secret",
    "VOICE_WORKER_SECRET": "voice-test-secret",
    "HOME_ASSISTANT_MCP_URL": "https://ha.example.com/api/mcp",
    "CODING_REPO_PATH": "/tmp/repo",
}


def _publish(directory: Path, features: str) -> Path:
    compose = directory / "docker-compose.yaml"
    if not compose.exists():
        subprocess.run(
            ["scripts/deploy/publish-compose.sh", str(directory)],
            check=True,
            cwd=REPO_ROOT,
            env={**os.environ, "JARVIS_FEATURES": features},
            stdout=subprocess.DEVNULL,
        )
    return compose


def _render(compose: Path, extra_env: dict[str, str] | None = None, profiles: tuple[str, ...] = ()) -> dict:
    with tempfile.TemporaryDirectory() as tmp:
        env_file = Path(tmp) / ".env.production"
        values = {**REQUIRED_ENV, **(extra_env or {})}
        env_file.write_text("".join(f"{k}={v}\n" for k, v in values.items()), encoding="utf-8")
        command = ["docker", "compose", "-p", "jarvis", "--project-directory", str(REPO_ROOT),
                   "--env-file", str(env_file), "-f", str(compose)]
        for profile in profiles:
            command += ["--profile", profile]
        completed = subprocess.run(command + ["config", "--format", "json"], check=True, capture_output=True,
                                   text=True, cwd=REPO_ROOT)
        return json.loads(completed.stdout)


@unittest.skipUnless(shutil.which("docker"), "docker is not installed")
class ProductionComposeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        root = Path(os.environ.get("JARVIS_GENERATED_COMPOSE_DIR", REPO_ROOT / "artifacts" / "compose-test"))
        cls.base_file = _publish(root / "base", "")
        cls.all_file = _publish(root / "all", ALL_FEATURES)
        cls.base = _render(cls.base_file, profiles=("migration",))
        cls.all = _render(cls.all_file, profiles=("migration", "direct-edge"))

    def mounts(self, name: str, path: str, project: dict) -> bool:
        return any(volume.get("source", "").endswith(path) for volume in self.service(name, project).get("volumes", []))

    def service(self, name: str, project: dict | None = None) -> dict:
        return (project or self.base)["services"][name]

    def test_no_hand_written_compose_files_remain(self) -> None:
        tracked = subprocess.run(["git", "ls-files", "*docker-compose*.yml", "*docker-compose*.yaml"],
                                 capture_output=True, text=True, cwd=REPO_ROOT, check=True).stdout.split()
        self.assertEqual([], tracked, "The AppHost generates Compose files; do not check one in.")

    def test_project_keeps_existing_names_so_data_volumes_are_reused(self) -> None:
        self.assertEqual("jarvis", self.base["name"])
        for volume in ("postgres-data", "temporal-postgres-data", "garage-data", "caddy-data"):
            self.assertIn(volume, self.base["volumes"])

    def test_application_images_default_to_ghcr_and_can_be_overridden(self) -> None:
        for name in ("jarvis-api", "jarvis-migrate", "mcp-runner"):
            self.assertEqual("ghcr.io/ykdbontekoe/jarvis-ai/api:latest", self.service(name)["image"])
        self.assertEqual("ghcr.io/ykdbontekoe/jarvis-ai/worker:latest", self.service("jarvis-worker")["image"])
        pinned = _render(self.base_file, profiles=("migration",), extra_env={"JARVIS_API_IMAGE": "ghcr.io/example/jarvis-ai/api:deadbeef",
                                          "JARVIS_WORKER_IMAGE": "ghcr.io/example/jarvis-ai/worker:deadbeef"})
        self.assertEqual("ghcr.io/example/jarvis-ai/api:deadbeef", pinned["services"]["jarvis-api"]["image"])
        self.assertEqual("ghcr.io/example/jarvis-ai/worker:deadbeef", pinned["services"]["jarvis-worker"]["image"])

    def test_migration_is_an_explicit_one_shot_service(self) -> None:
        migrate = self.service("jarvis-migrate")
        self.assertEqual(["migration"], migrate["profiles"])
        self.assertEqual(["Jarvis.Api.dll", "migrate"], migrate["command"])
        self.assertEqual("false", self.service("jarvis-api")["environment"]["Database__ApplyMigrationsAtStartup"])

    def test_deploy_generates_the_file_and_migrates_before_replacing_services(self) -> None:
        deploy = (REPO_ROOT / "scripts" / "deploy" / "remote-up.sh").read_text(encoding="utf-8")
        self.assertLess(deploy.index("jarvis_publish_compose"), deploy.index("jarvis_compose pull"))
        self.assertLess(deploy.index("--rm --no-deps jarvis-migrate"), deploy.index("-d --no-build --remove-orphans"))
        self.assertIn("flock -n 9", deploy)

    def test_application_containers_are_hardened(self) -> None:
        for name in ("jarvis-api", "jarvis-worker", "mcp-runner", "jarvis-migrate", "backup"):
            service = self.service(name)
            self.assertTrue(service.get("read_only"), name)
            self.assertEqual("1000:1000", service.get("user"), name)

    def test_databases_sit_on_internal_networks(self) -> None:
        for network in ("data", "temporal-data", "mcp"):
            self.assertTrue(self.base["networks"][network].get("internal"), network)
        self.assertEqual({"data"}, set(self.service("postgres")["networks"]))
        self.assertNotIn("ports", self.service("postgres"))

    def test_backup_service_reaches_only_the_databases_and_reads_keys(self) -> None:
        backup = self.service("backup")
        self.assertEqual({"data", "temporal-data"}, set(backup["networks"]))
        keys = next(v for v in backup["volumes"] if v["target"] == "/data-protection-keys")
        self.assertTrue(keys.get("read_only"))
        self.assertNotIn("ports", backup)

    def test_mcp_runner_is_isolated_from_databases(self) -> None:
        runner = self.service("mcp-runner")
        self.assertEqual(["Jarvis.Api.dll", "mcp-runner"], runner["command"])
        self.assertEqual({"mcp", "mcp-egress"}, set(runner["networks"]))
        self.assertEqual(["ALL"], runner["cap_drop"])
        self.assertEqual(512, runner["deploy"]["resources"]["limits"]["pids"])
        self.assertNotIn("ports", runner)

    def test_nothing_mounts_the_docker_socket(self) -> None:
        for project in (self.base, self.all):
            for name, service in project["services"].items():
                for volume in service.get("volumes", []):
                    self.assertNotIn("docker.sock", volume.get("source", ""), name)

    def test_signal_defaults_to_the_internal_service(self) -> None:
        self.assertEqual("http://signal-cli:8080", self.service("jarvis-api")["environment"]["Channels__Signal__BaseUrl"])
        self.assertNotIn("ports", self.service("signal-cli"))

    def test_optional_features_are_off_by_default(self) -> None:
        api = self.service("jarvis-api")["environment"]
        self.assertNotIn("Mcp__Servers__github__Name", api)
        self.assertNotIn("playwright-mcp", self.base["services"])
        caddy = self.service("caddy")
        self.assertNotIn("profiles", caddy)

    def test_only_ports_80_and_443_are_public(self) -> None:
        def public(service: str, project: dict) -> set[str]:
            return {f'{port["published"]}/{port.get("protocol", "tcp")}'
                    for port in self.service(service, project).get("ports", [])
                    if port.get("host_ip") not in ("127.0.0.1", "::1")}

        exposed = {name: public(name, self.base) for name in self.base["services"]}
        self.assertEqual({"80/tcp", "443/tcp"}, exposed.pop("caddy"))
        # LiveKit media: UDP 443 directly, ICE-TCP through Caddy's TCP 443 (infra/caddy/Caddyfile).
        self.assertEqual({"443/udp"}, exposed.pop("livekit"))
        self.assertEqual({}, {name: ports for name, ports in exposed.items() if ports})
        self.assertTrue(self.mounts("livekit", "infra/livekit/production.yaml", self.base))

    def test_caddy_is_built_with_the_layer4_plugin(self) -> None:
        caddy = self.service("caddy")
        self.assertTrue(caddy["build"]["context"].endswith("infra/caddy"))
        dockerfile = (REPO_ROOT / "infra/caddy/Dockerfile").read_text()
        self.assertIn("github.com/mholt/caddy-l4@", dockerfile)
        caddyfile = (REPO_ROOT / "infra/caddy/Caddyfile").read_text()
        self.assertIn("proxy livekit:443", caddyfile)
        self.assertIn("protocols h1 h2", caddyfile)

    def test_tunnel_keeps_dedicated_media_ports(self) -> None:
        ports = {f'{port["published"]}/{port.get("protocol", "tcp")}' for port in self.service("livekit", self.all)["ports"]}
        self.assertEqual({"7880/tcp", "7881/tcp"} | {f"{port}/udp" for port in range(50000, 50101)}, ports)
        self.assertTrue(self.mounts("livekit", "infra/livekit/production-tunnel.yaml", self.all))

    def test_all_features_add_their_services_and_settings(self) -> None:
        api = self.service("jarvis-api", self.all)
        self.assertEqual("github", api["environment"]["Mcp__Servers__github__Name"])
        self.assertEqual("https://ha.example.com/api/mcp", api["environment"]["Mcp__Servers__home-assistant__Endpoint"])
        self.assertEqual("/coding/repo", api["environment"]["Coding__Repositories__0__Path"])
        self.assertIn("agents", api["networks"])
        self.assertIn("playwright-mcp", api["depends_on"])
        self.assertTrue(self.all["networks"]["agents"].get("internal"))
        self.assertEqual(["direct-edge"], self.service("caddy", self.all)["profiles"])
        self.assertEqual("15082", str(api["ports"][0]["published"]))


if __name__ == "__main__":
    unittest.main()
