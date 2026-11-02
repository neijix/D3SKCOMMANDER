//! The icon set: Material Symbols Rounded (Apache-2.0, through `egui_material_icons`),
//! registered as a fallback of the proportional font so an icon is just a character in
//! any label. File icons get a colour per [`FileKind`].

use eframe::egui::{self, Color32, FontId, TextFormat, text::LayoutJob};
use egui_material_icons::icons as m;

use crate::core::filekind::FileKind;

/// An icon glyph, usable inside any string (`format!("{DRIVE} C:")`).
pub type Icon = &'static str;

pub const DRIVE: Icon = m::ICON_STORAGE.codepoint;
pub const UP: Icon = m::ICON_ARROW_UPWARD.codepoint;
pub const HOTLIST: Icon = m::ICON_STAR.codepoint;
pub const HISTORY: Icon = m::ICON_HISTORY.codepoint;
pub const BROWSE: Icon = m::ICON_FOLDER_OPEN.codepoint;
pub const FILTER: Icon = m::ICON_FILTER_ALT.codepoint;
pub const HOME: Icon = m::ICON_HOME.codepoint;
pub const NETWORK: Icon = m::ICON_LAN.codepoint;
pub const DELETE: Icon = m::ICON_DELETE.codepoint;
pub const SWAP_VERT: Icon = m::ICON_SWAP_VERT.codepoint;
pub const SWAP_HORIZ: Icon = m::ICON_SWAP_HORIZ.codepoint;
pub const RUN: Icon = m::ICON_PLAY_ARROW.codepoint;
pub const MOVE_UP: Icon = m::ICON_KEYBOARD_ARROW_UP.codepoint;
pub const MOVE_DOWN: Icon = m::ICON_KEYBOARD_ARROW_DOWN.codepoint;
pub const UPDATE: Icon = m::ICON_UPGRADE.codepoint;
pub const DOWNLOAD: Icon = m::ICON_DOWNLOAD.codepoint;
pub const RESTART: Icon = m::ICON_RESTART_ALT.codepoint;
pub const OPEN_PAGE: Icon = m::ICON_OPEN_IN_NEW.codepoint;
pub const OK: Icon = m::ICON_CHECK_CIRCLE.codepoint;
pub const WARNING: Icon = m::ICON_WARNING.codepoint;
pub const NEWER: Icon = m::ICON_NEW_RELEASES.codepoint;
pub const FOLDER: Icon = m::ICON_FOLDER.codepoint;
pub const FILE: Icon = m::ICON_DRAFT.codepoint;

/// Sets the app fonts: egui's defaults with the icon font right after the text font.
/// Not last: egui's own fallback icon fonts also use the private-use area, and the first
/// font with a glyph wins (`\u{f80e}`, a drive, came out as a "4").
pub fn install(ctx: &egui::Context) {
    let insert = egui_material_icons::font_insert();
    let mut defs = egui::FontDefinitions::default();
    defs.font_data
        .insert(insert.name.clone(), std::sync::Arc::new(insert.data));
    for family in [egui::FontFamily::Proportional, egui::FontFamily::Monospace] {
        let list = defs.families.entry(family).or_default();
        let at = list.len().min(1);
        list.insert(at, insert.name.clone());
    }
    ctx.set_fonts(defs);
}

/// The icon of a file kind.
pub fn of_kind(kind: FileKind) -> Icon {
    match kind {
        FileKind::Parent => m::ICON_DRIVE_FOLDER_UPLOAD.codepoint,
        FileKind::Folder => FOLDER,
        FileKind::Link => m::ICON_LINK.codepoint,
        FileKind::Archive => m::ICON_FOLDER_ZIP.codepoint,
        FileKind::Image => m::ICON_IMAGE.codepoint,
        FileKind::Audio => m::ICON_AUDIO_FILE.codepoint,
        FileKind::Video => m::ICON_MOVIE.codepoint,
        FileKind::Pdf => m::ICON_PICTURE_AS_PDF.codepoint,
        FileKind::Document => m::ICON_DESCRIPTION.codepoint,
        FileKind::Spreadsheet => m::ICON_TABLE_CHART.codepoint,
        FileKind::Code => m::ICON_CODE.codepoint,
        FileKind::Executable => m::ICON_TERMINAL.codepoint,
        FileKind::Text => m::ICON_ARTICLE.codepoint,
        FileKind::Other => FILE,
    }
}

/// The tint of a file kind, a little brighter on a dark background.
pub fn color(kind: FileKind, dark: bool) -> Color32 {
    let (r, g, b) = match kind {
        FileKind::Parent | FileKind::Other | FileKind::Text => {
            return if dark {
                Color32::from_gray(170)
            } else {
                Color32::from_gray(110)
            };
        }
        FileKind::Folder => (232, 168, 56),
        FileKind::Link => (38, 166, 154),
        FileKind::Archive => (149, 117, 205),
        FileKind::Image => (76, 175, 80),
        FileKind::Audio => (236, 64, 122),
        FileKind::Video => (239, 108, 0),
        FileKind::Pdf => (229, 57, 53),
        FileKind::Document => (66, 133, 244),
        FileKind::Spreadsheet => (46, 160, 67),
        FileKind::Code => (41, 151, 214),
        FileKind::Executable => (120, 144, 156),
    };
    if dark {
        Color32::from_rgb(r, g, b)
    } else {
        Color32::from_rgb(r * 4 / 5, g * 4 / 5, b * 4 / 5)
    }
}

/// `icon  text` with the icon in `tint` and the text in `text_color` (`None` = the
/// widget's own colour), for a label or a selectable row.
pub fn labelled(
    ui: &egui::Ui,
    icon: Icon,
    tint: Color32,
    text: &str,
    text_color: Option<Color32>,
    strong: bool,
) -> LayoutJob {
    let font: FontId = egui::TextStyle::Body.resolve(ui.style());
    let mut job = LayoutJob::default();
    // The glyphs are drawn a little small for their em box: 20 % larger matches the text.
    job.append(
        icon,
        0.0,
        TextFormat {
            font_id: FontId::new(font.size * 1.2, font.family.clone()),
            color: tint,
            valign: egui::Align::Center,
            ..Default::default()
        },
    );
    let color = text_color.unwrap_or_else(|| {
        if strong {
            ui.visuals().strong_text_color()
        } else {
            ui.visuals().text_color()
        }
    });
    job.append(
        text,
        6.0,
        TextFormat {
            font_id: font,
            color,
            ..Default::default()
        },
    );
    job
}

/// An icon-only button with a tooltip.
pub fn button(ui: &mut egui::Ui, icon: Icon, hover: &str) -> egui::Response {
    ui.add(egui::Button::new(egui::RichText::new(icon).size(18.0)).frame_when_inactive(false))
        .on_hover_text(hover)
}
