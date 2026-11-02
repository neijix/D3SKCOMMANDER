//! User-interface strings in English and French.
//!
//! `tr("key")` returns the string for the current language; an unknown key returns the
//! key itself so a missing translation shows up instead of crashing.

use std::sync::atomic::{AtomicU8, Ordering};

use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default, Serialize, Deserialize)]
pub enum Lang {
    #[default]
    En,
    Fr,
}

impl Lang {
    pub fn all() -> &'static [Lang] {
        &[Lang::En, Lang::Fr]
    }

    pub fn label(self) -> &'static str {
        match self {
            Lang::En => "English",
            Lang::Fr => "Français",
        }
    }

    /// Picks French when the system locale says so.
    pub fn from_env() -> Lang {
        let vars = ["LC_ALL", "LC_MESSAGES", "LANG"];
        let fr = vars
            .iter()
            .filter_map(|v| std::env::var(v).ok())
            .any(|v| v.to_lowercase().starts_with("fr"));
        if fr { Lang::Fr } else { Lang::En }
    }
}

static CURRENT: AtomicU8 = AtomicU8::new(0);

pub fn set_lang(lang: Lang) {
    CURRENT.store(lang as u8, Ordering::Relaxed);
}

pub fn lang() -> Lang {
    match CURRENT.load(Ordering::Relaxed) {
        1 => Lang::Fr,
        _ => Lang::En,
    }
}

/// Inline bilingual text, for UI strings that have no table key.
pub fn t(en: &'static str, fr: &'static str) -> &'static str {
    if lang() == Lang::Fr { fr } else { en }
}

