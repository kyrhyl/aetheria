# Aetheria SpaceTimeDB Module

This folder contains the local SpaceTimeDB server module for the `aetheria` database.

## Local workflow

1. Start local SpaceTimeDB server (separate terminal):

```powershell
spacetime start
```

2. Build module:

```powershell
spacetime build -p aetheria-db-rust/spacetimedb
```

3. Publish/update local database:

```powershell
spacetime publish aetheria -p aetheria-db-rust/spacetimedb -s http://localhost:3000 -y
```

4. Verify database exists:

```powershell
spacetime list --server http://localhost:3000 -y
```

## Notes

- The module currently uses the default template scaffold.
- `wasm-opt` is optional; install it for smaller/faster wasm output.
