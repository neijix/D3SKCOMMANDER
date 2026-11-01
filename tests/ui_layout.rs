//! Automated display tests: the real app runs headless (egui_kittest, no GPU) and the
//! position of what it draws is read back through its accessibility tree. They catch
//! layout bugs such as a panel drawn past its half of the window or a tool window that
//! opens off screen, on every OS the CI runs.

use std::path::Path;

use d3skcommander::config::Settings;
use d3skcommander::i18n::{self, Lang};
use d3skcommander::ui::keys::Command;
use d3skcommander::ui::{App, picker};
use eframe::egui::{self, Rect, Vec2};
use egui_kittest::{Harness, kittest::Queryable};

/// Slack for rounding and frame strokes, in points.
const SLACK: f32 = 2.0;

const HEADERS: [&str; 5] = ["Name", "Ext", "Size", "Date", "Attr"];

/// A folder with a few files, used by both panels.
fn sample_dir() -> tempfile::TempDir {
    let dir = tempfile::tempdir().unwrap();
    for i in 0..40 {
        let name = format!("file number {i} with a rather long name.txt");
        std::fs::write(dir.path().join(name), "x").unwrap();
    }
    std::fs::create_dir(dir.path().join("a folder")).unwrap();
    dir
}

fn app_harness<'a>(dir: &Path, size: Vec2, ppp: f32) -> Harness<'a, App> {
    let mut s = Settings {
        lang: Lang::En,
        ..Settings::default()
    };
    s.left.tabs = vec![dir.to_path_buf()];
    s.right.tabs = vec![dir.to_path_buf()];
    let settings_path = dir.join("settings.json");
    let mut h = Harness::builder()
        .allow_missing_glyphs()
        .with_size(size)
        .with_pixels_per_point(ppp)
        .build_eframe(move |_cc| App::with_settings(s, settings_path));
    h.run_steps(4);
    h
}

fn screen(h: &Harness<'_, App>) -> Rect {
    h.ctx.content_rect()
}

fn inside(outer: Rect, inner: Rect) -> bool {
    outer.expand(SLACK).contains_rect(inner)
}

/// Every column title of both panels lies inside its own half of the window.
fn assert_panels_fit(h: &Harness<'_, App>, what: &str) {
    let screen = screen(h);
    let mid = screen.center().x;
    let halves = [
        Rect::from_min_max(screen.min, egui::pos2(mid, screen.max.y)),
        Rect::from_min_max(egui::pos2(mid, screen.min.y), screen.max),
    ];
    let mut names = 0;
    for label in HEADERS {
        for node in h.query_all_by_label_contains(label) {
            let r = node.rect();
            if r.height() > 40.0 {
                continue; // not a header button
            }
            let half = if r.center().x < mid { 0 } else { 1 };
            assert!(
                inside(halves[half], r),
                "{what}: header {label:?} at {r:?} spills out of panel {half} ({:?})",
                halves[half]
            );
            names += usize::from(label == "Name");
        }
    }
    assert_eq!(names, 2, "{what}: expected a Name column in each panel");
}

/// The function-key buttons and menu titles stay inside the window.
fn assert_bars_fit(h: &Harness<'_, App>, what: &str) {
    let screen = screen(h);
    for label in ["F3 ", "F8 ", "Alt+F4 ", "Files", "Help"] {
        let node = h.get_by_label_contains(label);
        assert!(
            inside(screen, node.rect()),
            "{what}: {label:?} at {:?} is outside the window {screen:?}",
            node.rect()
        );
    }
}

/// Every window, popup and menu egui shows lies inside the screen.
fn assert_areas_on_screen(h: &Harness<'_, App>, what: &str) {
    let screen = screen(h);
    let rects: Vec<(egui::LayerId, Rect)> = h.ctx.memory(|m| {
        m.layer_ids()
            .filter(|l| l.order != egui::Order::Background)
            .filter_map(|l| m.area_rect(l.id).map(|r| (l, r)))
            .collect()
    });
    for (layer, r) in rects {
        if !h.ctx.memory(|m| m.areas().is_visible(&layer)) {
            continue;
        }
        assert!(
            inside(screen, r),
            "{what}: {layer:?} at {r:?} is outside the window {screen:?}"
        );
    }
}