/// Translates `key`.
pub fn tr(key: &'static str) -> &'static str {
    let fr = lang() == Lang::Fr;
    STRINGS
        .iter()
        .find(|(k, _, _)| *k == key)
        .map(|(_, en, f)| if fr { *f } else { *en })
        .unwrap_or(key)
}

/// (key, English, French)
const STRINGS: &[(&str, &str, &str)] = &[
    // Menus
    ("menu.files", "Files", "Fichiers"),
    ("menu.mark", "Mark", "Sélection"),
    ("menu.commands", "Commands", "Commandes"),
    ("menu.net", "Net", "Réseau"),
    ("menu.show", "Show", "Affichage"),
    ("menu.tools", "Tools", "Outils"),
    ("menu.config", "Configuration", "Configuration"),
    ("menu.help", "Help", "Aide"),
    // Function keys
    ("fk.view", "View", "Voir"),
    ("fk.edit", "Edit", "Éditer"),
    ("fk.copy", "Copy", "Copier"),
    ("fk.move", "Move", "Déplacer"),
    ("fk.mkdir", "NewFolder", "NouvDossier"),
    ("fk.delete", "Delete", "Supprimer"),
    ("fk.quit", "Exit", "Quitter"),
    ("fk.rename", "Rename", "Renommer"),
    ("fk.search", "Search", "Chercher"),
    // Commands
    ("cmd.view", "View file", "Voir le fichier"),
    ("cmd.edit", "Edit file", "Éditer le fichier"),
    ("cmd.edit_new", "Edit new file", "Nouveau fichier"),
    ("cmd.copy", "Copy", "Copier"),
    ("cmd.move", "Rename / Move", "Renommer / Déplacer"),
    ("cmd.rename", "Rename in place", "Renommer sur place"),
    ("cmd.mkdir", "New folder", "Nouveau dossier"),
    ("cmd.delete", "Delete", "Supprimer"),
    (
        "cmd.delete_perm",
        "Delete permanently",
        "Supprimer définitivement",
    ),
    ("cmd.pack", "Pack files…", "Compresser…"),
    ("cmd.unpack", "Unpack files…", "Décompresser…"),
    ("cmd.backup", "Backup to ZIP", "Sauvegarde ZIP"),
    ("cmd.properties", "Properties", "Propriétés"),
    ("cmd.open", "Open", "Ouvrir"),
    (
        "cmd.open_with",
        "Open selected with…",
        "Ouvrir la sélection avec…",
    ),
    ("cmd.quit", "Quit", "Quitter"),
    (
        "cmd.select_group",
        "Select group…",
        "Sélectionner un groupe…",
    ),
    (
        "cmd.unselect_group",
        "Unselect group…",
        "Désélectionner un groupe…",
    ),
    ("cmd.select_all", "Select all", "Tout sélectionner"),
    ("cmd.unselect_all", "Unselect all", "Tout désélectionner"),
    ("cmd.invert", "Invert selection", "Inverser la sélection"),
    (
        "cmd.select_same_ext",
        "Select same extension",
        "Même extension",
    ),
    (
        "cmd.copy_names",
        "Copy names to clipboard",
        "Copier les noms",
    ),
    (
        "cmd.copy_paths",
        "Copy full paths to clipboard",
        "Copier les chemins",
    ),
    (
        "cmd.compare_dirs",
        "Compare panels (mark differences)",
        "Comparer les panneaux",
    ),
    ("cmd.search", "Search files", "Rechercher"),
    ("cmd.multirename", "Multi-rename tool", "Renommage multiple"),
    (
        "cmd.compare_files",
        "Compare files (editor)",
        "Comparer les fichiers",
    ),
    (
        "cmd.sync",
        "Synchronize directories",
        "Synchroniser les dossiers",
    ),
    ("cmd.split", "Split file…", "Découper un fichier…"),
    (
        "cmd.combine",
        "Combine files (CRC check)",
        "Recombiner (contrôle CRC)",
    ),
    (
        "cmd.checksum_create",
        "Create checksum file…",
        "Créer des sommes de contrôle…",
    ),
    (
        "cmd.checksum_verify",
        "Verify checksums",
        "Vérifier les sommes",
    ),
    (
        "cmd.addtime",
        "Shift file dates (AddTime)…",
        "Décaler les dates (AddTime)…",
    ),
    (
        "cmd.redate",
        "Re-date incrementally (ReDate)…",
        "Redater par incréments (ReDate)…",
    ),
    ("cmd.touch", "Change date/time…", "Changer date/heure…"),
    ("cmd.catalog", "Disk catalog…", "Catalogue de disque…"),
    ("cmd.encrypt", "Encrypt files…", "Chiffrer…"),
    ("cmd.decrypt", "Decrypt files…", "Déchiffrer…"),
    (
        "cmd.dir_size",
        "Calculate folder sizes",
        "Taille des dossiers",
    ),
    ("cmd.ftp", "FTP connect…", "Connexion FTP…"),
    (
        "cmd.net_connect",
        "Connect network drive…",
        "Connecter un lecteur réseau…",
    ),
    (
        "cmd.net_disconnect",
        "Disconnect network drive",
        "Déconnecter le lecteur réseau",
    ),
    (
        "cmd.browse_folder",
        "Choose a folder…",
        "Choisir un dossier…",
    ),
    (
        "cmd.passwords",
        "Password manager",
        "Gestionnaire de mots de passe",
    ),
    ("cmd.quick_view", "Quick view panel", "Panneau d'aperçu"),
    ("cmd.thumbnails", "Thumbnails", "Vignettes"),
    ("cmd.full", "Full (details)", "Détaillé"),
    ("cmd.brief", "Brief", "Bref"),
    ("cmd.hidden", "Show hidden files", "Fichiers cachés"),
    (
        "cmd.branch",
        "Branch view (all files)",
        "Vue arborescente à plat",
    ),
    ("cmd.quick_filter", "Quick filter", "Filtre rapide"),
    ("cmd.refresh", "Refresh", "Actualiser"),
    ("cmd.swap", "Swap panels", "Échanger les panneaux"),
    ("cmd.same_dir", "Target = source", "Cible = source"),
    ("cmd.new_tab", "New tab", "Nouvel onglet"),
    ("cmd.close_tab", "Close tab", "Fermer l'onglet"),
    ("cmd.next_tab", "Next tab", "Onglet suivant"),
    ("cmd.switch_panel", "Switch panel", "Changer de panneau"),
    (
        "cmd.drives_left",
        "Drive / folder (left)",
        "Lecteur / dossier (gauche)",
    ),
    (
        "cmd.drives_right",
        "Drive / folder (right)",
        "Lecteur / dossier (droite)",
    ),
    ("cmd.sort_name", "Sort by name", "Trier par nom"),
    ("cmd.sort_ext", "Sort by extension", "Trier par extension"),
    ("cmd.sort_date", "Sort by date", "Trier par date"),
    ("cmd.sort_size", "Sort by size", "Trier par taille"),
    ("cmd.hotlist", "Directory hotlist", "Dossiers favoris"),
    (
        "cmd.add_hotlist",
        "Add current folder",
        "Ajouter le dossier courant",
    ),
    ("cmd.history", "Folder history", "Historique"),
    ("cmd.back", "Back", "Précédent"),
    ("cmd.forward", "Forward", "Suivant"),
    ("cmd.parent", "Parent folder", "Dossier parent"),
    ("cmd.root", "Root", "Racine"),
    ("cmd.home", "Home", "Dossier personnel"),
    ("cmd.terminal", "Open terminal here", "Terminal ici"),
    ("cmd.settings", "Settings…", "Options…"),
    (
        "cmd.settings_folder",
        "Settings folder…",
        "Dossier des réglages…",
    ),
    ("cmd.log", "Operation log", "Journal des opérations"),
    ("cmd.about", "About", "À propos"),
    (
        "cmd.check_update",
        "Check for updates…",
        "Rechercher des mises à jour…",
    ),
    (
        "check_updates",
        "Look for a new version at start-up",
        "Chercher une nouvelle version au démarrage",
    ),
    ("cmd.keys", "Keyboard shortcuts", "Raccourcis clavier"),
    // Common words
    ("ok", "OK", "OK"),
    ("cancel", "Cancel", "Annuler"),
    ("close", "Close", "Fermer"),
    ("apply", "Apply", "Appliquer"),
    ("start", "Start", "Démarrer"),
    ("stop", "Stop", "Arrêter"),
    ("save", "Save", "Enregistrer"),
    ("yes", "Yes", "Oui"),
    ("no", "No", "Non"),
    ("name", "Name", "Nom"),
    ("ext", "Ext", "Ext"),
    ("size", "Size", "Taille"),
    ("date", "Date", "Date"),
    ("attr", "Attr", "Attr"),
    ("path", "Path", "Chemin"),
    ("folder", "Folder", "Dossier"),
    ("files", "files", "fichiers"),
    ("folders", "folders", "dossiers"),
    ("selected", "selected", "sélectionnés"),
    ("free", "free", "libres"),
    ("of", "of", "sur"),
    ("error", "Error", "Erreur"),
    ("done", "Done", "Terminé"),
    ("password", "Password", "Mot de passe"),
    (
        "confirm_password",
        "Confirm password",
        "Confirmer le mot de passe",
    ),
    ("master_password", "Master password", "Mot de passe maître"),
    ("mask", "Mask", "Masque"),
    ("target", "Target", "Destination"),
    ("command", "Command", "Commande"),
    ("language", "Language", "Langue"),
    ("dark_mode", "Dark mode", "Mode sombre"),
    ("font_size", "Font size", "Taille du texte"),
    (
        "use_trash",
        "Delete to trash",
        "Supprimer vers la corbeille",
    ),
    (
        "confirm_delete",
        "Confirm deletion",
        "Confirmer la suppression",
    ),
    (
        "log_ops",
        "Log file operations",
        "Journaliser les opérations",
    ),
    (
        "editor",
        "External editor (empty = built-in)",
        "Éditeur externe (vide = intégré)",
    ),
    ("terminal", "Terminal command", "Commande du terminal"),
    ("columns", "Columns", "Colonnes"),
    ("filter", "Filter", "Filtre"),
    ("results", "Results", "Résultats"),
    ("progress", "Progress", "Progression"),
    ("copying", "Copying", "Copie"),
    ("moving", "Moving", "Déplacement"),
    ("deleting", "Deleting", "Suppression"),
    ("working", "Working…", "En cours…"),
    // Dialog texts
    (
        "dlg.copy_to",
        "Copy {n} item(s) to:",
        "Copier {n} élément(s) vers :",
    ),
    (
        "dlg.move_to",
        "Move / rename {n} item(s) to:",
        "Déplacer / renommer {n} élément(s) vers :",
    ),
    (
        "dlg.delete",
        "Delete {n} item(s)?",
        "Supprimer {n} élément(s) ?",
    ),
    (
        "dlg.delete_perm",
        "Permanently delete {n} item(s)? This cannot be undone.",
        "Supprimer définitivement {n} élément(s) ? Irréversible.",
    ),
    ("dlg.mkdir", "New folder name:", "Nom du nouveau dossier :"),
    ("dlg.newfile", "New file name:", "Nom du nouveau fichier :"),
    ("dlg.rename", "New name:", "Nouveau nom :"),
    (
        "dlg.select",
        "Select files matching:",
        "Sélectionner les fichiers :",
    ),
    (
        "dlg.unselect",
        "Unselect files matching:",
        "Désélectionner les fichiers :",
    ),
    (
        "dlg.overwrite",
        "The target file already exists",
        "Le fichier cible existe déjà",
    ),
    ("dlg.source", "Source", "Source"),
    ("dlg.existing", "Existing", "Existant"),
    ("dlg.overwrite_btn", "Overwrite", "Écraser"),
    ("dlg.overwrite_all", "Overwrite all", "Tout écraser"),
    (
        "dlg.overwrite_older",
        "Overwrite all older",
        "Écraser les plus anciens",
    ),
    ("dlg.skip", "Skip", "Ignorer"),
    ("dlg.skip_all", "Skip all", "Tout ignorer"),
    ("dlg.rename_new", "Rename new", "Renommer la copie"),
    (
        "dlg.pack",
        "Pack {n} item(s) into:",
        "Compresser {n} élément(s) dans :",
    ),
    ("dlg.unpack", "Unpack to:", "Décompresser vers :"),
    ("dlg.format", "Format", "Format"),
    ("dlg.level", "Compression level", "Niveau de compression"),
    (
        "dlg.part_size",
        "Part size (e.g. 700M, 1.44M, 4G)",
        "Taille des parties (ex. 700M, 1.44M, 4G)",
    ),
    (
        "dlg.offset",
        "Offset (e.g. +1h30m, -2d, -0:05)",
        "Décalage (ex. +1h30m, -2d, -0:05)",
    ),
    (
        "dlg.start_date",
        "First date (YYYY-MM-DD HH:MM:SS)",
        "Première date (AAAA-MM-JJ HH:MM:SS)",
    ),
    ("dlg.step", "Step (e.g. 1m, 1h, 1d)", "Pas (ex. 1m, 1h, 1d)"),
    ("dlg.modified", "Modification time", "Date de modification"),
    ("dlg.accessed", "Access time", "Date d'accès"),
    ("dlg.algo", "Algorithm", "Algorithme"),
    ("dlg.once_per_file", "Once per file", "Une fois par fichier"),
    (
        "dlg.all_at_once",
        "All files at once",
        "Tous les fichiers d'un coup",
    ),
    ("dlg.out_file", "Output file", "Fichier de sortie"),
    (
        "dlg.no_selection",
        "Nothing selected.",
        "Rien n'est sélectionné.",
    ),
    (
        "dlg.need_two_files",
        "Select two files (or one in each panel).",
        "Sélectionnez deux fichiers (ou un dans chaque panneau).",
    ),
    // Status
    ("st.ready", "Ready", "Prêt"),
    ("st.archive", "Archive", "Archive"),
    ("st.filtered", "filter", "filtre"),
];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keys_are_unique_and_translated() {
        let mut keys: Vec<&str> = STRINGS.iter().map(|(k, _, _)| *k).collect();
        let len = keys.len();
        keys.sort_unstable();
        keys.dedup();
        assert_eq!(keys.len(), len, "duplicate i18n key");
        assert!(
            STRINGS
                .iter()
                .all(|(_, en, fr)| !en.is_empty() && !fr.is_empty())
        );
    }

    #[test]
    fn command_labels_read_differently_in_each_language() {
        let cmds: Vec<_> = STRINGS
            .iter()
            .filter(|(k, _, _)| k.starts_with("cmd."))
            .collect();
        for (i, (k, en, fr)) in cmds.iter().enumerate() {
            for (other, en2, fr2) in &cmds[i + 1..] {
                assert!(en != en2 && fr != fr2, "{k} and {other} share a label");
            }
        }
    }

    #[test]
    fn switches_language() {
        set_lang(Lang::Fr);
        assert_eq!(tr("cancel"), "Annuler");
        set_lang(Lang::En);
        assert_eq!(tr("cancel"), "Cancel");
        assert_eq!(tr("no.such.key"), "no.such.key");
    }
}
