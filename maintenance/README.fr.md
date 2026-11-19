# RayXDesktop 1.4.7 bêta

**Version maintenue par D.Synx.** Projet Ray Desktop d’origine par [raydoomed](https://github.com/raydoomed/Ray-Desktop), sous licence MIT.

## Démarrer

1. Télécharge **RayXDesktop-Windows.zip** dans la rubrique **Assets** de la [version 1.4.7 bêta](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.4.7-beta) et extrais son contenu dans un dossier.
2. Garde **RayXDesktop.bat**, **RayXDesktop.exe**, **EnableChildSessions.exe** et les deux DLL dans ce dossier.
3. Dans **RayXDesktop.bat**, remplace **.\Admin** par le nom du compte autorisé si nécessaire. Le préfixe **.\** désigne un compte local du PC. Garde les guillemets autour de l’argument.
4. Double-clique sur **RayXDesktop.bat**, puis saisis le mot de passe dans la fenêtre RayXDesktop. Pour utiliser le compte Windows déjà connecté, ouvre directement **RayXDesktop.exe**.

Le BAT trouve l’EXE à côté de lui, même si la lettre de la clé USB change. Avec ce mode, RayXDesktop transmet le mot de passe saisi au contrôle RDP de Windows pour la tentative de connexion. L’application ne l’écrit ni dans le BAT ni dans les journaux.

Windows doit prendre en charge les sessions enfants. Si la fonctionnalité est désactivée, un administrateur autorisé peut lancer **EnableChildSessions.exe --enable-child-sessions**. Une déconnexion de Windows suivie d’une reconnexion peut être nécessaire après la première activation.

L’interface utilise le français sur les systèmes en français et l’anglais ailleurs. Les arguments **--lang=fr** et **--lang=en** forcent la langue. **--dynamic** utilise la taille de la fenêtre pour l’affichage.

## Options de connexion existantes

- **Bureau local standard.** Lance **RayXDesktop.exe**.
- **Compte explicite pour le bureau local.** Utilise **--account=.\Administrateur** pour un compte local, ou **--account=DOMAINE\Utilisateur** pour un compte de domaine autorisé. Pour migrer un ancien raccourci, retire l’argument **--password**. Il reste accepté pour la compatibilité, mais expose le mot de passe en clair dans la commande.
- **Autre poste de maintenance.** **--maintenance-host=maintenance.example.org --admin-account=IT\maint** ouvre le client Bureau à distance de Windows vers un hôte autorisé.

## Utiliser la barre

- **Affichage**, **Résolution** et **Plein écran** règlent le bureau.
- **Fichiers** copie les fichiers sélectionnés avec **Ctrl+C** dans l’Explorateur de la session source vers le Bureau du compte connecté. Windows doit autoriser la lecture et l’écriture dans les deux emplacements.
- La **flèche circulaire** permet de retenter une connexion. Le menu **Plus** donne accès aux journaux, aux builds et aux crédits.
- La **flèche vers le haut** réduit la barre en un moniteur turquoise. Clique dessus pour rouvrir la barre ou fais-le glisser pour la déplacer.

Le journal conserve son emplacement **%LOCALAPPDATA%\RayDesktop\Logs\child-session.log**. La ligne **RDP connected in** indique le délai jusqu’à l’état connecté.

## Compiler

Sur Windows, installe le compilateur .NET Framework et les outils du SDK Windows qui fournissent **AxImp.exe**. Lance **.\tests\run-tests.ps1** pour les tests de régression et **.\maintenance\build-portable.ps1** pour les tests de profil et le ZIP. GitHub Actions exécute les deux scripts. La recette du bureau local et des transferts doit être faite dans une session Windows interactive.

## Vérifier et signer le ZIP

Avant l’extraction, compare le SHA-256 calculé au premier champ de **RayXDesktop-Windows.zip.sha256** publié avec la bêta.

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath .\RayXDesktop-Windows.zip
Get-Content -LiteralPath .\RayXDesktop-Windows.zip.sha256
```

Le ZIP public de la bêta est compilé sans certificat de signature. Pour produire une archive signée, utilise un certificat de signature de code autorisé par l’établissement, présent dans le magasin personnel du compte Windows qui compile. Le SDK Windows fournit **signtool.exe**. Remplace les valeurs par ton chemin, ton empreinte et l’URL d’horodatage RFC 3161 fournie par l’autorité de certification.

```powershell
.\maintenance\build-portable.ps1 `
    -CodeSigningThumbprint 'EMPREINTE_HEXADECIMALE_SUR_40_CARACTERES' `
    -SignToolPath 'C:\Chemin\vers\signtool.exe' `
    -TimestampUrl 'https://horodatage.example.org'
```

Le script signe et vérifie les deux EXE et les deux DLL avant de créer le ZIP. Sur le poste pilote, contrôle que **Get-AuthenticodeSignature .\RayXDesktop.exe** renvoie **Valid** et que l’éditeur affiché correspond au certificat autorisé.

## Recette sur trois postes du lycée

Choisis trois profils réellement présents dans le parc, dont un poste avec les stratégies habituelles du domaine. Teste depuis une session standard avec une clé USB et un compte de maintenance autorisé. Pour tester un refus, utilise un compte de test et tiens compte du seuil de verrouillage du parc.

| Vérification | Résultat attendu |
| --- | --- |
| Lancer le BAT depuis la clé, puis annuler l’invite | Le compte configuré est affiché. Aucune connexion ne part après annulation. La flèche **Reconnecter** permet de relancer l’invite. |
| Essayer une connexion refusée, puis une connexion autorisée | La première affiche l’erreur Windows. La seconde ouvre le bureau du compte cible dans la fenêtre et laisse la session utilisateur ouverte. |
| Mesurer l’ouverture | Relever **RDP connected in** dans le journal. Si possible, comparer avec 1.4.6 sur le même poste, avec le même compte. |
| Copier un texte, puis transférer un fichier témoin avec **Fichiers** | Le texte se colle dans le bureau ouvert. Le fichier apparaît sur le Bureau du compte connecté. Un fichier déjà présent reste intact. |
| Fermer, puis relancer et vérifier le journal | La fenêtre se ferme proprement et la session principale reste utilisable. Aucun avertissement lié à un ancien lancement automatique de RayXDesktop ne réapparaît dans la session ouverte. |
| Lancer avec les sessions enfants désactivées, sur un poste déjà dans cet état | RayXDesktop indique le prérequis. Toute activation est réservée à un administrateur autorisé. |

Pour chaque poste, note la version de Windows, le résultat, le délai et le code d’erreur éventuel. Avant de partager un journal, retire les noms de comptes et les chemins locaux qu’il contient.
