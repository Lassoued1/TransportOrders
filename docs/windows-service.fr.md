# Service Windows

L'hôte API prend en charge le gestionnaire de services Windows via `Microsoft.Extensions.Hosting.WindowsServices`. La durée de vie et la configuration suivent les [instructions Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-8.0).

## Publication et installation

Pour cette installation, la base serveur est `C:\ProgramData\TransportOrders\extrusion.db`. WPF ne l'ouvre pas : il lit les instantanés et transmet les commandes à l'API. La [documentation consolidée EN/DE/FR](handbook/README.md) décrit le paquet actuel.

Depuis la racine du projet :

```powershell
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained true -o artifacts/alarms-api
```

Le paquet autonome inclut le runtime. Arrêter tout pilote central en console avant l'installation. Pour l'installation initiale, utiliser **PowerShell 7 administrateur** : `Path.IsPathFullyQualified`, appelé par le script, n'existe pas dans Windows PowerShell 5.1. Indiquer une base initialisée existante et son chemin absolu :

```powershell
.\scripts\Install-WindowsService.ps1 -DatabasePath 'C:\ProgramData\TransportOrders\extrusion.db' -PublishPath '.\artifacts\alarms-api'
```

Le script refuse une base absente et un service déjà installé. Il ne copie ni ne remplace la base. Il écrit `appsettings.Production.json` dans le paquet publié, utilise le compte intégré `NT AUTHORITY\LocalService`, accorde à ce compte la modification du répertoire de données (SQLite crée des fichiers journaux), et la lecture/exécution du paquet. Utiliser un répertoire de données dédié à TransportOrders, puisqu'il accorde les permissions à tout ce répertoire.

Le service `TransportOrders.Runtime` démarre automatiquement avec délai après Windows. Les échecs déclenchent une tentative de redémarrage après 60 secondes. Les erreurs métier d'un cycle restent journalisées et réessayées par le cycle suivant ; elles ne déclenchent pas un redémarrage du processus. L'arrêt demandé par un opérateur reste un arrêt normal.

Ne pas republier sur un service actif. Pour mettre à jour, publier dans un nouveau répertoire puis utiliser `Deploy-EquipmentApi.ps1` avec `-PublishPath` et les affectations prévues. Il conserve configuration, base, rôles et HTTPS. Sauvegarder avant migration ; le retour au programme précédent ne rétablit pas le schéma de base. L'installation initiale refuse d'écraser une configuration Production existante.

## WPF et exploitation

WPF utilise l'adresse du service et vérifie son identifiant d'installation. Pour le client local :

```powershell
$env:TRANSPORTORDERS_SERVICE_URL = 'http://localhost:5080'
```

Le poste n'a pas besoin de droits sur le dossier de base. Le compte Windows doit être authentifiable et autorisé par l'API. Le service exige toujours `Database:Path` absolu explicite ; ses permissions de fichier sont indépendantes des droits opérateur API.

L'interface EN/DE distingue service actif, automatisme arrêté, erreur et connexion indisponible. Une installation différente est refusée. L'indisponibilité HTTP ne prouve pas l'arrêt du service Windows : vérifier également son état. L'instantané est lu périodiquement, avec délai HTTP de cinq secondes et sans accès SQLite du poste.

```powershell
Get-Service TransportOrders.Runtime
Invoke-RestMethod http://localhost:5080/api/runtime -UseDefaultCredentials
Stop-Service TransportOrders.Runtime
Start-Service TransportOrders.Runtime
```

Les journaux `service-YYYYMMDD.log` sont dans le dossier configuré (14 fichiers quotidiens par défaut). L'installation initiale lie HTTP à localhost ; HTTPS et la règle réseau limitée se configurent séparément avec `Enable-DemoHttps.ps1`. Voir le [guide réseau](network-clients.fr.md).

Pour retirer le service, dans PowerShell administrateur :

```powershell
.\scripts\Uninstall-WindowsService.ps1
```

La base, les journaux, les binaires, la configuration et les permissions sont conservés. Le fichier `.driver.lock` restant n'est pas un verrou actif ; le système libère son handle après l'arrêt ou un crash.

Le mode Demo exécute le procédé simulé ; Commissioning prépare la consultation sans démonstration. Le raccordement physique et la recette restent nécessaires à l'exploitation industrielle.

Toutes les actions opérateur passent par l'API avec les [rôles](operator-roles.fr.md). Les anciennes listes ne s'appliquent qu'en configuration legacy ; `UseRoles=true` les neutralise. Redémarrer le service après modification des affectations.

## Attention à la redirection de stockage

Une application Windows empaquetée peut rediriger le chemin LocalAppData. Dans cette session Codex, la base visible comme `%LOCALAPPDATA%\TransportOrders\extrusion.db` était physiquement dans `%LOCALAPPDATA%\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\TransportOrders\extrusion.db`. Le service Windows ne voyait pas le chemin virtuel. Les droits sur les dossiers parents n'ont pas corrigé cette différence ; la copie vers ProgramData a permis le démarrage.

Après une copie cohérente de la base vers ProgramData, `scripts/Configure-SharedDatabase.ps1` raccorde un service déjà installé à cette copie, conserve la configuration précédente et accorde les droits de modification uniquement au compte LocalService et au SID de l'utilisateur choisi dans le dossier partagé.
