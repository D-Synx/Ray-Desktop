# RayˣDesktop 1.6.0 bêta · Accélération GPU

## Audit et périmètre

La branche expérimentale part du tag `v1.5.0.1`, commit `7cc2d5b`, qui correspondait à `ray-desktop-maintenance` au début du travail. La branche `main` avait divergé. Une PR vers `main` contient donc aussi l’historique de maintenance absent de cette branche. La comparaison avec `v1.5.0.1` isole l’expérience GPU.

`Program.cs` lance l’application WinForms, l’agent de presse-papiers ou le client de maintenance suivant les arguments. `ChildSessionControl.cs` encapsule le contrôle Microsoft RDP ActiveX. `DesktopForm.cs` configure le bureau local, le plein écran, le rapport largeur/hauteur et les tailles de session. `RemoteDesktop.cs` pilote les sessions distantes, leurs événements de connexion, Smart Sizing et la mise à jour de taille. `SessionWorkspace.cs` répartit les vues et demande leurs miniatures. `SessionThumbnails.cs` capture les fenêtres avec PrintWindow. `build.ps1` génère les wrappers ActiveX via AxImp et compile les exécutables du client. Ces connexions et contrôles conservent leur rôle dans cette bêta.

L’API publique de MsRdpClient9 expose notamment les réglages de taille et de synchronisation. Elle ne fournit pas de texture partagée que RayˣDesktop pourrait transférer à Direct3D ou importer dans Vulkan. La composition et le décodage du bureau vivant restent sous le contrôle de Windows et de l’hôte RDP. Les stratégies Windows de rendu matériel et H.264 côté hôte restent accessibles dans l’écran existant **Graphismes et codec RDP**. La présence de `vulkan-1.dll` seule ne sélectionne aucun moteur. [API ActiveX Microsoft](https://learn.microsoft.com/en-us/windows/win32/termserv/msrdpclient9)

## Moteurs comparés

| Choix | Opération dans RayˣDesktop | Effet attendu et coût à mesurer |
| --- | --- | --- |
| Windows natif | Mise à l’échelle GDI+ de la capture, avec bandes conservant le rapport largeur/hauteur | Référence. Le traitement échappe au coût de lancement du module et aux copies CPU ↔ GPU. |
| Automatique | Chemin Windows par défaut | L’évaluation sur poste pilote établira si le coût complet d’un autre moteur justifie une sélection automatique. |
| Direct3D 11 expérimental | Device matériel D3D11, composition Direct2D, mise à l’échelle bilinéaire, lecture d’une bitmap de sortie | Pilote DXGI matériel requis. Microsoft Basic Render/Display et les adaptateurs logiciels sont écartés. D3D12 apporterait davantage de gestion explicite sans accès nouveau aux surfaces ActiveX. |
| Vulkan expérimental | Envoi BGRA, images GPU, `vkCmdBlitImage` linéaire, copie et lecture en retour | Chargeur système, pilote intégré ou dédié, file graphique et format BGRA compatible requis. Les pilotes CPU et GPU virtuels sont exclus de l’expérience. |

Une capture de la fenêtre est faite avant la mise à l’échelle. Seule cette dernière est déléguée au processus x64. Les pixels passent en mémoire par les flux du processus, et seuls les PNG de miniatures déjà prévus par l’application sont écrits sur disque. Les dimensions GPU sont bornées à 8 192 pixels par axe et environ 16,8 millions de pixels par image. Il y a une miniature en traitement à la fois. L’application conserve les miniatures existantes, la taille dynamique, Smart Sizing, le plein écran et la vue côte à côte. Les pilotes, les moniteurs et la qualité de la capture PrintWindow peuvent influencer le résultat. [Blit Vulkan](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdBlitImage.html) · [Bitmap Direct2D](https://learn.microsoft.com/en-us/windows/win32/api/d2d1_1/ne-d2d1_1-d2d1_bitmap_options) · [Adaptateurs DXGI](https://learn.microsoft.com/fr-fr/windows/win32/direct3ddxgi/d3d10-graphics-programming-guide-dxgi)

Une absence de module, un échec de la mise à l’échelle ou un délai d’environ six secondes entraîne un repli Windows. La sélection explicite peut être réappliquée pour réessayer. Le lancement par miniature et les transferts de pixels font partie du coût et peuvent rendre le GPU plus lent à ce format. L’architecture isolée évite de charger un pilote Vulkan dans le processus qui héberge les sessions.

## Diagnostics et vidéo

Dans **Plus → Accélération graphique**, choisis un moteur, clique sur **Appliquer**, puis utilise **Plus → Actualiser la miniature sélectionnée**. Rouvre le panneau pour lire le moteur de la dernière miniature et son temps de traitement après capture. Le choix est enregistré dans `data.ini` sous `ThumbnailEngine`. Le diagnostic indique les adaptateurs et versions de pilote rapportés par Windows si WMI répond, le niveau Direct3D du périphérique ouvert et, si disponible, le périphérique Vulkan ainsi que sa version de pilote brute.

Le diagnostic D3D11 interroge des formats NV12 pour trois profils fournis par le pilote (H.264, HEVC Main, AV1 profil 0). Vulkan affiche l’exposition des extensions de file vidéo et de décodage H.264, H.265 et AV1. Une future chaîne Vulkan Video doit vérifier la file et les profils exacts avec `vkGetPhysicalDeviceVideoCapabilitiesKHR`, négocier format, résolution et profondeur, puis gérer le flux compressé, les surfaces de référence et leur synchronisation. L’ActiveX ne fournit pas le flux à ce prototype. Une chaîne alternative exige une étude séparée de l’interopérabilité RDP, du chiffrement, de l’authentification, des Child Sessions et des redirections. Le décodeur actif de l’ActiveX, les FPS du bureau et la charge GPU par processus ne sont pas déclarés par l’application. [Décodage D3D11](https://learn.microsoft.com/en-us/windows/win32/medfound/supporting-direct3d-11-video-decoding-in-media-foundation) · [Profils vidéo Vulkan](https://docs.vulkan.org/spec/latest/chapters/videocoding.html)

## Compiler sous Windows

Prérequis. Windows 10 ou 11 x64, .NET Framework 4.8, SDK Windows avec AxImp, Visual Studio 2022 Build Tools avec la charge de travail C++ et Python 3 pour le test natif. Le client peut aussi être compilé sans le module via `maintenance\build-portable.ps1`. Le module lie statiquement le runtime C++ et utilise les bibliothèques Direct3D 11 et Direct2D de Windows. Les en-têtes officiels Khronos au commit ci-dessous servent seulement à la compilation (Apache-2.0 ou MIT selon fichier). Le chargeur et les pilotes Vulkan proviennent de Windows et de l’installation du constructeur. Aucune DLL Vulkan ou implémentation logicielle n’est livrée. [Vulkan-Headers et licences](https://github.com/KhronosGroup/Vulkan-Headers/tree/2cd90f9d20df57eac214c148f3aed885372ddcfe)

```powershell
git clone https://github.com/KhronosGroup/Vulkan-Headers.git ..\Vulkan-Headers
git -C ..\Vulkan-Headers checkout 2cd90f9d20df57eac214c148f3aed885372ddcfe
powershell.exe -NoProfile -File .\tests\run-tests.ps1
.\maintenance\build-gpu.ps1 -VulkanHeaders ..\Vulkan-Headers
python .\tests\test-gpu-preview.py .\Raydesktop\RayXDesktop.GpuPreview.exe
.\maintenance\build-portable.ps1 -GpuHelperPath .\Raydesktop\RayXDesktop.GpuPreview.exe
```

GitHub Actions fournit la même compilation sur Windows avec l’archive et son SHA-256. Le test Direct3D WARP utilise explicitement le rendu logiciel pour vérifier les pixels sans carte disponible. Ce mode n’est pas proposé dans l’application et ne compte pas comme test matériel.

## Protocole reproductible sur poste pilote

1. Consigner Windows, CPU, GPU, pilote, alimentation, DPI, écrans, résolution de session, transport et profil graphique. Utiliser une session de test autorisée et le même contenu de bureau. Comparer des séquences identiques en Windows natif, Direct3D et Vulkan. Refaire en vue unique et côte à côte, avec un écran puis deux écrans de DPI différents. Séparer Child Session et RDP réseau, dont les latences ne sont pas comparables.
2. Exécuter le test des pixels synthétiques avec `--require d3d11` et `--require vulkan` sur le GPU compatible. Chaque option transforme le saut normal d’un test matériel en échec. Le test confirme une opération de mise à l’échelle et un retour de pixels. Il ne mesure pas les performances RDP.
3. Après cinq essais, effectuer au moins trente actualisations par moteur et relever à chaque fois le moteur effectivement utilisé ainsi que le temps après capture. Relever aussi le temps total entre le clic et le renouvellement de l’aperçu. Calculer médiane et 95ᵉ percentile. Consigner les replis séparément.
4. Répéter un parcours chronométré avec bascule de session, redimensionnement et défilement. Mesurer CPU et mémoire pour RayXDesktop et le module éphémère, et les moteurs GPU avec une trace ETW/WPR analysée dans WPA ou GPUView. Pour FPS et latence du bureau, utiliser un motif numéroté ou horodaté dans la session et une capture externe à fréquence connue. Une fréquence de rafraîchissement du moniteur ne mesure pas les FPS du flux. Laisser les outils de capture identiques entre mesures.
5. Renommer temporairement le module lorsque RayXDesktop est fermé et vérifier que les miniatures restent disponibles, puis rétablir son nom. Tester aussi un poste sans pilote Vulkan et les changements d’écran ou de DPI habituels. Vérifier l’authentification, la reconnexion et les transferts selon le guide de maintenance.

| Mesure | État de référence |
| --- | --- |
| CPU, GPU, mémoire, FPS et latence RDP | À relever sur un poste pilote avec GPU compatible. |
| Temps de mise à l’échelle | Affiché dans l’application après chaque miniature. Aucune mesure comparative publiée. |
| Régression et protocole binaire | Contrôles exécutés par GitHub Actions. Un saut matériel reste signalé dans les journaux. |

## Historique 1.6.0 bêta

- Sélection des moteurs, diagnostics GPU et persistance de la préférence.
- Mise à l’échelle des miniatures avec Direct3D 11/Direct2D ou Vulkan sur périphérique matériel, traitement hors du thread de l’interface après la capture.
- Repli natif, bornes de dimensions et de durée, tests du protocole, du rendu disponible et du paquet portable.
