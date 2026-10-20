# Démonstrateur de flux pour l'extrusion aluminium

Le premier onglet affiche désormais le synoptique du flux :

**Presses → fours de vieillissement → emballage → expédition.**

Deux presses alimentent deux fours de deux places chacun. Deux postes d'emballage disposent d'une place
chacun ; le tampon d'expédition accepte douze colis (ou paniers historiques). Ces capacités et les dimensions des équipements
sont des valeurs de démonstration.

## Essai guidé

1. Démarrer avec `dotnet run --project src/TransportOrders.App`.
2. Dans **Plant overview / Anlagenübersicht**, examiner les paniers aux presses, les deux paniers déjà
   dans le four 1, et le panier en mouvement vers le poste d'emballage 1.
3. Appuyer sur **Next step / Nächster Schritt**. Le shuttle confirme son arrivée : la réservation du poste
   devient une occupation. Les étapes suivantes remplissent les fours avec des paniers compatibles,
   démarrent les cycles et avancent la chauffe, le maintien puis le refroidissement avant le déchargement.
4. **Run demo / Demo starten** enchaîne un événement toutes les deux secondes. Les mises à jour se voient
   dans les cartes des stations, le tableau des paniers et le journal des mouvements.
5. Pendant un transport, activer **Shuttle fault / Shuttle-Störung**. La simulation se met en pause,
   le panier reste en mouvement et conserve sa réservation. Rétablir le shuttle, puis reprendre.
6. Mettre en pause et ouvrir **Orders & traceability / Aufträge & Rückverfolgung**. Le panier
   `AL-26052-K03` est bloqué pour inspection de surface. Sélectionner son ordre et utiliser **Release**.
7. Reprendre la simulation : dix colis atteignent l'expédition et leurs dix paniers reviennent vides aux
   presses. Libérer le panier en attente qualité permet d'expédier un onzième colis. Le panier dont l'ordre a été
   annulé reste physiquement à la presse. Une annulation en cours de mouvement est désactivée dans cette interface.
8. Pendant un cycle, sélectionner le four dans **Equipment controls / Anlagensteuerung**, choisir
   **Fault / Störung**, saisir un motif et appliquer. Sa progression reste inchangée lors des prochains
   pas. Fermer et rouvrir l'application : le défaut et les minutes déjà effectuées sont conservés.
9. Rétablir **Operational / Betriebsbereit** et **Automatic / Automatik**, puis reprendre la démonstration.
   Le cycle continue à la minute enregistrée. **Manual / Manuell** et **Maintenance / Wartung** suspendent
   également le cycle. Les commandes de contrôle mettent le mode d'exécution de la démonstration en pause.
10. Ouvrir **Packing & basket reuse / Verpackung & Korbrücklauf**. Les colis conservent leur numéro,
    lot, panier d'origine et numéro de chargement. Sélectionner un colis approuvé pour lire son étiquette.
11. Pour essayer un refus qualité, avancer pas à pas jusqu'à **Awaiting inspection**, mettre en pause,
    sélectionner le colis, décocher un contrôle, saisir un motif et enregistrer. Il reste refusé avec son
    panier chargé ; la démonstration ne le libère pas automatiquement. Répéter les deux contrôles après
    correction pour autoriser l'étiquetage et le déchargement.
12. Quand un panier vide est revenu à une presse, le sélectionner dans la zone de rechargement, choisir
    un lot enregistré, une quantité, un poids, une longueur et un four. **Reload basket** crée un nouveau
    chargement et son ordre de transport. Reprendre la démonstration : le nouveau chargement doit suivre
    un nouveau traitement, même si le lot est identique au précédent.
13. Ouvrir **Production schedule / Produktionsplanung** : la première ligne indique l'action du prochain
    pas. Les lignes suivantes sont les autres actions admissibles dans l'état courant. Le second tableau
    précise les attentes : contrôle qualité, défaut, mode manuel, destination pleine ou charge incompatible.
    Faire un pas puis vérifier que le plan s'est recalculé.
14. Ouvrir **PLC exchanges / SPS-Kommunikation** pour couper le canal simulé ou supprimer ses réponses.
    Le transfert conserve sa réservation et son identifiant pendant le timeout. Le [guide des échanges
    automates](plc-exchanges.fr.md) détaille les essais de reprise et la confirmation manuelle auditée.

## Ordonnancement des charges et déplacements

Le même ordonnanceur pur est utilisé par le simulateur, l'onglet opérateur et `GET /api/schedule`.
Consulter le plan ne crée ni ordre ni réservation. Après redémarrage, il est reconstruit à partir des
positions, chargements, cycles et états enregistrés. La sauvegarde transactionnelle vérifie toujours
les règles et la fraîcheur des données au moment de l'exécution.

Les transferts déjà engagés sont acquittés en premier. Les opérations d'emballage, les retours à vide
et l'expédition des colis précèdent les nouveaux chargements, afin de libérer les postes. Les paniers
traités sont dirigés vers un poste d'emballage disponible avant d'alimenter de nouvelles charges.

