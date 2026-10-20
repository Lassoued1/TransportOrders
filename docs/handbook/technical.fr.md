# TransportOrders - Documentation technique

Édition 1.1 | 12 octobre 2026 | Français

## 1. Périmètre et version de référence

Ce document décrit le code actuel et les paquets `recipes-api` / `recipes-client`. Un service installé peut utiliser un paquet plus ancien : vérifier son chemin d’exécutable avant de considérer les alarmes ou sauvegardes comme actives. Le projet est un démonstrateur indépendant de flux matière pour l’extrusion aluminium. Aucune compatibilité certifiée avec MCVIS n’est établie.

Fonctions développées : modèle physique persistant, ordonnancement, traitement de four simulé, emballage et réutilisation des paniers, échanges PLC simulés durables, commandes authentifiées Windows, client WPF EN/DE, rôles, alarmes, sauvegarde à chaud et profil de mise en service en consultation. Le driver d’automate réel, les recettes industrielles approuvées, l’interface ERP et l’impression intégrée de codes-barres/étiquettes restent à développer. Un champ de lecture de code et une étiquette textuelle existent. Le français est une langue documentaire, pas une langue de l’interface.

La référence validée compte 338 tests : 89 métier, 58 application et 191 intégration. Un résultat logiciel ne constitue pas la recette d’une installation physique.

## 2. Architecture et responsabilités

| Composant | Responsabilité |
| --- | --- |
| Core | Entités, transitions, contraintes, ordonnanceur, modèle d’échange PLC et détection des alarmes ; aucune référence WPF ou SQLite. |
| Data | Repositories EF Core/SQLite, migrations, initialisation de démonstration, boîtes de commandes/réponses PLC et sauvegarde à chaud. |
| API | Service ASP.NET Core, authentification Windows, autorisations, écritures sérialisées et workers runtime, sauvegarde et alarmes. |
| App | Client WPF .NET 8 utilisant les instantanés et commandes API ; référence Core, pas Data. |

Le client lit `/api/client-state` environ toutes les deux secondes, hors opération principale occupée. Il n’ouvre jamais la base serveur. `PlantConnection` conserve une adresse et fixe le `PlantId` ; `RemotePlantRepository` autorise les lectures et refuse les écritures locales. Les identifiants Windows de la session sont utilisés. Les redirections HTTP automatiques et le contournement des contrôles de certificat sont désactivés.

`PlantOperationGate` sérialise les changements opérateur, actions runtime et synchronisations d’alarmes dans un processus API. `PlantDriverLease` conserve `<base>.driver.lock` pendant la vie du runtime et exclut un second ordonnanceur sur cette base locale. La présence du fichier seul ne prouve pas la détention du verrou. Ce mécanisme coordonne des processus locaux, pas un cluster distribué.

## 3. Modèle métier et invariants

| Entité | Fonction / invariant |
| --- | --- |
| Basket / MaterialLot / PlantLocation | Identité physique, contenu, position et réservation ; la position n’est pas recalculée depuis le dernier ordre à chaque lecture. |
| TransportOrder / Transport | Une étape de mouvement et son chargement ; historique avec identité, date et motif. |
| PlantEquipment | Mode, état, opération active et version. Défaut, maintenance ou mode manuel peuvent bloquer l’automatique. |
| OvenRecipe / OvenCycle / OvenCycleLoad | Compatibilité d’alliage et copie de recette pour la séquence de chargement traitée. |
| PackedPackage / BasketReturn | Identité et qualité du colis indépendantes du panier réutilisable et de son retour. |
| PlcCommand / PlcInbox / PlcSimulatorExecutions | Commande durable, événements reçus validés et exécution simulée dédupliquée. |
| PlantAlarm / AlarmEvent | Une occurrence de condition, sa disparition, son acquittement et son historique. |

Un rechargement incrémente `ContentSequence`. Un traitement ou colis ancien ne valide pas le nouveau chargement. Un chargement contient un lot matière et produit un colis ; mélange et fractionnement ne sont pas pris en charge. Le départ contrôle position source, capacité destination, réservations, équipements et compatibilité de four. L’arrivée valide atomiquement la position et l’application de la commande.

