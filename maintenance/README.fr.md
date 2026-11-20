# RayXDesktop 1.5.0b bêta

**Version maintenue par D.Synx.** Projet Ray Desktop d’origine par [raydoomed](https://github.com/raydoomed/Ray-Desktop), sous licence MIT.

## Démarrer

1. Télécharge **RayXDesktop-Windows.zip** dans la rubrique **Assets** de la [version 1.5.0b bêta](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.5.0b) et extrais son contenu dans un dossier.
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
- **Fichiers** affiche **Envoyer vers RayXDesktop** et **Récupérer depuis RayXDesktop**. Dans l’Explorateur de la session source, sélectionne les fichiers ou dossiers et fais **Ctrl+C**. Choisis le sens, puis le dossier de destination. Le transfert local applique les droits des comptes Windows et les restrictions détectées. Les copies gérées par Windows affichent les options et la progression de l’Explorateur. La récupération nécessite que Windows transmette la sélection de la session enfant. Si le presse-papiers ne la reçoit pas, consulte **Diagnostic des transferts**.
- La **flèche circulaire** coupe puis relance la connexion sélectionnée, sans fermer volontairement ses programmes. **Plus → Déconnecter** coupe la liaison. **Plus → Fermer la session locale** affiche le compte concerné et ferme ses applications après confirmation. Fermer la fenêtre RayXDesktop déconnecte les vues.
- Dans **Plus → Afficher les sessions**, ajoute un poste distant autorisé par IP, par nom DNS ou en important un fichier .rdp de bureau direct. Windows prend en charge l’authentification et doit pouvoir vérifier l’identité du poste. Sélectionne successivement deux connexions, puis clique sur **Côte à côte**. La vue locale est limitée à une session enfant active par machine. Pour fermer un bureau distant, utilise la commande de fermeture de session dans ce bureau.
- **Plus → Privilégier la fluidité** réduit certains effets et plafonne la définition initiale à 1 600 × 900, ainsi que les redimensionnements dynamiques du bureau local. Ce mode s’applique à la prochaine connexion locale ou à la création d’une connexion distante. **Diagnostic graphique** affiche les stratégies GPU et d’encodage H.264 de ce PC et ouvre l’Observateur d’événements. Le rendu et l’encodage matériel dépendent de Windows, du pilote et du poste hôte.
- La **flèche vers le haut** réduit la barre en un moniteur turquoise. Clique dessus pour rouvrir la barre ou fais-le glisser pour la déplacer.

Le journal conserve son emplacement **%LOCALAPPDATA%\RayDesktop\Logs\child-session.log**. La ligne **RDP connected in** indique le délai jusqu’à l’état connecté.

## Connexion distante et miniatures

- Indique une IP, un nom DNS ou un nom de poste. Pour un port personnalisé, utilise par exemple 192.0.2.15:3390 ou [2001:db8::15]:3390. Tu peux laisser l’adresse vide si tu indiques **NOMPC\session**. Pour un compte de domaine, indique aussi l’adresse du poste.
- Le mot de passe est facultatif dans le formulaire RayXDesktop. Si tu le laisses vide, Windows le demande. L’authentification du poste distant reste obligatoire. Si l’IP ne correspond pas à l’identité vérifiable du poste, utilise son nom DNS.
- **Importer un .rdp** charge l’adresse et le compte. Un mot de passe protégé dans le champ Windows **password 51** peut servir pour cette tentative si le profil Windows courant peut le lire. Un champ vide, illisible ou un fichier qui utilise le Gestionnaire d’identification de Windows déclenche l’invite. RayXDesktop ne conserve ni le secret saisi ni une copie du fichier. Changer l’adresse ou le compte retire le secret importé du formulaire. L’import prend en charge les connexions directes à un bureau.
- Au changement de session, le bouton de la session quittée affiche sa dernière miniature disponible. **Plus → Ouvrir le dossier des miniatures** montre les PNG du lancement courant, sous **%LOCALAPPDATA%\RayDesktop\Thumbnails**. RayXDesktop les efface à la fermeture normale. Si le pilote ou la session ne fournit pas d’image exploitable, le bouton conserve l’aperçu précédent.

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
| Déconnecter, reconnecter, puis fermer la session locale | La reconnexion reprend le bureau et le document témoin quand Windows a conservé la session. Fermer la session locale demande confirmation et ferme ses applications. |
| Récupérer un fichier, un dossier et du texte depuis le bureau ouvert | Les éléments arrivent dans la session principale selon les permissions et redirections. Un blocage apparaît dans les diagnostics si la stratégie correspondante est détectable. |
| Ajouter un poste distant autorisé et activer Côte à côte | Les deux bureaux affichés répondent. La barre indique la session sélectionnée. La reconnexion et la fermeture ciblent cette session. |
| Comparer Standard et Privilégier la fluidité | Mesurer séparément le délai de connexion et la fluidité en session. Relever les stratégies lues sans modifier le registre. |
| Lancer avec les sessions enfants désactivées, sur un poste déjà dans cet état | RayXDesktop indique le prérequis. Toute activation est réservée à un administrateur autorisé. |

Pour chaque poste, note la version de Windows, le résultat, le délai et le code d’erreur éventuel. Avant de partager un journal, retire les noms de comptes et les chemins locaux qu’il contient.
