# KawaiiDesktopPets

Developed by **Thovarisk** — [GitHub Repository](https://github.com/Francisco-Tovar/DesktopSheep)

A lightweight Windows desktop pet overlay built with .NET 8 and WPF, inspired by the classic `sheep.exe` (*Stray Sheep* / *eSheep*) desktop mate, featuring Sheep, Dachshund, and Schnauzer companions.

## Features
- **Window detection, landing & climbing**: Sheep detect open Windows apps, land on top of title bars, ride along when you move windows, climb up window sides, and fall off edges.
- **Flock mode (multi-sheep)**: Spawn up to 12 sheep from the tray icon or right-click menu; sheep walking past each other bump and react (`Baa!`, `!`).
- **Classic sheep antics & animations**: Walk, zoomies (run), sit, sleep (`Zzz...`), yawn, graze on grass/flowers, surprise hops, and a rare UFO abduction easter egg.
- **File drag-and-drop "eating"**: Drop any file onto a sheep to feed it (`Nom nom!`); dropping a `.wav` file plays the audio.
- **Synthesized retro sound effects**: Built-in soft "Baa!", boing, nom, and UFO sounds (toggleable via Sound Effects).
- **Non-intrusive transparent overlay**: Uses WPF per-pixel transparency (`AllowsTransparency="True"`) and `WS_EX_TOOLWINDOW` to stay hidden from `Alt+Tab`.
- **Interactive dragging & gravity**: Left-click and drag any sheep anywhere on screen; release to drop or toss it with gravity and bounce. Double-click to pet (`♥ Baa!`).
- **Right-click menu & System Tray icon**: Add/remove sheep, scatter or center the flock, trigger behaviors, toggle Gravity, Window Climbing & Landing, Sound Effects, Always on Top, Click-Through mode, or Exit.

## Build & Run (Windows)
Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet run
```

To publish a standalone single-file `.exe`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
