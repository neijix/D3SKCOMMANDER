//! d3skcommander — a cross-platform two-panel file manager.
//!
//! `core` holds everything that touches the file system, archives, the network or
//! crypto, with no GUI dependency, so it is unit-tested on its own. `ui` (egui) only
//! calls into `core`.

pub mod config;
pub mod core;
pub mod i18n;
pub mod ui;
