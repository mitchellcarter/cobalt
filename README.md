# Cobalt

A desktop app that tails your Rust client log in real time and persists everything to a web API which enriches the data from Battlemetrics and Steam.

## What it does

Every time you die in Rust, the app parses the kill line from `output_log.txt`, looks up the killer's Steam profile and Battlemetrics history, and displays it in a live kill feed.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Rust installed via Steam (needs access to `output_log.txt`)

## Setup

1. Clone the repository:
   ```
   git clone https://github.com/mitchellcarter/cobalt.git
   cd cobalt
   ```

2. Edit `Cobalt.Watcher/appsettings.json` to set your log path and API URL:
   ```json
   {
     "LogFilePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Rust\\output_log.txt",
     "ApiBaseUrl": "https://your-cobalt-server",
     "CallbackPort": 7777,
     "AuthTokenPath": "watcher-auth.json"
   }
   ```

   Adjust `LogFilePath` to match your Steam library location. Each backslash must be escaped with an additional backslash.

3. Run the watcher:
   ```
   dotnet run --project Cobalt.Watcher
   ```

## Configuration

All settings live in `Cobalt.Watcher/appsettings.json`.

| Key | Description | Required |
|-----|-------------|----------|
| `LogFilePath` | Full path to `output_log.txt` | Yes |
| `ApiBaseUrl` | Base URL of the Cobalt API server | Yes |
| `CallbackPort` | Local port used for the OAuth callback | No (default: `7777`) |
| `AuthTokenPath` | Path to persist the auth token | No (default: `watcher-auth.json`) |

## Building a release

```
dotnet publish Cobalt.Watcher -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish/win-x64
```

Replace `win-x64` with `linux-x64` for Linux. The output folder contains the executable and all required native libraries. Zip the folder and distribute it as-is.

CI builds and publishes `win-x64` and `linux-x64` releases automatically on every push to `main`.

## Notes

- The app only tracks deaths caused by other players. Environmental deaths (fall damage, radiation, etc.) are ignored.