Pour chaque four, les candidats admis sont regroupés par recette compatible. Les groupes urgents
passent d'abord, puis ceux dont l'échéance de transport est la plus proche. À urgence et échéance
égales, le groupe comportant le plus de paniers compatibles est choisi ; les autres alliages attendent.
Les destinations prévues dans les ordres sont conservées, sans réaffectation automatique à un autre four.

Une charge normale partielle attend un panier compatible admissible. Une charge complète démarre ;
une charge urgente ou avec un ordre ouvert en retard peut démarrer partiellement. Si aucun complément
compatible n'est admissible, la charge normale peut également démarrer. Les paniers bloqués qualité,
les destinations pleines et les équipements indisponibles ne sont pas proposés à l'exécution.

Plusieurs cycles peuvent être actifs, mais un événement ne fait avancer qu'un four d'une minute simulée.
Le cycle ayant effectué le moins de minutes progresse en premier, avec un départage stable par identifiant.
Le défaut ou le mode manuel de la ressource partagée conserve la pause globale de la démonstration.

Le tableau présente des **alternatives admissibles**, pas un calendrier de réservation de tous les
mouvements futurs. La ligne suivante peut changer après un événement. Les échéances affichées sont
celles des ordres de transport ; il n'existe pas encore de date client de fin de fabrication ni de prévision
de durée globale. Une étiquette ou un colis sans échéance de transport propre affiche un tiret.

## Emballage, étiquettes et retour des paniers

Un chargement devient actuellement **un colis unique**. L'emballage passe par les états préparation,
attente de contrôle, refusé ou approuvé, en mouvement, puis arrivé à l'expédition. Les contrôles visuel
et de quantité doivent tous deux réussir. Chaque décision enregistre l'utilisateur, l'heure et le motif.
En mode automatique, ces contrôles sont des événements `QA-SIM`, explicitement simulés.

L'approbation enregistre dans une même transaction le colis, son étiquette, le déchargement du panier
et la libération du poste d'emballage. L'étiquette est un texte avec identifiant unique, lot, charge,
matière, quantité, poids, panier, numéro de chargement et date du contrôle. Elle est visible et copiable
dans l'interface, et disponible par `GET /api/packages/{number}/label`. Avant approbation, cette route
retourne 404. L'intégration d'une imprimante d'étiquettes et les codes-barres graphiques restent à développer.

Le colis se déplace vers l'expédition séparément du panier. Un colis encore au poste conserve sa place,
même si le panier vide est déjà parti. Le retour à vide réserve une place à une presse et utilise la
même ressource de transfert que les mouvements matière et colis. Les réservations et défauts survivent
à la fermeture. Le poids à vide de 0,15 t est une valeur de démonstration ; la longueur physique est conservée.

Le panier garde son identité physique mais change de numéro de chargement lors du rechargement.
Les cycles et colis antérieurs restent associés à leur chargement d'origine. Un lot différent peut être
chargé dans le même panier sans renommer ce dernier. Les quantités de rechargement sont une saisie
de démonstration ; elles ne proviennent pas encore d'un stock matière ou d'un ERP.

## Équipements et cycles de fours

Sept équipements sont persistés : deux sorties de presse, deux fours, deux postes d'emballage et une
ressource de transfert partagée `SHUTTLE-01`. Le pont roulant et le shuttle du planificateur utilisent
encore cette ressource commune. Chaque équipement possède un mode automatique ou manuel, un état
opérationnel, en défaut ou en maintenance, et une opération active. L'affichage distingue disponible
et occupé. Le motif, l'utilisateur et l'heure des changements sont enregistrés dans son journal.

Les recettes `DEMO-6060`, `DEMO-6063` et `DEMO-6082` acceptent uniquement l'alliage correspondant.
Ces réglages sont **des exemples de simulation**, pas des paramètres métallurgiques validés. Un cycle
englobe tout le chargement physique du four, conserve les références paniers/lots et copie les durées
et la consigne de la recette au démarrage. Un changement ultérieur de recette ne modifie pas le cycle.

Chaque événement d'avance ajoute **une minute simulée**. Le mode Run produit un événement toutes les
deux secondes ; un événement de transport n'avance pas les fours. L'ordonnanceur peut garder plusieurs
cycles actifs et répartit les événements d'avance entre les fours. Le temps réel écoulé pendant une fermeture ne compte pas comme traitement.
La température affichée est une interpolation de démonstration, sans sonde réelle ; les échanges PLC
persistants concernent actuellement les transferts, avec un automate simulé.

Le dépôt vérifie les règles, y compris pour le planificateur manuel : aucune entrée incompatible,
aucun chargement pendant un cycle, aucune sortie avant la fin du refroidissement. Une charge terminée
doit être entièrement évacuée avant une nouvelle charge. Un blocage qualité sur un ordre ouvert
empêche le démarrage du cycle. Les ordres de sortie créés par l'ancienne version restent en attente
jusqu'à l'exécution d'un cycle ; leur présence ne bloque pas ce traitement.

