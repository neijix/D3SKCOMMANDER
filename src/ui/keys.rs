//! Commands and their keyboard shortcuts (classic orthodox file manager layout).

use eframe::egui::{self, Key, KeyboardShortcut, Modifiers};

use crate::i18n::tr;

/// One table drives the enum, the ordered list of all commands and the i18n key of each.
macro_rules! commands {
    ($($variant:ident => $key:literal,)*) => {
        #[derive(Clone, Copy, PartialEq, Eq, Debug)]
        pub enum Command {
            $($variant,)*
        }

        impl Command {
            /// Every command, in declaration order.
            pub fn all() -> &'static [Command] {
                &[$(Command::$variant,)*]
            }

            fn i18n_key(self) -> &'static str {
                match self {
                    $(Command::$variant => $key,)*
                }
            }
        }
    };
}

commands! {
    View => "cmd.view",
    Edit => "cmd.edit",
    EditNew => "cmd.edit_new",
    Copy => "cmd.copy",
    Move => "cmd.move",
    RenameInPlace => "cmd.rename",
    MkDir => "cmd.mkdir",
    Delete => "cmd.delete",
    DeletePermanent => "cmd.delete_perm",
    Pack => "cmd.pack",
    Unpack => "cmd.unpack",
    BackupZip => "cmd.backup",
    Properties => "cmd.properties",
    OpenWith => "cmd.open_with",
    Quit => "cmd.quit",
    SelectGroup => "cmd.select_group",
    UnselectGroup => "cmd.unselect_group",
    SelectAll => "cmd.select_all",
    UnselectAll => "cmd.unselect_all",
    Invert => "cmd.invert",
    SelectSameExt => "cmd.select_same_ext",
    CopyNames => "cmd.copy_names",
    CopyPaths => "cmd.copy_paths",
    ComparePanels => "cmd.compare_dirs",
    Search => "cmd.search",
    MultiRename => "cmd.multirename",
    CompareFiles => "cmd.compare_files",
    Sync => "cmd.sync",
    Split => "cmd.split",
    Combine => "cmd.combine",
    ChecksumCreate => "cmd.checksum_create",
    ChecksumVerify => "cmd.checksum_verify",
    AddTime => "cmd.addtime",
    ReDate => "cmd.redate",
    Touch => "cmd.touch",
    Catalog => "cmd.catalog",
    Encrypt => "cmd.encrypt",
    Decrypt => "cmd.decrypt",
    DirSizes => "cmd.dir_size",
    Ftp => "cmd.ftp",
    Passwords => "cmd.passwords",
    NetConnect => "cmd.net_connect",
    NetDisconnect => "cmd.net_disconnect",
    BrowseFolder => "cmd.browse_folder",
    QuickView => "cmd.quick_view",
    ViewFull => "cmd.full",
    ViewBrief => "cmd.brief",
    ViewThumbs => "cmd.thumbnails",
    ToggleHidden => "cmd.hidden",
    Branch => "cmd.branch",
    QuickFilter => "cmd.quick_filter",
    Refresh => "cmd.refresh",
    Swap => "cmd.swap",
    SameDir => "cmd.same_dir",
    NewTab => "cmd.new_tab",
    CloseTab => "cmd.close_tab",
    NextTab => "cmd.next_tab",
    Hotlist => "cmd.hotlist",
    AddHotlist => "cmd.add_hotlist",
    History => "cmd.history",
    Back => "cmd.back",
    Forward => "cmd.forward",
    Parent => "cmd.parent",
    Root => "cmd.root",
    Home => "cmd.home",
    Terminal => "cmd.terminal",
    Settings => "cmd.settings",
    Log => "cmd.log",
    About => "cmd.about",
    CheckUpdate => "cmd.check_update",
    Keys => "cmd.keys",
    SortName => "cmd.sort_name",
    SortExt => "cmd.sort_ext",
    SortDate => "cmd.sort_date",
    SortSize => "cmd.sort_size",
    SwitchPanel => "cmd.switch_panel",
    DrivesLeft => "cmd.drives_left",
    DrivesRight => "cmd.drives_right",
}

impl Command {
    /// The command whose variant name is `name` (`"Keys"`, `"Sync"` …).
    pub fn from_name(name: &str) -> Option<Command> {
        Command::all()
            .iter()
            .copied()
            .find(|c| format!("{c:?}").eq_ignore_ascii_case(name.trim()))
    }

    /// Menu / help label.
    pub fn label(self) -> &'static str {
        tr(self.i18n_key())
    }
}

const NONE: Modifiers = Modifiers::NONE;
const CTRL: Modifiers = Modifiers::COMMAND;
const SHIFT: Modifiers = Modifiers::SHIFT;
const ALT: Modifiers = Modifiers::ALT;

const fn sc(m: Modifiers, k: Key) -> KeyboardShortcut {
    KeyboardShortcut::new(m, k)
}

fn ctrl_shift() -> Modifiers {
    CTRL | SHIFT
}

