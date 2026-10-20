# Toutes les actions opérateur passent par le service

WPF envoie désormais les contrôles qualité, le rechargement des paniers, les blocages/libérations/annulations d'ordres, les départs manuels, les confirmations d'arrivée, les réglages du simulateur PLC et les réessais au service central. Les boutons de simulation passent aussi par l'API ; si le service pilote déjà automatiquement, ils indiquent que le service possède le pilotage.

WPF ne fait aucune mutation, lecture, migration ni initialisation SQLite. Il reçoit `/api/client-state` et transmet les commandes au service. Le calcul local des propositions utilise les objets reçus par l'API. Voir la [documentation consolidée](handbook/README.md) et le [guide réseau](network-clients.fr.md).

## Identité et permission

Les requêtes utilisent la même authentification Windows que les [commandes d'équipements](equipment-api.fr.md). Le nom et l'heure d'audit sont fixés par le service. Un nom d'utilisateur dans le JSON n'est pas une identité authentifiée.

La nouvelle permission est explicite et indépendante :

```json
{
  "OperatorCommands": {
    "AllowedOperators": ["DORNBIRNTATA\\moham"]
  }
}
```

Sans cette liste, les nouvelles commandes sont refusées, même si le compte possède déjà la permission des équipements. Les catégories métier partagent pour l'instant une permission opérateur ; des rôles qualité, logistique et maintenance pourront la découper ultérieurement. Les listes sont lues au démarrage du service.

## Commandes disponibles

| Route POST | Action |
|---|---|
| `/api/packages/{id}/inspection` | Contrôle visuel et de quantité, rejet ou approbation motivés |
| `/api/baskets/{id}/reload` | Nouvelle charge d'un panier vide à la presse, avec création de l'ordre suivant |
| `/api/orders/{id}/state` | Blocage, libération ou annulation d'un ordre |
| `/api/transports/dispatch` | Départ d'une sélection d'ordres avec véhicule autorisé et justification éventuelle de charge partielle |
| `/api/transports/{id}/manual-arrival` | Confirmation opérateur d'une arrivée physiquement vérifiée, pour tous les membres du transport |
| `/api/plc/simulation/settings` | Connexion et suppression des réponses du simulateur |
| `/api/plc/commands/{id}/retry` | Réessai motivé de la même commande PLC après timeout |
| `/api/plc/commands/{id}/manual-arrival` | Réconciliation motivée après vérification physique |
| `/api/simulation/step` | Pas manuel du procédé simulé, lorsque le pilote automatique n'est pas actif |

`{id}` désigne le numéro métier pour les colis, paniers, ordres et transports ; c'est le GUID de commande pour les routes PLC. Chaque requête inclut `expectedDatabasePath` pour refuser une station configurée sur une autre base.

## Fraîcheur, transactions et erreurs

Les colis, paniers, réglages et commandes PLC utilisent leur GUID de version existant. Les ordres disposent d'une révision calculée à partir de leur numéro, statut, appartenance au transport, numéro de charge et historique. `GET /api/orders` expose cette révision. Un départ ou une arrivée transmet les numéros et révisions de chaque ordre ; le service recharge les entités et vérifie la sélection complète. Les dimensions du véhicule sont issues de la flotte serveur, pas de chiffres transmis par le poste.

Une porte séquentielle partagée sérialise les mutations opérateur et les cycles de l'ordonnanceur **dans le processus central**. Les dépôts gardent leurs contrôles de concurrence et leurs transactions pour les changements physiques, les réservations, les commandes PLC et les historiques. Cette porte n'est pas un verrou distribué : exploiter un seul service central, sans anciens postes encore capables d'écrire directement dans la base.

Les réponses sont `401` pour une requête anonyme, `403` pour un compte non autorisé, `400` pour une requête invalide, `404` pour un objet absent et `409` pour une version périmée ou une règle métier bloquante. Une approbation qualité obsolète ne peut pas décharger une deuxième fois un panier ; un rechargement obsolète ne peut pas créer une deuxième charge ; un réessai conserve le GUID et le contenu de la commande PLC et sa limite d'essais.

Une confirmation manuelle reste une déclaration opérateur après vérification physique. Elle ne prétend pas être un acquittement machine. Les pannes peuvent différer son application ; l'opérateur doit restaurer les équipements concernés. Le dialogue WPF demande confirmation avant l'envoi.

WPF conserve les choix et leur version initiale pendant les actualisations automatiques. Après un conflit, utiliser **Refresh operator data / Bedienerdaten aktualisieren** ou F5, vérifier le nouvel état et prendre une nouvelle décision. La remise à jour explicite abandonne les motifs qualité/PLC en cours ; les contrôles d'équipements disposent aussi de leur propre bouton de remise à jour.

Une erreur réseau peut survenir après une action déjà enregistrée. Le client ne fait aucun renvoi automatique et aucune écriture locale de secours : consulter l'état et l'historique avant de réessayer. Il n'existe pas encore de reçu durable de commande opérateur interrogeable par identifiant. Les acquittements PLC gardent leurs mécanismes durables existants.

## Mise à jour de cette installation

Publier dans un nouveau dossier évite de remplacer des DLL utilisées par le service actif :

```powershell
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained false -o artifacts/operator-api
```

Dans PowerShell administrateur :

```powershell
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -PublishPath '.\artifacts\operator-api'
```

Le script reprend la configuration du paquet actuellement installé, conserve les comptes autorisés existants, ajoute ce compte aux deux permissions, arrête le service, change son exécutable et le relance. Il tente un retour à l'ancien paquet si le nouveau démarrage échoue. La base ProgramData reste utilisée. Relancer WPF après recompilation.

## Limites actuelles

Le procédé et les contrôles qualité automatiques restent simulés. Le service de démonstration peut approuver automatiquement un colis dans son cycle ; cette étape ne remplace pas la future validation humaine d'une installation réelle. Le serveur reste local, sans nouveau port exposé au réseau. Aucun transfert vers un automate réel n'a été ajouté.

Les tests couvrent les permissions de chaque route, les conflits simultanés sur les ordres, les rejets/réinspections qualité, la réutilisation unique d'un panier, les départs et arrivées cohérents, les réservations et réessais PLC, les requêtes HTTP de chaque client et l'absence de mutation locale en cas d'échec.