En mode manuel, le simulateur n'utilise pas l'équipement concerné. Les transports explicitement
déclenchés par le planificateur manuel restent soumis aux capacités et aux règles de traitement.
Les phases, les minutes simulées et les opérations actives sont persistées ; le démarrage et la fin
du cycle mettent à jour son état et l'équipement dans une même transaction.

## Identité et stockage

Un panier chargé garde son identifiant entre les étapes. Par exemple, `AL-26031-K03` apparaît dans
plusieurs ordres successifs mais une seule fois dans le synoptique. Sa position est enregistrée dans
`Baskets`, indépendamment du dernier ordre. `MaterialLots` conserve son lot matière et `PlantLocations`
décrit les emplacements et leurs capacités. Un panier enregistré sans ordre reste visible.
Un ordre terminé signifie que **ce déplacement** est achevé, pas nécessairement que la fabrication l'est.
Après le four, le simulateur crée un ordre vers l'emballage. Après contrôle, le colis part vers l'expédition
et le panier suit une opération de retour à vide distincte.

Le scénario utilise `%LOCALAPPDATA%\TransportOrders\extrusion.db`. L'ancien `transportorders.db`
est conservé. Les mouvements, les blocages qualité, les réservations, les modes des équipements,
les défauts et les cycles sont persistés. L'exécution automatique de la démonstration reste en pause
après fermeture.

Dans les ordres existants, `HeatNumber` représente la charge matière, `PieceNumber` le panier
chargé et `ParentPieceNumber` la charge de billettes. Ces références sont vérifiées contre le panier
et son lot lors des sauvegardes. `BasketContentSequence` relie l'ordre au chargement courant. Le panier
vide n'a plus de lot courant ; `MaterialLotNumber` dans son entité conserve le dernier lot pour
l'historique, tandis que `CurrentMaterialLotNumber` est nul. Le modèle associe encore un seul lot à
chaque chargement et un seul colis à ce chargement ; le fractionnement et les contenus mixtes restent à développer.

La migration `PersistedPlantModel` ajoute les trois tables sans supprimer les ordres ni leur historique.
Au premier démarrage après migration, un import transactionnel initialise les positions à partir des
ordres et transports existants, y compris les réservations en cours. Les démarrages suivants conservent
les positions enregistrées. Des contenus matière contradictoires dans un même lot bloquent cet import
avec un message explicite, pour éviter une association arbitraire.

La migration additive `EquipmentAndOvenCycles` ajoute les équipements, leur journal, les recettes,
les cycles et les paniers de chaque cycle. Elle conserve les tables et données existantes. Un transfert
en cours reprend l'occupation de la ressource partagée ; aucun traitement historique n'est inventé.

La migration `PackingAndReusableBaskets` ajoute les colis, leur journal, les retours et le journal des
paniers. Les ordres et cycles historiques reçoivent le numéro de chargement 1. Les positions, versions,
cycles et historiques existants sont conservés. Les anciens ordres planifiés d'emballage vers expédition
sont annulés avec un lien vers le colis lors de son approbation ; leur historique reste consultable.
Les paniers déjà expédiés dans une ancienne base restent à leur position historique : aucun colis ou
contrôle qualité n'est inventé rétroactivement.

Le départ et l'arrivée enregistrent dans une même transaction le transport, les ordres, leur historique,
la position et la réservation des paniers. Le dépôt refuse une destination pleine, une source incorrecte,
un second ordre ouvert pour le même panier ou une modification fondée sur un historique périmé.

## Ce que démontre cette version

- Supervision des positions, capacités et réservations persistées indépendamment des ordres.
- Déplacement d'un panier à la fois par le simulateur, avec contrôle des destinations et priorité des urgences.
- Passage par les fours, emballage contrôlé, expédition des colis et retour à vide, avec conservation de la traçabilité.
- Blocage qualité, défauts et modes des équipements, et reprise des cycles à partir des données persistées.
- Consultation des stations, paniers, équipements, recettes, cycles, colis et retours par `GET /api/plant`.

Le planificateur manuel peut regrouper deux paniers sur le pont roulant ; l'envoi vérifie les places
disponibles et l'absence de transfert déjà en cours. Le simulateur traite cet équipement comme une ressource
de transfert partagée. Il ne calcule pas les trajectoires ni les collisions de machines.

## Prochaine étape vers une supervision industrielle

L'ordonnancement est désormais intégré pour les priorités, les charges compatibles et les équipements
disponibles. Les échanges et acquittements des transferts sont désormais persistants via un canal PLC
simulé. L'étape suivante concerne le service central et un adaptateur pour le protocole réel des machines.
Les contrôles d'emballage et les températures restent simulés.
Le pilotage industriel nécessitera des recettes approuvées, des mesures, des contrôles de qualité du
traitement, la validation de reprise après incident et des échanges automates. Les colis fractionnés,
les stocks, les imprimantes, plusieurs cycles en parallèle et plusieurs postes opérateur restent à développer.

Les [sources publiques et leur rapprochement technique](mcvis-technical-alignment.fr.md) expliquent
l'inspiration du projet. Cette application reste un démonstrateur indépendant, sans connexion à MCVIS.
