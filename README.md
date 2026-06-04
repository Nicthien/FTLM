# Free The Last Marble (FTLM)

> 🇫🇷 Version française par défaut · 🇬🇧 [English version below](#-english-version)

**FTLM** signifie **Free The Last Marble**.

L'objectif pur d'un casse-briques : faire rebondir cette foutue bille jusqu'à détruire le tout dernier bloc.

Jeu de **casse-brique** futuriste **multijoueur** (local & réseau) en 3D, construit avec **Godot 4.6.3 (.NET / Mono)** en **C#**.

---

## 🎮 Fonctionnalités

- **Multijoueur local 2/3/4 joueurs** sur une arène générée à la volée (couloirs radiaux autour d'un hub central).
- **Multijoueur réseau** avec hôte autoritaire (ENet, port 42424), lobby avec système **Prêt** et synchronisation des snapshots à ~30 Hz.
- **Serveur dédié headless** (`--serveur`) sans joueur local, piloté par commandes stdin, avec remplissage **IA** des emplacements vides.
- Adversaires **IA** par emplacement, **briques multi-coups**, **capsules** bonus / malus, **multi-balle**, laser / capacité active.
- **Score / vies / meilleur score** persistant (`user://meilleur_score.dat`).
- **Effets sonores** procéduraux et **particules**.
- Menu, sélection des joueurs, **pause** et **options** (touches remappables par joueur).

## 🕹️ Contrôles

| Action | Joueur 1 | Joueur 2 | Joueur 3 | Joueur 4 |
| --- | --- | --- | --- | --- |
| Déplacer | ◀ / ▶ (+ souris) | A / D | J / L | Pavé num. 4 / 6 |
| Lancer la balle | Espace / clic gauche | W | I | Pavé num. 5 |
| Capacité / laser | Alt / clic droit | S | K | Pavé num. 8 |
| Pause | Échap | — | — | — |

> Toutes les touches sont remappables dans **Options ▸ Touches**.

## 🛠️ Stack technique

- **Moteur :** Godot **4.6.3** (Mono).
- **Langage :** C# (`net8.0` desktop, `net9.0` Android).
- **Physique :** **Jolt**.
- **Rendu :** Forward+, Direct3D 12 sous Windows.
- **Scène principale :** `res://MainMenu.tscn`.

## 🚀 Démarrer le projet (développement)

L'exécutable Godot n'est **pas** dans le PATH — utilise le chemin complet (variante `_console` pour le travail en ligne de commande) :

```powershell
# Compiler le C#
dotnet build

# Lancer le jeu
& "C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe" --path .

# Ouvrir l'éditeur
& "C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe" --path . --editor
```

### Modes de lancement utiles

| Argument | Effet |
| --- | --- |
| `-- --test` | Auto-test : joue une partie complète et affiche `[TEST] …`, puis quitte. |
| `--serveur [--port <n>] [--joueurs <n>]` | Serveur dédié headless (port 42424, 2 joueurs par défaut). |
| `-- --nethost` / `-- --netjoin` | Test réseau deux instances (hôte + client). |
| `--headless --import` | Réimporte les ressources (après ajout de fichiers `Audio/`, etc.). |

## 📥 Télécharger / Jouer

Les builds Windows sont publiés dans la section **[Releases](../../releases)**.

> ℹ️ Un build Windows nécessite **tous** les fichiers du dossier `Build/` ensemble
> (`FTLM.exe` + `FTLM.pck` + `data_FTLM_windows_x86_64`) : l'exe seul ne se lance pas.
> Décompresse l'archive complète puis lance `FTLM.exe`.

## 📄 Licence

Licence **propriétaire** — « Tous droits réservés ». Le code source et les ressources
(graphismes, sons, scènes, niveaux) sont protégés. Voir [LICENSE](LICENSE).

---

## 🇬🇧 English version

**FTLM** stands for **Free The Last Marble**.

The pure goal of a brick-breaker: keep that darn marble bouncing until the very last
block is destroyed.

A futuristic **brick-breaker** (Breakout-style) **multiplayer** 3D game, built with
**Godot 4.6.3 (.NET / Mono)** in **C#**.

### 🎮 Features

- **Local multiplayer for 2/3/4 players** on an arena generated at runtime (radial
  corridors around a central hub).
- **Network multiplayer** with an authoritative host (ENet, port 42424), a lobby with a
  **Ready** system and ~30 Hz snapshot synchronisation.
- **Headless dedicated server** (`--serveur`) with no local player, driven by stdin
  commands, filling empty slots with **AI**.
- Per-slot **AI** opponents, **multi-hit bricks**, bonus / malus **capsules**,
  **multi-ball**, laser / active ability.
- Persistent **score / lives / best score** (`user://meilleur_score.dat`).
- Procedural **sound effects** and **particles**.
- Menu, player selection, **pause** and **options** (per-player remappable keys).

### 🕹️ Controls

| Action | Player 1 | Player 2 | Player 3 | Player 4 |
| --- | --- | --- | --- | --- |
| Move | ◀ / ▶ (+ mouse) | A / D | J / L | Numpad 4 / 6 |
| Launch ball | Space / left click | W | I | Numpad 5 |
| Ability / laser | Alt / right click | S | K | Numpad 8 |
| Pause | Esc | — | — | — |

> All keys are remappable in **Options ▸ Keys**.

### 🛠️ Tech stack

- **Engine:** Godot **4.6.3** (Mono).
- **Language:** C# (`net8.0` desktop, `net9.0` Android).
- **Physics:** **Jolt**.
- **Rendering:** Forward+, Direct3D 12 on Windows.
- **Main scene:** `res://MainMenu.tscn`.

### 🚀 Getting started (development)

The Godot executable is **not** on the PATH — use the full path (the `_console`
variant for command-line work):

```powershell
# Build C#
dotnet build

# Run the game
& "C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe" --path .

# Open the editor
& "C:\DEV\Godot\Godot_v4.6.3-stable_mono_win64_console.exe" --path . --editor
```

#### Useful launch modes

| Argument | Effect |
| --- | --- |
| `-- --test` | Auto-test: plays a full game, prints `[TEST] …`, then quits. |
| `--serveur [--port <n>] [--joueurs <n>]` | Headless dedicated server (port 42424, 2 players by default). |
| `-- --nethost` / `-- --netjoin` | Two-instance network test (host + client). |
| `--headless --import` | Re-imports assets (after adding `Audio/` files, etc.). |

### 📥 Download / Play

Windows builds are published in the **[Releases](../../releases)** section.

> ℹ️ A Windows build needs **all** the files from the `Build/` folder together
> (`FTLM.exe` + `FTLM.pck` + `data_FTLM_windows_x86_64`): the exe alone won't run.
> Extract the full archive, then launch `FTLM.exe`.

### 📄 License

**Proprietary** license — "All rights reserved". The source code and assets
(graphics, sounds, scenes, levels) are protected. See [LICENSE](LICENSE).
</content>
</invoke>