/// Every binding, most specific (most modifiers) first so it is consumed first.
pub fn bindings() -> Vec<(KeyboardShortcut, Command)> {
    use Command::*;
    let mut v = vec![
        (sc(ctrl_shift(), Key::F1), ViewThumbs),
        (sc(SHIFT, Key::F4), EditNew),
        (sc(SHIFT, Key::F6), RenameInPlace),
        (sc(SHIFT, Key::F8), DeletePermanent),
        (sc(SHIFT, Key::Delete), DeletePermanent),
        (sc(SHIFT, Key::F2), ComparePanels),
        (sc(ALT, Key::F4), Quit),
        (sc(ALT, Key::F5), Pack),
        (sc(ALT, Key::F9), Unpack),
        (sc(ALT, Key::F7), Search),
        (sc(ALT, Key::F1), DrivesLeft),
        (sc(ALT, Key::F2), DrivesRight),
        (sc(ALT, Key::Enter), Properties),
        (sc(ALT, Key::ArrowLeft), Back),
        (sc(ALT, Key::ArrowRight), Forward),
        (sc(ALT, Key::ArrowDown), History),
        (sc(CTRL, Key::F1), ViewBrief),
        (sc(CTRL, Key::F2), ViewFull),
        (sc(CTRL, Key::F3), SortName),
        (sc(CTRL, Key::F4), SortExt),
        (sc(CTRL, Key::F5), SortDate),
        (sc(CTRL, Key::F6), SortSize),
        (sc(CTRL, Key::A), SelectAll),
        (sc(CTRL, Key::B), Branch),
        (sc(CTRL, Key::D), Hotlist),
        (sc(CTRL, Key::H), ToggleHidden),
        (sc(CTRL, Key::L), DirSizes),
        (sc(CTRL, Key::M), MultiRename),
        (sc(CTRL, Key::N), Ftp),
        (sc(CTRL, Key::Q), QuickView),
        (sc(CTRL, Key::R), Refresh),
        (sc(CTRL, Key::S), QuickFilter),
        (sc(CTRL, Key::T), NewTab),
        (sc(CTRL, Key::U), Swap),
        (sc(CTRL, Key::W), CloseTab),
        (sc(CTRL, Key::Tab), NextTab),
        (sc(CTRL, Key::Backslash), Root),
        (sc(CTRL, Key::PageUp), Parent),
        (sc(CTRL, Key::Plus), SelectSameExt),
        (sc(CTRL, Key::Equals), SelectSameExt),
        (sc(CTRL, Key::Minus), UnselectAll),
        (sc(NONE, Key::F1), Keys),
        (sc(NONE, Key::F2), RenameInPlace),
        (sc(NONE, Key::F3), View),
        (sc(NONE, Key::F4), Edit),
        (sc(NONE, Key::F5), Copy),
        (sc(NONE, Key::F6), Move),
        (sc(NONE, Key::F7), MkDir),
        (sc(NONE, Key::F8), Delete),
        (sc(NONE, Key::Delete), Delete),
        (sc(NONE, Key::F9), Settings),
        (sc(NONE, Key::Plus), SelectGroup),
        (sc(NONE, Key::Minus), UnselectGroup),
        (sc(NONE, Key::Tab), SwitchPanel),
        (sc(NONE, Key::Backspace), Parent),
    ];
    v.sort_by_key(|(s, _)| std::cmp::Reverse(modifier_count(s.modifiers)));
    v
}

fn modifier_count(m: Modifiers) -> u8 {
    u8::from(m.ctrl || m.command || m.mac_cmd) + u8::from(m.shift) + u8::from(m.alt)
}

/// Shortcut text for a command, for menus ("Ctrl+M").
pub fn shortcut_text(ctx: &egui::Context, cmd: Command) -> String {
    bindings()
        .into_iter()
        .find(|(_, c)| *c == cmd)
        .map(|(s, _)| ctx.format_shortcut(&s))
        .unwrap_or_default()
}

/// Consumes the first matching shortcut of this frame.
pub fn pressed_command(ctx: &egui::Context) -> Option<Command> {
    let bindings = bindings();
    ctx.input_mut(|i| {
        bindings
            .iter()
            .find(|(s, _)| i.consume_shortcut(s))
            .map(|(_, c)| *c)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn commands_resolve_by_name() {
        assert_eq!(Command::from_name("sync"), Some(Command::Sync));
        assert_eq!(Command::from_name("Nope"), None);
        let mut seen = std::collections::HashSet::new();
        assert!(Command::all().iter().all(|c| seen.insert(format!("{c:?}"))));
    }

    #[test]
    fn every_command_has_its_own_translated_label() {
        let mut seen = std::collections::HashMap::new();
        for c in Command::all() {
            let key = c.i18n_key();
            assert_ne!(tr(key), key, "{c:?}: no translation for {key}");
            if let Some(other) = seen.insert(key, *c) {
                panic!("{c:?} and {other:?} share the label {key}");
            }
        }
    }

    #[test]
    fn no_shortcut_bound_twice() {
        let b = bindings();
        for (i, (s, _)) in b.iter().enumerate() {
            assert!(
                !b[i + 1..].iter().any(|(o, _)| o == s),
                "duplicate shortcut {s:?}"
            );
        }
    }

    #[test]
    fn modified_shortcuts_come_first() {
        let b = bindings();
        let first_plain = b.iter().position(|(s, _)| s.modifiers == NONE).unwrap();
        assert!(b[first_plain..].iter().all(|(s, _)| s.modifiers == NONE));
    }
}
