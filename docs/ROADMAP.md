# Roadmap

First, the C# app (0.2.0) reaches what the Rust v0.1 did (README, Status). Then, what v0.1
did not do, roughly in priority order.

- **Native RAR / ARJ / LZH / CAB / ACE / UC2.** Today these are read through an external `7z`
  or `unrar` on `PATH`. ACE and UC2 have no maintained .NET reader.
- **FTP through an HTTP proxy** (CONNECT), and SFTP.
- **Plugin API** for packers, viewers and file systems (probably WASM or a dynamic library
  with a C ABI).
- **Video in quick view.** Videos open in the default player for now.
- **Separate tree panel** (a folder tree beside each file list).
- **Thumbnail disk cache.** Thumbnails are decoded on demand in memory.
- **Transfer over a USB or network cable** between two machines (a direct-link mode).
- **Native Wayland screenshot hook.** The smoke-test screenshot needs XWayland on some
  compositors (see the README).
- Shell context menu of the OS (Explorer / Finder) on right-click. The app has its own
  context menu today.
