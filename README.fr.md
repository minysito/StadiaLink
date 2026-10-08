<div align="center">
  <img src="app/assets/readme-logo.svg" width="112" height="112" alt="Logo StadiaLink">
  <h1>StadiaLink</h1>
  <p><strong>Votre Stadia. À votre façon.</strong></p>
  <p>Configuration de la manette Stadia pour Windows 11 x64 · Version 1.0</p>
  <p><a href="https://github.com/minysito/StadiaLink/releases/latest"><strong>⬇ Télécharger</strong></a> · <a href="https://github.com/minysito/StadiaLink/issues">Signaler un problème</a> · <a href="LICENSE">GPL-3.0</a></p>
  <p>🪟 Windows 11 x64 &nbsp; · &nbsp; 🎮 XInput &nbsp; · &nbsp; 🔗 USB + Bluetooth</p>
  <p><a href="README.md"><img src="app/assets/flag-es.svg" width="24" height="16" alt="ES"> Español</a> &nbsp; | &nbsp; <a href="README.en.md"><img src="app/assets/flag-en.svg" width="24" height="16" alt="EN"> English</a> &nbsp; | &nbsp; <strong><img src="app/assets/flag-fr.svg" width="24" height="16" alt="FR"> Français</strong> &nbsp; | &nbsp; <a href="README.de.md"><img src="app/assets/flag-de.svg" width="24" height="16" alt="DE"> Deutsch</a></p>
</div>

---

## 🎮 Fonctionnalités

- Connexions USB et Bluetooth avec sortie XInput.
- Attribution des boutons, raccourcis clavier et appuyer pour parler.
- Sensibilité des sticks, zones mortes et réglage des gâchettes.
- Vibration par moteur et test continu avec arrêt manuel.
- Importation et exportation de profils.
- Batterie, notifications, zone de notification et démarrage avec Windows.
- Interface adaptable en espagnol, anglais, français et allemand.

## 🚀 Installation