Les anciens noms restent utilisés : `HeatNumber` porte la référence de lot matière, `PieceNumber` l'identité du panier, `ParentPieceNumber` son lot de billettes et `BasketContentSequence` la séquence de chargement. Unités : tonnes, mètres de chargement, degrés Celsius et minutes simulées. Les dates d'exploitation sont persistées en UTC ; les équipes utilisent `Plant:TimeZone`, et les dates d'alarme sont affichées dans l'heure Windows locale du poste.

Un chargement complet atteint 90 % de la longueur disponible. Il ne peut dépasser longueur ou charge utile, partir vide ni mélanger des trajets. Un chargement partiel nécessite un motif autorisé enregistré. Les états d’ordre sont `Planned`, `OnHold`, `InTransit`, `Done`, `Cancelled`. Le départ est le passage à `InTransit` ; `Done` et `Cancelled` sont définitifs.

Le four traverse chauffage, maintien, refroidissement et fin de traitement. Le cycle copie les paramètres de recette et les identités de chargement au démarrage. Un événement avance d’une minute simulée ; un arrêt du service n’ajoute aucune durée. Les températures sont interpolées, sans lecture de capteur. Les contrôles visuel/quantité conditionnent l’approbation, le texte d’étiquette et l’expédition. La qualité automatique de démonstration est simulée.

Gestion des recettes : GET /api/recipes et POST /api/recipes/{id}/create, revise, validate ou archive. Écriture réservée au rôle Supervisor, avec identité du site, motif et version attendue pour une recette existante. Le verrou partagé et la concurrence EF empêchent les écritures périmées. RecipeEvents conserve les paramètres complets ; les cycles stockent RecipeRevision. La migration 20261012172347_VersionedOvenRecipes conserve les recettes existantes comme révision 1 validée et importe une seule fois leur état initial. Aucune suppression de recette. Les paramètres restent illustratifs pour la simulation.

## 4. Échanges PLC et concurrence

Commande et réservation sont enregistrées ensemble. La commande possède GUID stable, version de contrat, équipement, opération, source, destination, contenu immuable et empreinte. Le simulateur mémorise l’exécution par GUID. Un réessai conserve le GUID et le contenu.

| État | Signification |
| --- | --- |
| Pending | En file, non envoyée. |
| AwaitingAck | Tentative enregistrée ; réponse attendue. |
| Accepted | Acceptation reçue ; arrivée non démontrée. |
| Completed | Fin reçue ; application physique éventuellement différée. |
| Applied | Arrivée et réponse validées ensemble. |
| TimedOut | Délai dépassé ; réservation conservée. |
| Rejected | Rejet PLC ; examen opérateur nécessaire. |

Le délai est de 15 secondes, avec trois tentatives d’envoi au maximum. L’expiration ne renvoie pas automatiquement. Doublons d’événements, équipement/trajet/empreinte différents et séquences anciennes sont contrôlés. Une réponse tardive valide peut résoudre une expiration sans renvoi. Un défaut peut différer l’application d’une fin déjà reçue. La confirmation manuelle est une réconciliation physique tracée, pas une réponse machine inventée.

Les versions GUID et `OrderRevision` protègent contre les postes périmés. Cette révision est une représentation SHA-256 de l’état pertinent et de l’historique de l’ordre. `ExpectedPlantId` vérifie l’installation ; le contrôle historique par chemin de base reste disponible pour les anciens clients de commandes. Une réponse perdue peut suivre une action déjà validée : relire état et historique avant de décider d’une nouvelle action.

## 5. Identité, rôles et modes

Negotiate authentifie le principal Windows. L’acteur vient du principal, jamais d’un nom JSON. Les consultations métier sont authentifiées ; `/health` est une disponibilité HTTP anonyme, pas une preuve de bon fonctionnement du procédé.

| Rôle | Changements autorisés |
| --- | --- |
| Quality | Inspection de colis ; blocage/libération d’ordres. |
| Logistics | Rechargement ; annulation ; départ et arrivée manuelle de transport. |
| Maintenance | Équipements ; réglages, réessais et réconciliation PLC ; acquittement d’alarmes. |
| Supervisor | Toutes les commandes, dont les pas de démonstration. |

