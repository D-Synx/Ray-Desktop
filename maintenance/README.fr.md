# RayXDesktop

**Version maintenue par D.Synx.** Projet Ray Desktop d’origine par [raydoomed](https://github.com/raydoomed/Ray-Desktop), sous licence MIT.

## Démarrer

Télécharge l’artefact **RayXDesktop-Windows** depuis un build réussi, puis extrais le ZIP portable. Garde les deux EXE et les deux DLL dans le même dossier. L’exécutable principal s’appelle **RayXDesktop.exe**. Si ton raccourci ou ton BAT utilise encore **Raydesktop.exe**, remplace uniquement ce nom par **RayXDesktop.exe**.

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