Téléchargez `StadiaLink.Setup.exe` depuis les [versions publiées](https://github.com/minysito/StadiaLink/releases/latest), lancez-le et autorisez l'installation du pilote avec les droits administrateur. Connectez la manette par USB ou associez-la dans les paramètres Bluetooth de Windows, puis allumez-la. StadiaLink détecte la manette disponible.

Le Bluetooth nécessite le firmware Bluetooth de la manette. StadiaLink ne modifie pas le firmware. L'installateur contient ses composants et le code source correspondant et fonctionne hors ligne. Il utilise .NET Framework 4.8 et les composants Windows, sans émulateur de manette ni environnement d'exécution tiers supplémentaire.

Comparez l'empreinte du téléchargement avec `SHA256SUMS.txt` de la même version :

```powershell
Get-FileHash .\StadiaLink.Setup.exe -Algorithm SHA256
```

## 🛠 Compiler depuis zéro

### Outils nécessaires

- Windows 11 x64 et Windows PowerShell 5.1, fourni avec Windows.
- [Git pour Windows](https://git-scm.com/downloads/win) ou l'archive ZIP du code GitHub.
- [Visual Studio 2022 ou Build Tools 2022](https://visualstudio.microsoft.com/downloads/) avec **Développement Desktop en C++**, MSVC x64 et Windows SDK **10.0.26100.0**.
- .NET Framework **4.8**. Les scripts utilisent `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` et les bibliothèques WPF de Windows. Node.js et `dotnet build` ne sont pas nécessaires.
- Internet pour la première compilation du pilote : téléchargement de `Microsoft.Windows.WDK.x64` **10.0.26100.6584** depuis NuGet, utilisation d'UMDF **2.31** et cache dans `.build/driver/.wdk`.

Ouvrez Windows PowerShell dans un dossier de travail. La compilation ne nécessite pas les droits administrateur ; l'installation du pilote les nécessite.

```powershell
git clone https://github.com/minysito/StadiaLink.git
cd StadiaLink

# Si les scripts sont bloqués, uniquement pour cette session.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned

# Pilote, application, bibliothèque native et installateur.
.\app\package.ps1 -Driver

# Tests de traduction des entrées et du modèle de l'application.
.\tests\run.ps1
```

Avec le ZIP, extrayez-le et ouvrez PowerShell dans le dossier contenant `app`, `driver` et `tests`. Vérifiez les scripts avant de les débloquer si Windows les marque comme téléchargés depuis Internet. L'exemple ne modifie pas la stratégie d'exécution permanente du PC.

| Chemin | Résultat |
| --- | --- |
| `release/StadiaLink.exe` | Application WPF x64. |
| `release/StadiaLink.exe.config` | Configuration .NET Framework. |
| `release/StadiaDevice.dll` | Lecture native de la batterie. |
| `release/StadiaLink.Setup.exe` | Installateur avec composants et `Source.zip` intégré. |
| `release/StadiaLink.Setup.exe.config` | Configuration de l'installateur. |
| `release/SHA256.txt` | Empreinte de l'installateur compilé localement. |
| `driver/out/pkg/` | Paquet du pilote UMDF compilé. |
| `.build/` | Dépendances, objets et résultats des tests. |

Exécutez `release/StadiaLink.Setup.exe` pour installer votre compilation. Lancer uniquement l'application n'installe pas le pilote. Pour compiler séparément :

```powershell
.\driver\build.ps1       # Pilote uniquement.
.\app\build.ps1          # Application et bibliothèque native.
.\app\package.ps1        # Installateur utilisant le pilote déjà compilé.
.\tests\run.ps1          # Nécessite l'application compilée.
```

Si MSVC manque, vérifiez la charge de travail C++ de Visual Studio. Installez le SDK indiqué si des en-têtes ou bibliothèques manquent. Vérifiez l'accès à NuGet si le téléchargement du WDK échoue. Chaque compilation du pilote inclut une date et une version ; les binaires ne sont pas nécessairement identiques entre compilations.

## ⚙️ Utilisation

Réglez le profil et cliquez sur **Enregistrer et appliquer**. Changer d'onglet abandonne les modifications du profil non enregistrées. La case de démarrage avec Windows est enregistrée immédiatement et ouvre l'application dans la zone de notification à la connexion de cet utilisateur. Le test de vibration continu s'arrête avec **Arrêter**, au changement d'onglet, à la déconnexion ou à la fermeture.

Les raccourcis nécessitent que l'application reste ouverte, même réduite dans la zone de notification. Configurez la même touche ou combinaison pour parler dans votre jeu ou application vocale. Le pilote conserve la configuration XInput appliquée après la fermeture de l'interface.

Les profils sont dans `%LOCALAPPDATA%\StadiaStudio\profiles.json` et l'application dans `%LOCALAPPDATA%\Programs\StadiaStudio`. Ces noms internes sont conservés pour la compatibilité avec les versions précédentes.

## 🔋 Détection de la batterie

Aucune activation du firmware ni utilité supplémentaire n'est nécessaire. `StadiaDevice.dll` consulte le pourcentage en arrière-plan toutes les **30 secondes** et l'affiche dans l'interface et l'icône de notification.

- **Bluetooth :** Windows doit exposer le service BLE Battery Service (`0x180F`) de la manette associée. La bibliothèque lit Battery Level (`0x2A19`) directement sur cette manette et valide une valeur d'un octet entre 0 et 100. Elle n'écrit pas dans le service de batterie et ne modifie pas HID.
- **USB :** la bibliothèque recherche l'interface WinUSB Stadia `VID_18D1 / PID_9400 / MI_00`, demande la valeur avec la requête de contrôle `0x83` puis la récupère avec `0x84`. Elle vérifie le format et le pourcentage sans écrire de firmware.
- **Charge :** la connexion USB affiche **En charge**, ou **Charge complète** si le niveau lu est de 100 %. Cette indication repose sur la connexion ; elle ne mesure pas le courant et ne confirme pas la charge physique.
- **Notifications :** si elles sont activées, elles signalent connexion, déconnexion, batterie à **15 % ou moins** et niveau de **100 %**. La notification de charge complète exige une lecture valide.

**Batterie indisponible** indique que la lecture a échoué ; aucun pourcentage n'est inventé. En Bluetooth, éteignez puis rallumez la manette et attendez la prochaine lecture. Si nécessaire, supprimez l'association et recommencez pour que Windows réénumère les services. En USB, vérifiez la connexion de données et l'interface WinUSB. Le fonctionnement des boutons ne garantit pas l'accès à la batterie.

## 💬 Problèmes, contributions et désinstallation

Ouvrez un [ticket](https://github.com/minysito/StadiaLink/issues) en indiquant versions de Windows et de l'application, type de connexion et étapes de reproduction. Le journal d'installation est dans `%LOCALAPPDATA%\StadiaStudio\logs\driver-install.log` ; retirez les données personnelles avant de le partager.

Les pull requests sont bienvenues. Décrivez vos vérifications et exécutez `tests/run.ps1` si vous modifiez la configuration ou la traduction des entrées. Ne publiez pas de dépendances téléchargées, binaires générés, profils personnels ou recherches sur le firmware.

Désinstallez depuis **Applications installées** de Windows. Pour retirer seulement le pilote d'une copie compilée :

```powershell
.\driver\install.ps1 -Uninstall
```

Le script demande les droits administrateur, retire les paquets de ce pilote et rétablit le pilote HID de Windows. Une reconnexion peut être nécessaire.

## 🤝 Compatibilité, crédits et licence

La vibration Bluetooth utilise des interfaces privées de Windows, testées sur Windows 11 build **26200** ; les mises à jour peuvent les modifier. La prise audio fonctionne en USB. L'audio Bluetooth, l'activation du microphone intégré et du Wi-Fi ne sont pas proposés. Les temps mesurés concernent les requêtes HID et l'interface, pas la latence physique totale du jeu et de l'écran.

Ce projet a été rendu possible en partie par **WinStadia d'aisk**, dont dérive le pilote natif. Dépôt original complet : **https://github.com/aisk/WinStadia**.

Référence : [commit e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a](https://github.com/aisk/WinStadia/commit/e9e84a0a24c3cc13bf7cc7ac2b8baa2b653ce80a). Les avis de copyright sont conservés. StadiaLink et ses modifications sont distribués sous **GPL-3.0** ; voir [LICENSE](LICENSE) et [UPSTREAM-README.md](UPSTREAM-README.md). Projet indépendant de Google et Microsoft.

## ✨ Développement avec ChatGPT / Codex

Le projet est géré et développé avec **ChatGPT / Codex**, selon une approche de **vibe coding** : le responsable définit les fonctionnalités et examine les résultats ; Codex aide à implémenter, rechercher, documenter et vérifier. Les tests sur la manette physique et la revue des changements font partie du travail. Cette assistance n'implique aucune certification ni approbation d'OpenAI, Google ou Microsoft.
