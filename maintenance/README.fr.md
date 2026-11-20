# RayXDesktop 1.5.0j bêta

**Version maintenue par D.Synx.** Projet Ray Desktop d’origine par [raydoomed](https://github.com/raydoomed/Ray-Desktop), sous licence MIT.

## Démarrer

1. Télécharge **RayXDesktop-Windows.zip** dans la rubrique **Assets** de la [version 1.5.0j bêta](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.5.0j) et extrais son contenu dans un dossier.
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
- **Plus → Privilégier la fluidité** réduit certains effets et plafonne la définition initiale à 1 600 × 900, ainsi que les redimensionnements dynamiques du bureau local. Ce mode s’applique à la prochaine connexion locale ou à la création d’une connexion distante. **Graphismes et codec RDP** affiche les valeurs appliquées par Windows et les interrupteurs de la stratégie locale. Régle-les sur le poste qui héberge le bureau. Le rendu et l’encodage matériel dépendent de Windows, du pilote et du poste hôte.
- La **flèche vers le haut** réduit la barre en un moniteur turquoise. Clique dessus pour rouvrir la barre ou fais-le glisser pour la déplacer.

Clique sur la bande verticale de **4 px** à droite ou choisis **Plus → Ouvrir le journal d’activité** pour consulter les dernières lignes. Le chevron réduit le volet. Décoche **Plus → Afficher la bande du journal** pour faire disparaître la bande. Ce choix est mémorisé dans `data.ini` et le menu permet toujours d’ouvrir le journal. La ligne **RDP connected in** dans **%LOCALAPPDATA%\RayDesktop\Logs\child-session.log** indique le délai jusqu’à l’état connecté.

## Connexion distante et miniatures

- Indique une IP, un nom DNS ou un nom de poste. Pour un port personnalisé, utilise par exemple 192.0.2.15:3390 ou [2001:db8::15]:3390. Tu peux laisser l’adresse vide si tu indiques **NOMPC\session**. Pour un compte de domaine, indique aussi l’adresse du poste.
- Le mot de passe est facultatif dans le formulaire RayXDesktop. Si tu le laisses vide, RayXDesktop tente d’utiliser le mot de passe enregistré pour cette session ou laisse Windows le demander. Par défaut, Windows doit pouvoir authentifier le poste distant. Si l’IP ne correspond pas à l’identité vérifiable du poste, utilise son nom DNS.
- **Ctrl+C** et **Ctrl+V** copient le texte dans les deux sens entre la session principale et le bureau distant intégré, selon les stratégies Windows. Sélectionne le bureau distant, puis utilise **Plus → Partager le presse-papiers (prochaine connexion)** pour régler le partage de cette session. Reconnecte la session pour appliquer le choix.
- **Importer un .rdp** charge l’adresse et le compte. Un mot de passe protégé dans le champ Windows **password 51** peut servir pour cette tentative si le profil Windows courant peut le lire. Changer l’adresse ou le compte retire le secret importé du formulaire. L’import prend en charge les connexions directes à un bureau.
- Les sessions enregistrées réapparaissent dans la liste de gauche au démarrage. Clique sur une session pour lancer sa connexion. **Plus → Retirer la session enregistrée** la supprime de la liste.
- RayXDesktop prend une miniature dès que le bureau s’affiche et l’actualise lorsque tu quittes sa vue. **Plus → Ouvrir le dossier des miniatures** montre les PNG du lancement courant, sous **%LOCALAPPDATA%\RayDesktop\Thumbnails**. RayXDesktop les efface à la fermeture normale. Si le pilote ne fournit pas d’image exploitable, le bouton conserve l’aperçu précédent.

`data.ini` se trouve à côté de `RayXDesktop.exe`. Il contient les adresses et noms de compte des sessions, le mode d’avertissement par adresse, les choix de presse-papiers, d’affichage et de journal. Garde-le lors d’une mise à jour. Les anciennes préférences de journal et d’avertissement sont importées lors de la première création du fichier. Ajoute une dernière fois les sessions des versions antérieures, qui ne conservaient que les préférences.

### Mot de passe et démarrage automatique

Lors de l’ajout d’une connexion, saisis son mot de passe et coche **Enregistrer ce mot de passe sur ce PC**. Pour une session existante, sélectionne-la, puis choisis **Plus → Enregistrer ou modifier le mot de passe…**. Une connexion réussie enregistre le mot de passe sous forme chiffrée dans `data.ini`. Windows DPAPI protège le secret avec le profil Windows courant, généralement sur le même PC. En cas de changement de PC ou de profil, Windows peut redemander le mot de passe. [Protection DPAPI](https://learn.microsoft.com/fr-fr/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)

Coche **Plus → Reconnecter au démarrage** sur le bureau distant à ouvrir au lancement. Choisir une autre session lui donne la priorité. Le bureau local reste accessible depuis sa carte. **Oublier le mot de passe enregistré** retire le secret de `data.ini`.

RayXDesktop utilise le mot de passe enregistré uniquement avec l’authentification stricte du poste distant. En mode **Me demander en cas d’erreur de certificat**, l’invite Windows reste nécessaire, y compris au démarrage. L’ouverture automatique utilise les stratégies du poste et les droits du compte. [Modes d’authentification du serveur](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings4-authenticationlevel)

## Écran distant blanc et réglages graphiques

1. Clique sur la carte du bureau distant. Son bandeau indique si la liaison RDP est établie et si Windows a ouvert la session. Une déconnexion affiche le détail remonté par Windows et le journal conserve les codes.
2. Si la session Windows est ouverte mais la vue reste blanche, utilise **Plus → Actualiser l’affichage distant**. Vérifie le résultat en vue unique et côte à côte.
3. **Plus → Ouvrir cette adresse avec Bureau à distance Windows** ouvre le client natif avec la même adresse et le même compte. Il te demande à nouveau le mot de passe. Après un échec, **Plus → Copier le diagnostic de connexion distante** récupère le code client, le code étendu et l’étape d’ouverture de session. Le code client 0x107 seul ne suffit pas à attribuer la déconnexion au mot de passe ou à une licence.

### Certificat du poste distant

Après une déconnexion `0x00000107` sans cause précise avant l’ouverture de session, **Réessayer avec l’avertissement de certificat** apparaît dans la vue concernée. Utilise ce bouton si tu reconnais le poste. Après une connexion réussie dans ce mode, RayXDesktop mémorise le choix pour cette adresse et ce port dans `data.ini`. Les identifiants restent demandés par Windows.

Tu peux aussi activer ce mode depuis **Plus → Me demander en cas d’erreur de certificat (prochaine connexion)**. Clique sur **Reconnecter** pour la vue intégrée, ou ouvre l’adresse avec Bureau à distance Windows depuis ce menu. Décoche l’option pour oublier le mode mémorisé et rétablir la validation stricte à la prochaine connexion. Windows décide quand afficher son avertissement. [Modes d’authentification du serveur](https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings4-authenticationlevel)

Le client Windows peut afficher un avertissement sur l’éditeur du fichier `.rdp` temporaire avant toute connexion. L’erreur **L’ordinateur distant ne peut pas être authentifié** concerne le certificat présenté ensuite par le poste distant. [Avertissements liés aux fichiers RDP](https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/remotepc/understanding-security-warnings)

Si Windows indique que l’autorité de certification est inconnue, le service informatique doit vérifier le certificat utilisé par l’écouteur RDP du poste cible et la chaîne de confiance sur le poste client. Utilise le nom DNS couvert par le certificat. Changer seulement d’adresse ne corrige pas une autorité inconnue. Sur un parc géré, le service informatique peut déployer les certificats avec sa PKI et ses stratégies de domaine. [Configuration du certificat de l’écouteur RDP](https://learn.microsoft.com/fr-fr/troubleshoot/windows-server/remote/remote-desktop-listener-certificate-configurations)

### Réglages graphiques de départ

Sur le **poste hôte** autorisé, ouvre **Plus → Graphismes et codec RDP** puis choisis **Réglages conseillés**. Cette commande prépare les choix suivants.

| Réglage | Point de départ |
| --- | --- |
| Rendu avec la carte graphique matérielle | Activé |
| Encodage matériel H.264/AVC | Activé |
| Priorité à H.264/AVC 4:4:4 | Non configuré. Essaie Activé ensuite si tu veux comparer la netteté. |

Pour enregistrer, clique sur **Appliquer sur ce PC**, confirme le détail, puis valide la demande administrateur de Windows. **Non configuré** efface le réglage de la stratégie locale. **Désactivé** l’enregistre à l’état désactivé. Une stratégie du parc peut prendre le dessus. **Relire les valeurs** montre ce que Windows a appliqué. Le bouton **Ouvrir la stratégie locale** permet aussi d’utiliser l’éditeur Windows. Pour un premier essai, laisse les paramètres RemoteFX et WDDM du parc tels qu’ils sont.

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
| Dans le Bloc-notes, copier un texte différent dans chaque sens entre la session principale et un bureau distant intégré | Le texte arrive dans les deux sens si Windows autorise le partage. Décoche **Partager le presse-papiers**, reconnecte la session et essaie avec un nouveau texte pour vérifier que le partage est coupé. |
| Sur un poste pilote connu avec un certificat non approuvé, tester l’option **Me demander en cas d’erreur de certificat** et annuler l’avertissement Windows | Le mode par défaut refuse le certificat. En mode interactif Windows présente l’avertissement sur les postes où la stratégie le permet. Annuler ferme la connexion. |
| Après une déconnexion `0x00000107` sans cause précise avant l’ouverture de session, cliquer sur **Réessayer avec l’avertissement de certificat** | Le bouton apparaît dans la vue concernée. Windows affiche son avertissement si sa stratégie le permet. Si l’utilisateur annule, RayXDesktop affiche l’état déconnecté sans nouvelle tentative. |
| Réussir une connexion après le réessai, relancer RayXDesktop et rouvrir la même adresse et le même port | RayXDesktop démarre directement en mode interactif pour cette adresse. Décocher l’option du menu rétablit le mode strict. |
| Ouvrir puis réduire le journal, masquer sa bande et redémarrer RayXDesktop | Le journal suit les nouvelles lignes quand il est ouvert. La bande masquée ne réapparaît pas au redémarrage et le volet reste accessible dans le menu. |
| Ajouter deux sessions puis relancer RayXDesktop | Les sessions sont visibles à gauche. Un clic relance la connexion voulue et sa miniature apparaît dès que le bureau fournit une image exploitable. |
| Enregistrer un mot de passe pour un poste authentifié et le sélectionner pour le démarrage | La connexion réussie écrit un champ `PasswordDpapi` dans `data.ini`. Au prochain lancement cette session est sélectionnée et le client RDP utilise le mot de passe chiffré si Windows peut le lire. |
| Comparer Standard et Privilégier la fluidité | Mesurer séparément le délai de connexion et la fluidité en session. Relever les stratégies lues sans modifier le registre. |
| Lancer avec les sessions enfants désactivées, sur un poste déjà dans cet état | RayXDesktop indique le prérequis. Toute activation est réservée à un administrateur autorisé. |

Pour chaque poste, note la version de Windows, le résultat, le délai et le code d’erreur éventuel. Avant de partager un journal, retire les noms de comptes et les chemins locaux qu’il contient.
