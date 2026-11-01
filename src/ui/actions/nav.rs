//! Keyboard navigation inside a panel, and moving between folders.

use std::path::{Path, PathBuf};

use eframe::egui::{self, Key};

use crate::core::fs::{self as cfs, Entry};
use crate::core::launcher;
use crate::ui::App;
use crate::ui::keys;
use crate::ui::panel::{Location, Tab};

impl App {
    /// Shortcuts, then cursor keys, then type-ahead. Only called when no text field and
    /// no dialog has the keyboard.
    pub(crate) fn handle_keys(&mut self, ctx: &egui::Context) {
        if let Some(cmd) = keys::pressed_command(ctx) {
            self.run(cmd, ctx);
            return;
        }
        let (nav, texts) = ctx.input_mut(|i| {
            let shift = i.modifiers.shift;
            let mut nav = Vec::new();
            for (key, n) in [
                (Key::ArrowUp, Nav::Move(-1)),
                (Key::ArrowDown, Nav::Move(1)),
                (Key::PageUp, Nav::Move(-20)),
                (Key::PageDown, Nav::Move(20)),
                (Key::Home, Nav::Move(isize::MIN / 2)),
                (Key::End, Nav::Move(isize::MAX / 2)),
                (Key::Enter, Nav::Enter),
                (Key::Insert, Nav::Insert),
                (Key::Space, Nav::Space),
                (Key::Escape, Nav::Escape),
            ] {
                while i.key_pressed(key) {
                    i.consume_key(i.modifiers, key);
                    nav.push((n, shift));
                }
            }
            let texts: Vec<String> = i
                .events
                .iter()
                .filter_map(|e| match e {
                    egui::Event::Text(t) => Some(t.clone()),
                    _ => None,
                })
                .collect();
            (nav, texts)
        });
        for (n, shift) in nav {
            self.navigate(n, shift, ctx);
        }
        for text in texts {
            match text.as_str() {
                "*" => self.active_panel().tab_mut().invert_selection(),
                "+" | "-" | " " => {}
                s => self.active_panel().tab_mut().type_ahead(s),
            }
        }
    }

    fn navigate(&mut self, nav: Nav, shift: bool, ctx: &egui::Context) {
        let tab = self.active_panel().tab_mut();
        match nav {
            Nav::Move(delta) if shift => select_while_moving(tab, delta),
            Nav::Move(delta) => tab.move_cursor(delta),
            Nav::Insert => {
                toggle_cursor(tab);
                tab.move_cursor(1);
            }
            Nav::Space => {
                let dir = tab
                    .current()
                    .filter(|e| e.is_dir && !e.is_parent)
                    .map(|e| e.path.clone());
                match dir {
                    Some(d) if !tab.selected.contains(&d) => {
                        tab.toggle_selection(&d);
                        self.compute_dir_sizes(vec![d]);
                    }
                    _ => toggle_cursor(tab),
                }
            }
            Nav::Enter => self.open_current(ctx),
            Nav::Escape => {
                if tab.filter.take().is_some() {
                    tab.apply_filter();
                } else {
                    tab.selected.clear();
                }
            }
        }
    }

    /// Enter: open a folder or an archive, run / open a file with its default app.
    pub(crate) fn open_current(&mut self, ctx: &egui::Context) {
        let hidden = self.show_hidden();
        let tab = self.active_panel().tab_mut();
        let Some(entry) = tab.current().cloned() else {
            return;
        };
        if let Some(loc) = tab.enter_location(&entry) {
            let from = tab.location.clone();
            if tab.go(loc, hidden) && entry.is_parent {
                // Put the cursor back on the folder we came from.
                let back_to = match from {
                    Location::Local(p) => Some(p),
                    Location::Archive { file, inner } if inner.is_empty() => Some(file),
                    Location::Archive { inner, .. } => Some(PathBuf::from(inner)),
                    Location::List { .. } => None,
                };
                if let Some(p) = back_to {
                    tab.select_path(&p);
                }
            }
            return;
        }
        let path = match self.local_copy(&entry) {
            Ok(p) => p,
            Err(e) => return self.error(e),
        };
        if let Err(e) = launcher::open_with_default(&path) {
            self.error(e);
        }
        let _ = ctx;
    }

    /// A real file for `entry`: itself, or a temporary copy when it is inside an archive.
    pub(super) fn local_copy(&self, entry: &Entry) -> Result<PathBuf, String> {
        let Location::Archive { file, .. } = &self.panels[self.active].tab().location else {
            return Ok(entry.path.clone());
        };
        let dest = std::env::temp_dir()
            .join("d3skcommander")
            .join(format!("{}", std::process::id()));
        std::fs::create_dir_all(&dest).map_err(|e| e.to_string())?;
        let inner = entry.path.to_string_lossy().replace('\\', "/");
        crate::core::archive::extract(file, Some(&[inner]), &dest, &mut |_| true)
            .map_err(|e| e.to_string())?;
        Ok(dest.join(&entry.name))
    }

    pub(super) fn go_parent(&mut self) {
        let hidden = self.show_hidden();
        let tab = self.active_panel().tab_mut();
        if let Some(loc) = tab.parent_location() {
            let from = tab.location.base_dir();
            let was_local = matches!(tab.location, Location::Local(_));
            if tab.go(loc, hidden)
                && was_local
                && let Some(p) = from
            {
                tab.select_path(&p);
            }
        }
    }

    pub(super) fn go_root(&mut self) {
        let hidden = self.show_hidden();
        let dir = self.active_dir().unwrap_or_else(cfs::home_dir);
        let root = dir.ancestors().last().map(Path::to_path_buf).unwrap_or(dir);
        self.active_panel()
            .tab_mut()
            .go(Location::Local(root), hidden);
    }
}

#[derive(Clone, Copy)]
enum Nav {
    Move(isize),
    Enter,
    Insert,
    Space,
    Escape,
}

/// Shift+cursor: toggles every entry the cursor passes over.
fn select_while_moving(tab: &mut Tab, delta: isize) {
    let steps = delta.unsigned_abs().min(tab.visible.len());
    for _ in 0..steps.max(1) {
        toggle_cursor(tab);
        let before = tab.cursor;
        tab.move_cursor(delta.signum());
        if tab.cursor == before {
            break;
        }
    }
}

fn toggle_cursor(tab: &mut Tab) {
    if let Some(e) = tab
        .current()
        .filter(|e| !e.is_parent)
        .map(|e| e.path.clone())
    {
        tab.toggle_selection(&e);
    }
}