Les rôles se cumulent. `Access:Roles` affecte des utilisateurs/groupes existants sans les créer. Avec `UseRoles=true`, les anciennes listes ne donnent aucun droit supplémentaire. L’acquittement utilise `equipment.control`, y compris l’autorisation legacy lorsqu’elle est conservée. Un compte sans permissions consulte seulement. La perte de droits désactive le poste, sans arrêter un runtime indépendant.

`Plant:Mode=Demo` est le défaut. `Commissioning` exige `Database:Path` et `Plant:Id` explicites, refuse un runtime activé, initialise le schéma sans données de démonstration et refuse les requêtes de modification. Les permissions renvoyées sont vides. `Live` et les modes inconnus sont refusés : le pilotage physique n’est pas implémenté. Utiliser une base et un profil distincts, examinés pour la mise en service.

## 6. Contrat API

JSON renvoie les enums en texte. Le contrat d’instantané est en version 1, complété par les champs facultatifs d’alarmes et de mode. Réponses usuelles : 400 entrée invalide, 401 non authentifié, 403 non autorisé/consultation, 404 référence inconnue, 409 état périmé, mauvaise installation ou opération bloquée.

| Endpoint de lecture | Objet |
| --- | --- |
| /api/client-state | Instantané physique, droits, runtime, alarmes et mode. |
| /api/access; /api/runtime; /api/backups/status | Droits effectifs, diagnostic runtime, résultat de sauvegarde. |
| /api/orders; /api/orders/{id}; /api/orders/{id}/history | Ordres et historique ; ID numérique pour ces routes d’élément. |
| /api/orders/by-number/{number}; /api/transports; /api/load-plan | Recherche par numéro, mouvements et propositions calculées. |
| /api/plant; /api/schedule; /api/plc | Atelier, candidats de l’ordonnanceur et échanges durables. |
| /api/packages/{number}/label | Texte d’étiquette approuvée. |
| /api/heats/{heatNumber}/genealogy; /api/shift-summary | Généalogie matière et indicateurs d’équipe. |

Commandes POST : `/api/equipment/{id}/control`, `/api/packages/{id}/inspection`, `/api/baskets/{id}/reload`, `/api/orders/{id}/state` (numéro d’ordre), `/api/transports/dispatch`, `/api/transports/{id}/manual-arrival`, `/api/plc/simulation/settings`, `/api/plc/commands/{id}/retry`, `/api/plc/commands/{id}/manual-arrival`, `/api/simulation/step`, `/api/alarms/{id}/acknowledge`. Rôle, version/révision et identité d’installation sont contrôlés ; les motifs sont obligatoires pour les opérations qui les exigent. Les records de requête Core/API définissent les payloads exacts. Swagger est actif uniquement en Development.

## 7. Installation et configuration

Compilation : Windows et SDK .NET 8. Les paquets `win-x64 --self-contained true` incluent le runtime. Le service installé est `TransportOrders.Runtime`, normalement sous `NT AUTHORITY\LocalService`, avec démarrage automatique différé et relances de récupération configurées. Modifier le service exige des droits Windows élevés.

Utiliser PowerShell 7 pour `Install-WindowsService.ps1` : son appel à `Path.IsPathFullyQualified` n’existe pas dans Windows PowerShell 5.1. Les scripts de mise à jour/diagnostic testés peuvent utiliser Windows PowerShell 5.1. Pour une démonstration neuve, créer le dossier de base choisi, lancer une fois l’API avec `--Database:Path` explicite puis l’arrêter par Ctrl+C après initialisation. Ne pas appliquer cette initialisation Demo à une base opérationnelle non examinée.

```powershell
dotnet build TransportOrders.sln
dotnet test TransportOrders.sln
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained true -o artifacts/recipes-api
dotnet publish src/TransportOrders.App -c Release -r win-x64 --self-contained true -o artifacts/recipes-client
```

Pour un service existant, utiliser `scripts/Deploy-EquipmentApi.ps1` avec compte, rôle et chemin publié. Le script conserve la configuration et tente de revenir à l’ancien exécutable si le contrôle de disponibilité échoue. Il ne rétablit pas les migrations de base. Faire une sauvegarde vérifiée avant mise à jour. L’installation initiale exige une base initialisée existante ; examiner le chemin avant `Install-WindowsService.ps1`.

```powershell
# Console élevée ; remplacer le compte exemple par l'identité Windows prévue.
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\recipes-api'
.\scripts\Check-PlantBackup.ps1
```

