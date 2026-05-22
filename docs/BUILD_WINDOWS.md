# Windows Build And Multi-Instance Test

## 1) Export executable from Godot

1. Open project in Godot.
2. Install export templates if prompted.
3. Open `Project > Export`.
4. Add preset: `Windows Desktop`.
5. Set export path to `build/Aetheria.exe`.
6. Export project.

## 2) Start local backend

Run in terminal:

```powershell
spacetime start
```

## 3) Launch many client instances

From repo root:

```powershell
pwsh -ExecutionPolicy Bypass -File "tools/launch-multi-client.ps1" -Count 2
```

Each instance uses:

- unique `--profile` (separate cached token/identity)
- window title suffix via `--instance`
- different window position/size

## 4) Command line options supported

- `--profile <name>`: token/profile isolation per client
- `--instance <label>`: window title label
- `--x <px>`, `--y <px>`: initial window position
- `--w <px>`, `--h <px>`: initial window size
