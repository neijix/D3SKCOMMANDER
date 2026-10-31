//! Developer hook for smoke tests: `D3SK_SCREENSHOT=<file.png>` saves the window's first
//! settled frame to that file and quits. `D3SK_SCREENSHOT_CMD` optionally names a command
//! (as in the menus, e.g. `Keys`) to run before the capture. Does nothing when unset.

use std::path::PathBuf;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

use eframe::egui;

pub(crate) struct Smoke {
    path: PathBuf,
    command: Option<String>,
    started: Instant,
    requested: bool,
    done: Arc<AtomicBool>,
}

impl Smoke {
    pub(crate) fn from_env() -> Option<Smoke> {
        let path = std::env::var_os("D3SK_SCREENSHOT")?;
        Some(Smoke {
            path: PathBuf::from(path),
            command: std::env::var("D3SK_SCREENSHOT_CMD").ok(),
            started: Instant::now(),
            requested: false,
            done: Arc::new(AtomicBool::new(false)),
        })
    }

    /// The command to run once, before the capture.
    pub(crate) fn take_command(&mut self) -> Option<String> {
        if self.started.elapsed() > Duration::from_millis(800) {
            self.command.take()
        } else {
            None
        }
    }

    /// Call every frame: asks for a screenshot once the UI has settled, saves it, quits.
    pub(crate) fn frame(&mut self, ctx: &egui::Context) {
        ctx.request_repaint();
        if self.done.load(Ordering::Relaxed) {
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            return;
        }
        if self.requested || self.started.elapsed() < Duration::from_millis(2000) {
            return;
        }
        self.requested = true;
        let path = self.path.clone();
        let done = self.done.clone();
        let repaint = ctx.clone();
        let callback = egui::ScreenshotCallback::new(move |img| {
            let [w, h] = img.size;
            let bytes: Vec<u8> = img.pixels.iter().flat_map(|c| c.to_array()).collect();
            let saved =
                image::RgbaImage::from_raw(w as u32, h as u32, bytes).map(|b| b.save(&path));
            match saved {
                Some(Ok(())) => eprintln!("screenshot saved: {}", path.display()),
                Some(Err(e)) => eprintln!("screenshot failed: {e}"),
                None => eprintln!("screenshot failed: bad buffer"),
            }
            done.store(true, Ordering::Relaxed);
            repaint.request_repaint();
        });
        ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(callback));
    }
}
