# Échanges automates et acquittements fiables

Cette version fournit un contrat d'échange et un **automate de démonstration persistant**, sans connexion
à une machine réelle. Le canal couvre les transferts matière, les colis et les retours de paniers vides.
Les recettes de fours et les contrôles d'emballage restent simulés localement.

## Essai dans l'application

1. Ouvrir **PLC exchanges / SPS-Kommunikation**. Le tableau indique la commande, son état, la route,
   les tentatives et l'échéance. L'identifiant de corrélation est consultable et copiable.
2. Avant d'acquitter un transfert, cocher **Suppress acknowledgement delivery**, saisir un motif et
   appliquer. Faire un pas : le simulateur PLC mémorise l'exécution, mais sa réponse n'est pas livrée.
   Le transfert reste réservé et l'arrivée n'est pas appliquée dans l'application.
3. Attendre au moins 15 secondes après la tentative d'envoi, puis faire un pas. Le timeout est enregistré
   et le mode Run s'arrête. Le timeout ne libère pas le panier, le colis ni la destination.
4. Restaurer les réponses et saisir un motif. Faire un pas permet de recevoir la réponse tardive et
   d'appliquer l'arrivée sans nouvel envoi. Pour tester un réenvoi, sélectionner la commande en timeout
   et utiliser **Retry same command** avant le prochain pas : l'identifiant et le contenu sont conservés.
5. Le PLC simulé restitue la même réponse mémorisée ; son enregistrement d'exécution reste unique.
   Au maximum trois tentatives d'envoi sont autorisées, avec un motif pour chaque reprise.
6. Décocher **Demo PLC connected** pour essayer une coupure de connexion. L'application se met en pause,
   garde la commande et ses réservations, et conserve ce réglage après fermeture. Si l'envoi n'a jamais
   atteint le simulateur, une reprise explicite après timeout est nécessaire pour le renvoyer.
7. Après vérification physique d'une arrivée, l'opérateur peut utiliser **Confirm physically verified
   arrival manually** avec un motif et une confirmation. Cette décision est enregistrée comme une
   confirmation humaine, distincte d'une réponse machine. Les règles de chargement et les équipements
   indisponibles continuent de bloquer l'application d'une arrivée incohérente.

La touche **Next step** et le mode Run traitent les échanges ; aucun processus de communication ne
tourne en arrière-plan quand l'application est fermée. Les délais sont conservés et réévalués au prochain
traitement. Le temps de communication est exprimé en secondes réelles, séparément des minutes simulées
des cycles de fours.

## Enregistrement et reprise

```mermaid
flowchart LR
    A[Départ autorisé] --> B[Transaction : réservation + commande]
    B --> C[Tentative persistée avant envoi]
    C --> D[Canal IPlcChannel]
    D --> E[Acquittement validé et conservé]
    E --> F[Transaction : arrivée + historique + commande appliquée]
```

La migration additive `DurablePlcExchange` ajoute les commandes, leur journal, les acquittements reçus,
les exécutions du simulateur PLC et ses réglages. Elle conserve les ordres, positions, lots, cycles et
colis. Les transferts déjà en cours dans l'ancienne démonstration reçoivent une commande en attente,
sans inventer de réponse historique. Avant toute migration d'une installation réelle, ces transferts
doivent être réconciliés avec l'état de la machine ; le comportement d'import actuel vise la démonstration.

Chaque nouvelle réservation et sa commande sortante sont écrites dans la même transaction. L'enveloppe
contient un identifiant stable, une version de contrat, une opération, l'équipement, la source, la destination
et le chargement. Le chargement conserve les identifiants et numéros de chargement, quantités, poids et
longueurs. Sa représentation canonique évite qu'une différence de format décimal change son empreinte.
L'empreinte vérifie la correspondance du contenu ; elle ne constitue pas une authentification du message.

Une tentative est écrite **avant** l'appel au canal. Une interruption juste avant l'envoi peut donc compter
comme une tentative sans exécution machine. Après reprise, l'application recherche une réponse ; elle
ne renvoie pas automatiquement une commande dont l'issue est inconnue. Une reprise conserve le même
identifiant, au lieu de créer un nouveau mouvement.

Un acquittement **Accepted** ne confirme pas l'arrivée. Seul **Completed**, avec identifiant, équipement,
destination, empreinte et séquence concordants, peut conduire à l'application automatique de l'arrivée.
Les messages d'une autre commande, route ou charge sont refusés. Les doublons identiques et séquences
anciennes n'appliquent pas une seconde arrivée ; réutiliser le même identifiant de message avec un contenu
différent est refusé. Un rejet machine conserve les réservations et nécessite une réconciliation opérateur.

La réponse est d'abord conservée, puis l'arrivée est appliquée dans une transaction avec les positions,
réservations, états des ordres/colis/retours, historique et état final de la commande. Si un défaut ou un
changement de données bloque cette application, la réponse reste disponible. Après rétablissement,
l'application peut reprendre sans nouvel envoi. Une confirmation manuelle est auditée séparément ; une
réponse machine tardive à ce mouvement ne modifie plus les positions.

Le PLC simulé conserve une exécution par identifiant de commande, indépendamment de la livraison de sa
réponse. Cela permet de tester la perte d'une réponse et le réenvoi sans double exécution dans ce simulateur.
Une machine réelle doit fournir une garantie équivalente ou une procédure explicite de réconciliation :
le logiciel ne peut garantir à lui seul une exécution physique unique si le contrôleur rejoue les commandes.

## API et suite industrielle

`GET /api/plc` expose les enveloppes, états, échéances, acteurs et journaux, ainsi que les réglages du canal
simulé. L'API reste en lecture seule ; elle ne permet pas d'envoyer des commandes ou d'injecter des acquittements.

`IPlcChannel` sépare l'envoi et la réception de la persistance. Son implémentation réelle devra mapper le
contrat sur le protocole convenu avec les machines, respecter les délais et annulations, valider la source
des messages et conserver l'identité lors des reconnexions. Les appels du service sont limités à 15 secondes ;
un appel arrivé à échéance peut avoir une issue physique inconnue, et les réservations restent conservées.

La suite concerne un service central de traitement, une connexion et un protocole réellement identifiés,
les télémétries et interverrouillages propres aux machines, les droits opérateur et les essais sur installation.
Ce canal applicatif ne remplace pas les fonctions de sécurité des automates.