| Paramètre | Objet |
| --- | --- |
| Database:Path | Chemin serveur absolu explicite pour le service Windows. |
| Plant:Id / Name / Mode / TimeZone | Identité stable, nom, mode et fuseau des équipes. |
| Runtime:Enabled / LogDirectory | Runtime simulé et journaux serveur. |
| Access:UseRoles / Roles | Affectations utilisateurs/groupes Windows. |
| Backup:Enabled / Directory / IntervalHours | Sauvegarde au démarrage puis périodique ; 1-168 heures, défaut 24. |
| Kestrel:Endpoints | HTTP local, HTTPS distant et certificat. |

Client : `TRANSPORTORDERS_SERVICE_URL`, défaut `http://localhost:5080`, et `TRANSPORTORDERS_PLANT_ID` facultatif pour fixer le site avant le premier contact. Une adresse distante exige HTTPS. Le certificat doit être reconnu et correspondre au nom serveur ; LocalService doit lire sa seule clé privée. L’exemple utilise DORNBIRNTATA:5443, avec règle réseau limitée au profil Privé/sous-réseau local. Aucun changement de profil réseau n’est automatique. Un second poste nécessite confiance du certificat, authentification Windows opérationnelle et essai de connexion.

## 8. Stockage, alarmes, sauvegarde et reprise

La base installée configurée est `C:\ProgramData\TransportOrders\extrusion.db`. Les chemins de repli de développement viennent de `DatabasePaths`, pas de WPF. Le client conserve langue et journaux dans `%LOCALAPPDATA%\TransportOrders.Client`. Les journaux serveur sont quotidiens, avec 14 fichiers conservés par défaut. Protéger base, configuration, binaires, clés privées et sauvegardes par les droits Windows.

Une condition active ne crée pas d’alarmes en doublon à chaque lecture. Acquitter trace acteur/commentaire sans effacer le défaut. Disparition et récidive donnent des événements/occurrences distincts. Les erreurs runtime/sauvegarde dépendent du diagnostic de leurs workers. WPF indique localement un service inaccessible ; ce service ne peut enregistrer sa propre panne dans une base inaccessible.

La sauvegarde SQLite à chaud inclut les données WAL validées et produit `.db` et manifeste `.db.json`. Contrôles : SHA-256, taille, site, intégrité SQLite, clés étrangères et séquence de migrations supportée. Elle s’exécute au démarrage puis à intervalle configuré, sans suppression automatique des anciennes copies. Copier vers un stockage distinct pour couvrir une perte du disque.

`Backup-Plant.ps1` propose backup, verify et `restore-copy`. La restauration exige une destination inutilisée sans journaux SQLite ; elle ne remplace pas la base active. Examiner la copie avec runtime désactivé et rapprocher les positions physiques avant son utilisation. La compatibilité historique n’est pas universelle : certaines anciennes migrations réinitialisent la démonstration et les bases `EnsureCreated` ont un traitement spécifique. Une base opérationnelle legacy quelconque n’est pas un import pris en charge.

## 9. Vérification et maintenance

Exécuter les tests, `Check-PlantBackup.ps1` et `Test-RuntimeRecovery.ps1`. Ce dernier utilise sa propre base et le port 5099, interrompt les processus et vérifie une tentative et une application d’arrivée. Il n’arrête pas le service installé. `New-CommissioningProfile.ps1` prépare une base vide en consultation sur localhost:5096.

Diagnostic : état/chemin du service, `/api/runtime` et `/api/backups/status` authentifiés, journaux, `PlantId`, expiration du certificat et rôles effectifs. L’erreur 1053 impose de contrôler démarrage, configuration, accès exécutable/base et journaux ; augmenter un délai ne remplace pas ce diagnostic.

Entrées du code : `Api/Program.cs`, `PlantRuntimeWorker`, `PlantAlarmWorker`, `PlantBackupWorker`, Core `PlantCommands`, `PlcExchange`, `ProductionSchedule`, Data `AppDbContext`, repositories, `DatabaseBackup`, App `PlantConnection` et view models. L’index documentaire mène aux guides spécialisés. Recette PLC physique, recettes approuvées, ERP, imprimantes et essais prolongés sur site restent des travaux distincts.
