# Commandes d'équipements centralisées

Les commandes Automatique / Manuel / Panne / Maintenance, ainsi que le bouton de panne/rétablissement de la navette, passent désormais de WPF au service par `POST /api/equipment/{id}/control`. WPF ne réalise aucun repli vers une écriture SQLite lorsque cette commande échoue.

## Identité et permissions

L'hôte Kestrel utilise l'[authentification Windows Negotiate documentée par Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-8.0). WPF transmet les identifiants de la session Windows via `UseDefaultCredentials`, sans demander ni conserver de mot de passe. Le service extrait l'identité authentifiée ; un nom d'opérateur fourni dans le JSON n'a aucun effet sur l'historique.

La configuration Production contient une liste explicite :

```json
{
  "EquipmentControl": {
    "AllowedOperators": ["DORNBIRNTATA\\moham"]
  }
}
```

Une liste absente ou vide refuse toutes les commandes. Le serveur répond `401` sans authentification et `403` pour un compte absent de la liste. Les endpoints de consultation gardent leur accès actuel. Il s'agit d'une permission unique de contrôle des équipements, pas encore d'un système complet de rôles métier.

## Contrat et conflits

La requête contient `expectedVersion` (GUID affiché par le poste), `mode`, `condition`, `reason` (1–1000 caractères) et `expectedDatabasePath`. Le chemin de base doit correspondre à la base du service, afin de ne pas commander par erreur une autre installation. Le service fixe l'heure UTC et enregistre l'identité, le motif et les nouveaux contrôles dans l'historique existant.

Deux commandes basées sur la même version ne peuvent pas toutes les deux être appliquées : la première modifie la version ; la seconde reçoit `409`. Le contrôle est vérifié dans le dépôt et par la concurrence EF, ce qui couvre aussi une modification du procédé entre la lecture et l'enregistrement. Une commande incorrecte reçoit `400`, un équipement inconnu `404`.

Le poste garde les choix en cours de saisie et leur version initiale pendant les actualisations automatiques. En cas de conflit, utiliser **Reload controls / Steuerung neu laden** : ce bouton abandonne la saisie en cours, recharge l'état et permet une nouvelle décision explicite.

En cas de coupure ou de réponse perdue, le poste conserve le motif et affiche un message EN/DE. Il ne réessaie pas automatiquement. Une réponse perdue peut suivre une écriture réussie : recharger et vérifier l'historique avant de refaire une commande. Cette version utilise la concurrence de version ; elle n'ajoute pas encore de reçu de commande interrogeable par identifiant.

## Installation existante

Le paquet de cette étape se publie dans `artifacts/equipment-api` :

```powershell
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained false -o artifacts/equipment-api
```

Le script `scripts/Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham'`, dans PowerShell administrateur, reprend la configuration du service installé, ajoute le compte autorisé, accorde la lecture du paquet à LocalService et change le chemin de l'exécutable. Il arrête puis relance le service. Si le nouveau service ne devient pas disponible, il tente de restaurer l'ancien chemin et de relancer l'ancien paquet. La base ProgramData reste la même.

Fermer puis relancer WPF après compilation pour utiliser le client HTTP. Le service reste lié à localhost. Pour des postes sur le réseau, il faudra préparer le transport sécurisé, l'accès aux données et l'architecture des autres commandes avant de modifier cette exposition.

## Vérification

Les tests couvrent identité authentifiée, refus de comptes non autorisés, motifs et enums invalides, base différente, équipement inconnu, deux commandes concurrentes et conservation des choix WPF. Les tests client couvrent aussi la coupure du service, l'absence de repli local et l'absence de renvoi automatique. Un essai sur Kestrel réel vérifie le dialogue Windows Negotiate et les réponses 401 / 200 / 409 sur une base isolée.

Les tests HTTP en mémoire remplacent uniquement le gestionnaire d'authentification dans le projet de tests, car TestServer ne fournit pas les connexions nécessaires à Negotiate. Aucun en-tête `Test-User` n'est accepté comme identité par le serveur de production.

Le contrôle qualité, le rechargement des paniers, les commandes d'ordres et les réessais PLC passent désormais aussi par [les commandes opérateur centralisées](operator-commands.fr.md), avec une permission distincte.
