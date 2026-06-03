# Plan Multijoueur Reseau FTLM

## Resume

Mettre en place un multijoueur en ligne hote-joueur avec simulation autoritaire cote hote. Les clients envoient uniquement leurs entrees ; l'hote simule les barres, balles, briques, scores, bonus et envoie l'etat a afficher. Les clients n'executent aucune physique ni logique de jeu : ils affichent et interpolent ce que l'hote envoie. L'IA reste optionnelle pour remplir des slots ou conserver les tests automatiques.

## Interfaces Et API

- Ajouter un autoload `NetworkSession` responsable de l'etat reseau : `Offline`, `Host`, `Client`, port par defaut `42424`, slots joueurs, peer IDs, connexion/deconnexion, start lobby.
- Utiliser `ENetMultiplayerPeer` avec `CreateServer(port, 3)` cote hote et `CreateClient(ip, port)` cote client.
- Ajouter une tentative UPnP en thread separe au lancement hote (`UPNP.Discover()` peut bloquer plusieurs secondes) ; en cas d'echec, afficher un fallback clair : "ouvrez le port UDP 42424".
- Etendre `PartieConfig` :
  - `ModePartie { Local, Reseau }`
  - `TypeControle { HumainLocal, HumainDistant, IA }` (l'enum actuel `{ Humain, IA }` doit etre etendu sans casser le defaut J1-humain/reste-IA attendu par `--jeu` / `--test`).
  - `PeerControleurDe(slot)`
  - Passer les couples d'actions actuels `(Gauche, Droite)` a des triplets `(Gauche, Droite, Action)`. **Impact mode local** : `_actions` et `_toucheParDefaut` n'ont aujourd'hui aucune action de lancement pour J2-J4 ; il faut ajouter `j2_action`/`j3_action`/`j4_action` avec des touches par defaut, et adapter tout code qui lit `ActionsDe(index)` comme un couple.
- Remplacer le lancement global `lancer_balle` par une action dediee par slot :
  - J1 : `ui_left`, `ui_right`, `lancer_balle`
  - J2 : `j2_gauche`, `j2_droite`, `j2_action`
  - J3 : `j3_gauche`, `j3_droite`, `j3_action`
  - J4 : `j4_gauche`, `j4_droite`, `j4_action`

## Changements Cles

- Menu :
  - Conserver le mode local existant.
  - Ajouter "Heberger" et "Rejoindre".
  - Le lobby hote choisit 2 a 4 slots ; chaque slot actif est `Humain` connecte ou `IA`.
  - Le bouton demarrer est reserve a l'hote et actif seulement si chaque slot actif a un controleur.
- Gameplay :
  - Hors reseau, comportement actuel inchange.
  - En reseau, seul l'hote execute la logique de jeu complete dans `GameManager` (briques, balles, bonus, score, IA).
  - Les clients n'appellent aucune mutation de gameplay ; ils envoient leurs inputs et appliquent les snapshots recus.
  - `BarScript` recoit un mode de controle : input local, input reseau, IA, ou spectateur.
  - **`BalleScript` recoit aussi un mode de controle.** Les balles sont des `RigidBody3D` (Jolt) : cote client il ne faut PAS re-simuler la physique (divergence garantie). En mode client la balle devient un objet purement affiche : `_PhysicsProcess` (renormalisation de la vitesse), `Lancer`, `RebondSurBarre`, `CollerA` et toutes les mutations ne tournent que chez l'hote ; le client recoit position/vitesse et interpole. Couper la physique (freeze / mode kinematic) cote client.

- Generation de l'arene cote client :
  - L'arene est generee au runtime (`ConstruireMurs`, `AppliquerGeometrie`, `PlacerCamera`, bras pivotes) a partir de `PartieConfig`.
  - La generation geometrique est deterministe : le message de start envoie `NombreJoueurs` + types de controle par slot, et le client appelle la meme generation pour obtenir des murs/bras/camera identiques. Aucune position de mur n'a besoin d'etre repliquee.

- Synchronisation :
  - Client vers hote : axe gauche/droite en `UnreliableOrdered`, action lancement/laser en `Reliable`.
  - Hote vers clients, **deux flux distincts** (ne pas tout mettre dans un seul snapshot) :
    - Snapshots haute frequence (positions barres + balles), cadence fixe ~20-30 Hz, en `UnreliableOrdered`.
    - Evenements et etat rare (score, vies, niveau, message HUD) en `Reliable`, envoyes uniquement au changement.
  - Cycle de vie des objets (spawn/despawn) en `Reliable` avec IDs reseau stables :
    - brique detruite, capsule apparue/ramassee, balle supplementaire (multi-balle) creee/perdue.
    - **Etat initial a la connexion** : a l'arrivee d'un client (ou au demarrage de la partie), l'hote envoie l'etat complet (toutes les briques presentes et leur vie restante, capsules en vol, balles), sinon un client qui rejoint voit une arene vide.
  - Verifier cote hote que le peer qui envoie une entree controle bien le slot concerne.
- Fin de partie et pause :
  - L'hote controle pause, nouvelle partie, retour lobby et relance.
  - Les clients affichent les messages recus et reviennent au menu si le serveur se deconnecte.

## Tests

- Garder les smoke tests IA existants pour 2/3/4 joueurs (mono-process, inchanges).
- Ajouter un test deux instances. **Realiste : ce sera un test manuel scripte**, pas un test auto au meme titre que les smoke tests, car il faut lancer deux process Godot, les synchroniser et lire deux logs (`godot-mcp run_project` ne passe pas d'args CLI). Lancer via l'exe console avec args dedies (ex. `--host` / `--join 127.0.0.1`). Verifier :
  - hote demarre sur `127.0.0.1:42424` ;
  - client rejoint ;
  - slot client assigne ;
  - input client deplace uniquement sa barre ;
  - action client lance uniquement sa balle ;
  - hote detruit une brique et le client recoit l'etat ;
  - un client qui rejoint apres le start recoit l'etat initial complet (briques + balles).
- Tester manuellement :
  - partie locale inchangee (et `--jeu` / `--test` toujours fonctionnels) ;
  - hote + client LAN ;
  - hote + client Internet avec UPnP reussi ;
  - UPnP echoue avec message de fallback port UDP ;
  - slot IA active dans un lobby reseau ;
  - deconnexion client en lobby et en partie.

## Hypotheses

- MVP Internet public = IP/port direct avec tentative UPnP, pas de matchmaking ni relais.
- Pas de prediction client pour la premiere version ; priorite a la coherence autoritaire. Latence sur le mouvement de barre toleree.
- Pas de gestion fine de la desync pour le MVP : un client qui lag ou perd des paquets prolonge se contente de l'interpolation ; au-dela d'un timeout de connexion ENet, retour au menu.
- Port par defaut : UDP `42424`, configurable dans le lobby.
- References :
  - [Godot high-level multiplayer](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)
  - [Godot MultiplayerAPI 4.6](https://docs.godotengine.org/en/4.6/classes/class_multiplayerapi.html)
  - [Godot UPNP](https://docs.godotengine.org/en/stable/classes/class_upnp.html)
