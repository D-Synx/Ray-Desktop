# RayXDesktop

**Version maintenue par D.Synx.** Projet Ray Desktop d’origine par [raydoomed](https://github.com/raydoomed/Ray-Desktop), sous licence MIT.

## Démarrer

1. Télécharge **RayXDesktop-Windows.zip** dans la rubrique **Assets** de la [version 1.4.6](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.4.6) et extrais son contenu dans un dossier.
2. Garde **RayXDesktop.bat**, **RayXDesktop.exe**, **EnableChildSessions.exe** et les deux DLL dans ce dossier.
3. Dans ta copie locale du BAT, remplace **PASSW_WORD!** par le mot de passe et **.\Admin** par le compte autorisé si nécessaire. Le préfixe **.\** désigne un compte local du PC. Garde les guillemets autour des arguments. Si le mot de passe contient **%**, écris-le **%%** dans le BAT.
4. Double-clique sur **RayXDesktop.bat**. Pour utiliser le compte Windows déjà connecté, ouvre directement **RayXDesktop.exe**.

Le BAT trouve l’EXE à côté de lui, même si la lettre de la clé USB change. Le mot de passe est écrit en clair dans le BAT personnalisé. Garde cette copie hors du dépôt et ne la partage pas.

Windows doit prendre en charge les sessions enfants. Si la fonctionnalité est désactivée, un administrateur autorisé peut lancer **EnableChildSessions.exe --enable-child-sessions**. Une déconnexion de Windows suivie d’une reconnexion peut être nécessaire après la première activation.

L’interface utilise le français sur les systèmes en français et l’anglais ailleurs. Les arguments **--lang=fr** et **--lang=en** forcent la langue. **--dynamic** utilise la taille de la fenêtre pour l’affichage.

## Options de connexion existantes

- **Bureau local standard.** Lance **RayXDesktop.exe**.
- **Compte explicite pour le bureau local.** Le raccourci existant peut utiliser **--account=DOMAINE\Utilisateur --password:MOT_DE_PASSE**. Le contrôle RDP transmet les identifiants à Windows, qui décide si la connexion est autorisée.
- **Autre poste de maintenance.** **--maintenance-host=maintenance.example.org --admin-account=IT\maint** ouvre le client Bureau à distance de Windows vers un hôte autorisé.

## Utiliser la barre

- **Affichage**, **Résolution** et **Plein écran** règlent le bureau.
- **Fichiers** copie les fichiers sélectionnés avec **Ctrl+C** dans l’Explorateur de la session source vers le Bureau du compte connecté. Windows doit autoriser la lecture et l’écriture dans les deux emplacements.
- La **flèche circulaire** permet de retenter une connexion. Le menu **Plus** donne accès aux journaux, aux builds et aux crédits.
- La **flèche vers le haut** réduit la barre en un moniteur turquoise. Clique dessus pour rouvrir la barre ou fais-le glisser pour la déplacer.

Le journal conserve son emplacement **%LOCALAPPDATA%\RayDesktop\Logs\child-session.log**. La ligne **RDP connected in** indique le délai jusqu’à l’état connecté.

## Compiler

Sur Windows, installe le compilateur .NET Framework et les outils du SDK Windows qui fournissent **AxImp.exe**. Lance **.\tests\run-tests.ps1** pour les tests de régression et **.\maintenance\build-portable.ps1** pour les tests de profil et le ZIP. GitHub Actions exécute les deux scripts. La recette du bureau local et des transferts doit être faite dans une session Windows interactive.
