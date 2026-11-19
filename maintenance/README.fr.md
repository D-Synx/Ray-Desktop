# Ray Desktop Maintenance

## Faisabilité

| Mécanisme | Bureau dans une fenêtre | Identité |
|---|---|---|
| Child Session Windows | Oui, sur le poste, avec la session principale toujours accessible | Identité Windows de la session parente |
| Élévation UAC d’un processus | Fenêtre de l’application élevée sur le bureau courant | Compte autorisé par Windows pour ce processus |
| RDP vers un hôte de maintenance ou une VM Windows approuvée | Oui, avec la session du poste principal inchangée | Compte distinct authentifié par l’hôte cible |

Microsoft documente la Child Session comme une session RDP locale liée à la session parente. Ray Desktop configure `ConnectToChildSession` et réutilise cette identité. L’invite UAC du programme ou de son assistant autorise un processus. Elle ne crée pas une deuxième session interactive sous l’identité saisie. Renseigner un nom dans le contrôle ActiveX ne constitue pas un mécanisme documenté de sélection d’un autre compte en mode Child Session.

Une connexion RDP standard vers le même Windows client ne fournit pas de garantie de deux bureaux simultanés. Les modifications de `termsrv.dll`, wrappers de sessions concurrentes et manipulations de jetons sont hors périmètre. Pour un bureau complet sous un compte distinct, le prototype utilise le client Windows vers un autre hôte autorisé. Une VM donne un bureau distinct, avec ses propres applications et son propre système. Les outils dans ce bureau agissent sur la VM ou sur les machines auxquelles ils accèdent par les canaux d’administration déjà autorisés.

Sources Microsoft [Child Sessions](https://learn.microsoft.com/en-us/windows/win32/termserv/child-sessions), [UAC](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works), [mstsc](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/mstsc).

## Utilisation du prototype

**Bureau enfant.** Lancer `Raydesktop.exe` avec ou sans les options historiques `--lang=en` et `--dynamic`. Ray Desktop conserve son affichage, sa barre d’outils, son pont de presse-papiers et son comportement de fermeture en mode enfant. Le programme lit l’état WTS et affiche un message si la fonctionnalité est désactivée.

Pour une activation **autorisée** sur un poste de test, lancer depuis un emplacement de confiance `EnableChildSessions.exe --enable-child-sessions`. Le manifeste demande une élévation UAC, puis l’assistant demande confirmation avant d’appeler `WTSEnableChildSessions`. Il relit l’état après l’appel. Le double-clic sans argument indique l’état, sans modifier la configuration. Si un renouvellement de session est nécessaire après la première activation, le faire manuellement en dehors d’une intervention. L’assistant ne modifie ni les GPO ni l’état de l’écouteur RDP. Il ne s’élève jamais automatiquement depuis la clé. Les réglages UAC du poste déterminent si Windows affiche une demande de consentement, demande des identifiants ou refuse.

**Compte distinct.** Configurer la cible du raccourci Windows en remplaçant la lettre du lecteur, le DNS et le compte par des valeurs autorisées.

```text
"E:\RayDesktopMaintenance\Raydesktop.exe" --maintenance-host=maintenance.example.org --admin-account=IT\maint
```

L’hôte doit exister, accepter le compte en RDP et présenter un certificat approuvé qui correspond au nom DNS. Le compte indiqué présélectionne un identifiant. Windows authentifie le compte auprès de l’hôte et l’opérateur doit vérifier l’identité effective dans le bureau distant. Le client RDP affiche sa propre fenêtre et son propre état de connexion. Le prototype lance `mstsc /public /prompt` et désactive les redirections usuelles, dont disques et presse-papiers. Aucun mot de passe n’est demandé par Ray Desktop ni écrit dans le raccourci, le profil temporaire ou le journal. Le mot de passe est saisi dans Windows. UAC s’applique à l’élévation des outils sur l’hôte distant selon sa politique et ne remplace pas l’authentification RDP. Une invite UAC locale à chaque ouverture RDP ne fait pas partie du prototype.

Le lanceur journalise le démarrage, les exceptions et la sortie du processus via le journal Ray Desktop, normalement dans `%LOCALAPPDATA%\RayDesktop\Logs\child-session.log`. La sortie de mstsc ne prouve ni l’établissement d’une connexion ni la fermeture de la session distante. Dans le bureau de maintenance, utiliser **Se déconnecter de Windows** pour fermer la session et les applications. Fermer la fenêtre mstsc déconnecte le client et peut laisser la session ouverte côté hôte.

## Compiler sans compilateur sur ton poste

1. Dans un dépôt GitHub sous ton contrôle, cloner l’amont et appliquer le patch fourni à la racine avec `git apply --check ray-desktop-maintenance.patch`, puis `git apply ray-desktop-maintenance.patch`. Si l’amont a évolué, utiliser la révision dont les blobs concordent avec le patch.
2. Commiter les fichiers appliqués. Une fois le workflow sur la branche par défaut, ouvrir **Actions → Ray Desktop Maintenance Windows → Run workflow**.
3. Télécharger l’artefact qui contient `RayDesktopMaintenance-Windows.zip`. Extraire ensemble les deux EXE et les deux DLL dans un dossier de la clé. Le build est prévu pour Windows x64 avec .NET Framework 4.8 et le contrôle RDP Windows. Les EXE produits par le workflow ne sont pas signés.

Pour compiler sur une machine Windows autorisée, utiliser Windows PowerShell avec le compilateur .NET Framework et les outils .NET Framework du SDK Windows, notamment `AxImp.exe`. Ils peuvent être installés via les composants SDK de Visual Studio Build Tools. Depuis la racine, exécuter `.\maintenance\build-portable.ps1`. Le script exécute les tests de profil, puis le build amont et crée l’archive. Si le SDK est installé à un emplacement personnalisé, définir `RAYDESKTOP_SDK_ROOT` vers sa racine. L’agent GitHub compile et teste le profil, sans ouvrir de bureaux interactifs.

## Recette Windows avant distribution

| Scénario | Résultat attendu |
|---|---|
| Fonction désactivée, lancement de Ray | État désactivé, aucune élévation ni modification |
| Assistant sans argument, refus UAC, puis refus de confirmation | Configuration inchangée pour chaque cas |
| Activation explicitement autorisée | État relu et confirmé, aucune fermeture automatique de la session principale |
| Mode enfant depuis un compte standard | Identité identique dans les deux bureaux. Vérifier affichage, presse-papiers et fermeture de l’enfant |
| RDP avec deux comptes distincts | Comparer `whoami /user` dans les deux bureaux. Vérifier que les applications locales restent ouvertes |
| Annulation des identifiants, compte refusé, hôte hors ligne ou certificat non approuvé | Erreur ou annulation native. Aucun repli vers une Child Session |
| Outil administratif sur l’hôte | Vérifier le comportement UAC et l’identité avec la politique de cet hôte |
| Fermeture de la fenêtre et fermeture de session | Comparer la persistance côté hôte. Vérifier le journal et la disparition du fichier temporaire après sortie normale |
| Lancement depuis la clé avec un chemin contenant des espaces | Le raccourci fonctionne avec les guillemets et affiche le bon compte |

Une compilation Windows et cette recette restent à exécuter. Le dépôt amont est en lecture seule pour ce compte. Le patch ne publie rien sur GitHub.
