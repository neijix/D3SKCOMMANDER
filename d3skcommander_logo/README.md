# D3SKCOMMANDER — kit de marque

Famille D3SK (th3t). Gestionnaire de fichiers à double panneau, piloté au clavier.

## Logo
Wordmark `D3SKCOMMANDER` en Archivo 800, capitales, tracking −5 %, **en courbes** (aucune police requise).
Symbole `/▮▯` : le slash −16° de la famille + deux panneaux inclinés au même angle — panneau actif plein, panneau cible en contour. Arrondi 3 u.

## Fichiers
- `d3skcommander-couleur-fond-sombre.svg` / `-fond-clair.svg` / `-noir.svg` / `-blanc.svg` — logo complet (calques `wordmark`, `symbole`)
- `d3skcommander-icone.svg` — icône app (pastille anthracite, slash blanc, panneaux ambre)
- `d3skcommander-icone-clair.svg` — variante fond clair
- `d3skcommander-icone-petit.svg` — sous 48 px : deux panneaux seuls, sans slash
- `symbole-seul.svg` — symbole sans wordmark
- `png/` — logos 800 / 2400 px, icônes 16 → 1024 px

## Quel fichier utiliser
- **Illustrator / Figma / print** → les `.svg`
- **Web, doc, présentation** → `png/d3skcommander-*-800px.png` (2400 px pour Retina)
- **Dock macOS / app Windows** → `png/icone-1024px.png`
- **Favicon / barre des tâches** → `png/icone-16px.png` / `icone-32px.png`

## Dans l’app
| Endroit | Source |
|---|---|
| Icône de la fenêtre (`src/ui/mod.rs`) | `packaging/icons/256x256.png` = `png/icone-1024px.png` réduite |
| Installeurs (`Cargo.toml`, `[package.metadata.packager]`) : PNG Linux, `icon.icns` macOS, `icon.ico` Windows et icône du setup NSIS | `packaging/icons/`, produits par `python3 packaging/make_icon.py` : `png/icone-16px.png` / `-32px` (icône simplifiée) sous 48 px, l’icône complète au-dessus |

Nouveau kit → remplacer les fichiers de ce dossier, relancer `packaging/make_icon.py`, commiter `packaging/icons/`.

## Couleurs
- Ambre `#FFB020` — accent, symbole, panneau actif
- Ambre foncé `#B86E00` — accent sur fond clair
- Anthracite `#2A2B30` — pastille, icône
- Charbon `#121216` — fond principal
- Blanc cassé `#F4F4F7` — wordmark sur sombre
- Noir `#111318` — print

## Typographie
Archivo 800 (wordmark) · Space Grotesk (interface) · JetBrains Mono (chemins, raccourcis F1–F10).

## Règles
- Le panneau plein est toujours à gauche (actif), le contour à droite (cible).
- Sous 48 px, utiliser l'icône simplifiée.
