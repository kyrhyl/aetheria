# Aetheria Architecture

## Folder Layout

- `scenes/`: Godot scenes by feature domain.
- `scripts/`: C# scripts mirrored to scene domains.
- `scripts/networking/spacetimedb/`: SpaceTimeDB integration code.
- `assets/`: Art, audio, shaders, and other imported resources.
- `data/`: Config and schema files used by gameplay/services.
- `tests/`: Unit and integration test projects/files.
- `aetheria-db-rust/`: Local SpaceTimeDB module source used to publish `aetheria`.

## Naming Conventions

- Scene files: `PascalCase.tscn` (example: `LoginScreen.tscn`).
- C# files/classes: `PascalCase.cs` and class name matches file name.
- Methods/properties: `PascalCase`.
- Private fields: `_camelCase`.
- Exported Godot fields: `PascalCase` for inspector readability.
- Constants: `UPPER_SNAKE_CASE`.
- Feature folders: lowercase singular/plural nouns (example: `world`, `characters`).

## MMO Module Boundaries

- `scripts/core`: startup, dependency wiring, scene flow.
- `scripts/networking`: transport, auth, session sync.
- `scripts/gameplay`: systems like combat, quests, inventory.
- `scripts/ui`: HUD, menus, and screen controllers.
- `scripts/shared`: DTOs, enums, and utility helpers.

## Initial MMO Scaffolding

- `Bootstrap` creates a `ServiceRoot` singleton node under `/root`.
- `SpaceTimeDbAuthService` is the current auth/session placeholder service.
- `PlayerReplicationService` is the current replication placeholder service.
- `LoginScreen` is the first boot flow and exercises connection/login.

## World Vertical Slice (Current)

- `LoginScreen` transitions into `GameWorld` after successful login.
- `GameWorld` owns environment, light, ground collision, and local player spawn.
- `ChunkedTerrain` generates a simple noise-based multi-chunk terrain with collision.
- `PlayerPawn` is the temporary third-person controller for movement/camera testing.

## SpaceTimeDB Auth Contract

- Current local ping endpoint: `v1/health`.
- Current local login endpoint: `v1/identity`.
- Identity response shape supported: `{ "identity": "...", "token": "..." }`.
- `RefreshEndpointTemplate` and `LogoutEndpointTemplate` are optional and can be blank for local dev.
- Runtime values are loaded from `data/configs/spacetimedb.network.tres`.

## SpaceTimeDB Local Workflow

- Start server: `spacetime start`.
- Build module: `spacetime build -p aetheria-db-rust/spacetimedb`.
- Publish database: `spacetime publish aetheria -p aetheria-db-rust/spacetimedb -s http://localhost:3000 -y`.
- Verify database: `spacetime list --server http://localhost:3000 -y`.
