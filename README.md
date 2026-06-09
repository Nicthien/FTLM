# Free The Last Marble (FTLM)

**Français** · [English](README.en.md)

**FTLM** signifie **Free The Last Marble**.

L'objectif pur d'un casse-briques : faire rebondir cette foutue bille jusqu'à détruire le tout dernier bloc.

Jeu de **casse-brique** futuriste **multijoueur** (local & réseau) en 3D, construit avec **Godot 4.6.3 (.NET / Mono)** en **C#**.

[![Soutenir sur Ko-fi](https://img.shields.io/badge/Soutenir-Ko--fi-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/nthstudio)

Ce jeu est un projet personnel développé sur mon temps libre. S'il vous plaît et que vous souhaitez le soutenir, vous pouvez m'offrir un Ko-fi : [ko-fi.com/nthstudio](https://ko-fi.com/nthstudio). Merci !

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

Licence **MIT** — libre d'utilisation, de modification et de redistribution. Voir [LICENSE](LICENSE).
