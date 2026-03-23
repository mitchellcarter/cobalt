# Cobalt

A desktop app that tails your Rust client log in real time and persists everything to a web API which enriches the data from Battlemetrics and Steam.

## What it does

Every time you die in Rust, the app parses the kill line from `output_log.txt`, looks up the killer's Steam profile and Battlemetrics history, and displays it in a live kill feed.

## Requirements
- Rust installed via Steam (needs access to `output_log.txt`)

## Setup

1. Clone the repository:
   ```
   git clone https://github.com/mitchellcarter/cobalt.git
   cd cobalt
   ```

2. Edit `Cobalt.Watcher/appsettings.json` (Note: each slash should be escaped with an additional slash):
   ```json
   {
     "LogFilePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Rust\\output_log.txt",
   }
   ```

   The default log path is `C:\Program Files (x86)\Steam\steamapps\common\Rust\output_log.txt`. Adjust it to match your Steam library location.

3. Run the app:
   ```
   dotnet run --project Cobalt
   ```

## Configuration

| Key | Description | Required |
|-----|-------------|----------|
| `LogFilePath` | Full path to `output_log.txt` | Yes |

## Building a release

```
dotnet publish Cobalt -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o ./publish/win-x64
```

Replace `win-x64` with `linux-x64` for Linux. The output folder contains the executable and all required native libraries (including the SQLite binary). Zip the folder and distribute it as-is.

CI runs on every push and produces `win-x64` and `linux-x64` builds automatically when merging to `main`.

## Notes

- The app only tracks deaths caused by other players. Environmental deaths (fall damage, radiation, etc.) are ignored.