const SIZES: [(f32, f32); 4] = [
    (640.0, 400.0),
    (1024.0, 640.0),
    (1280.0, 800.0),
    (1920.0, 1080.0),
];

#[test]
fn main_window_fits_at_every_size_and_scale() {
    i18n::set_lang(Lang::En);
    let dir = sample_dir();
    for ppp in [1.0, 1.5, 2.0] {
        for (w, ht) in SIZES {
            let what = format!("{w}x{ht} @{ppp}x");
            let h = app_harness(dir.path(), Vec2::new(w, ht), ppp);
            assert_panels_fit(&h, &what);
            assert_bars_fit(&h, &what);
        }
    }
}

#[test]
fn panels_shrink_back_after_the_window_gets_smaller() {
    i18n::set_lang(Lang::En);
    let dir = sample_dir();
    let mut h = app_harness(dir.path(), Vec2::new(1920.0, 1080.0), 1.0);
    for (w, ht) in [
        (1280.0, 800.0),
        (800.0, 600.0),
        (640.0, 400.0),
        (1600.0, 900.0),
    ] {
        h.set_size(Vec2::new(w, ht));
        h.run_steps(4);
        assert_panels_fit(&h, &format!("resized to {w}x{ht}"));
    }
}

/// Opens `cmd` the way a user does: its menu title, then its entry.
fn open_from_menu(h: &mut Harness<'_, App>, cmd: Command, menu: &str) {
    h.get_by_label(menu).click();
    h.run_steps(2);
    h.get_by_label_contains(cmd.label()).click();
    h.run_steps(6);
}

#[test]
fn tool_windows_open_on_screen() {
    i18n::set_lang(Lang::En);
    let dir = sample_dir();
    let tools = [
        (Command::Search, "Commands"),
        (Command::MultiRename, "Commands"),
        (Command::Sync, "Commands"),
        (Command::Hotlist, "Commands"),
        (Command::ComparePanels, "Mark"),
        (Command::Ftp, "Net"),
        (Command::NetConnect, "Net"),
        (Command::Settings, "Configuration"),
        (Command::Log, "Configuration"),
        (Command::Keys, "Help"),
        (Command::About, "Help"),
        (Command::MkDir, "Files"),
        (Command::Properties, "Files"),
        (Command::Catalog, "Tools"),
    ];
    for (w, ht) in [(640.0, 400.0), (1280.0, 800.0)] {
        for (cmd, menu) in tools {
            let mut h = app_harness(dir.path(), Vec2::new(w, ht), 1.0);
            open_from_menu(&mut h, cmd, menu);
            let what = format!("{cmd:?} at {w}x{ht}");
            assert_areas_on_screen(&h, &what);
        }
    }
}

#[test]
fn a_chosen_folder_opens_in_the_panel_whose_button_was_clicked() {
    i18n::set_lang(Lang::En);
    let dir = sample_dir();
    let picked = dir.path().join("picked folder");
    std::fs::create_dir(&picked).unwrap();
    std::fs::write(picked.join("inside the picked folder.txt"), "x").unwrap();
    // A headless test never opens the native dialog: it answers at once. (The Name
    // column shows names without their extension.)
    picker::answer_with(Some(picked));
    let mut h = app_harness(dir.path(), Vec2::new(1280.0, 800.0), 1.0);
    let buttons = h.query_all_by_label("📂").collect::<Vec<_>>();
    assert_eq!(buttons.len(), 2, "one browse button per panel");
    let right = buttons
        .into_iter()
        .max_by(|a, b| a.rect().center().x.total_cmp(&b.rect().center().x))
        .unwrap();
    right.click();
    h.run_steps(6);
    let file = h.get_by_label_contains("inside the picked folder");
    assert!(
        file.rect().center().x > screen(&h).center().x,
        "the right panel shows the chosen folder"
    );
}
