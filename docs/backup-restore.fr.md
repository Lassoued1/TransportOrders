# Sauvegarde et restauration du service central

La sauvegarde utilise `SqliteConnection.BackupDatabase`, avec le service en fonctionnement. Elle prend les données validées, y compris celles encore dans le journal WAL ; une transaction non validée n’est pas incluse. Le fichier produit est autonome, sans WAL à transporter.

Chaque sauvegarde comprend un fichier `.db` et son manifeste `.db.json`, avec l’identifiant `Plant:Id`, la date UTC, la taille, une empreinte SHA-256 et la liste des migrations. La création contrôle l’intégrité SQLite, les clés étrangères et la compatibilité du schéma. La vérification contrôle aussi l’identité de l’installation et l’empreinte du fichier.

## Installation et sauvegardes automatiques

Depuis PowerShell administrateur :

```powershell
Set-Location C:\MeineProjekte\TransportOrders
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\alarms-api'
```

Le déploiement conserve la base, l’identifiant du site, HTTPS et les rôles existants. Il ajoute par défaut :

```json
"Backup": {
  "Enabled": true,
  "IntervalHours": 24,
  "Directory": "C:\\ProgramData\\TransportOrders\\backups"
}
```

Le service sauvegarde au démarrage, puis toutes les 24 heures tant qu’il fonctionne. Un redémarrage produit donc une nouvelle sauvegarde. Les sauvegardes précédentes sont conservées ; aucun nettoyage automatique n’est effectué. L’intervalle est configurable de 1 à 168 heures.

`GET https://DORNBIRNTATA:5443/api/backups/status`, avec authentification Windows, donne `Enabled`, `LastSuccessUtc`, `LastPath` et `LastError`. Un échec de sauvegarde est journalisé et ne bloque pas le fonctionnement de l’installation. Une copie régulière du dossier vers un stockage distinct reste nécessaire pour récupérer après une perte du disque du serveur ; cette étape crée les sauvegardes locales.

Pour contrôler le résultat depuis Windows PowerShell : `scripts\Check-PlantBackup.ps1`.

## Sauvegarde ou vérification manuelle

```powershell
.\scripts\Backup-Plant.ps1 -Destination '.\artifacts\manual-backups'
.\scripts\Backup-Plant.ps1 -Operation verify -BackupPath 'chemin\plant-....db'
```

Le script lit l’adresse de la base et `Plant:Id` dans la configuration du service installé. Il utilise le dernier paquet `artifacts\alarms-api` comme outil de maintenance, qui inclut les sauvegardes et le schéma des alarmes. Les droits sur les fichiers suffisent : les modes de maintenance ne lancent aucun serveur HTTP, n’exécutent aucune migration et ne créent aucune donnée de démonstration.

## Restaurer une copie à examiner

```powershell
New-Item -ItemType Directory -Path '.\artifacts\restore-drill' -Force
.\scripts\Backup-Plant.ps1 -Operation restore-copy -BackupPath 'chemin\plant-....db' -Destination '.\artifacts\restore-drill\extrusion-restored.db'
```

La restauration refuse un fichier de destination existant ou ses journaux SQLite. Elle vérifie la sauvegarde avant de copier, puis contrôle la base restaurée. Elle ne remplace jamais la base du service et ne démarre aucun scheduler.

Avant une remise en production : arrêter le service, conserver une sauvegarde de son état actuel, examiner la copie restaurée avec le runtime désactivé et rapprocher les positions et commandes de l’état physique réel. Le choix d’utiliser cette copie comme nouvelle base est une opération distincte ; ne pas démarrer deux services sur la même installation. Restaurer une ancienne sauvegarde fait perdre les changements survenus depuis sa création.

## Validation

Tests : données validées du WAL conservées pendant une écriture non validée, historique et versions identiques après restauration, réponse PLC tardive appliquée une seule fois sans réémission, refus d’un fichier corrompu, d’un autre site, d’un schéma inconnu et d’une destination existante. Les tests du worker vérifient également le statut authentifié et la poursuite des lectures métier lorsqu’une sauvegarde échoue.

Recette réelle du 12 octobre 2026 : sauvegarde en fonctionnement de 380 928 octets, puis restauration contrôlée dans `artifacts\restore-drill\extrusion-restored.db`. Rapport et chemins : `artifacts\live-backup-result.json`. La base de production n’a pas été remplacée.
