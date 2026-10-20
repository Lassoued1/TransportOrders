# TransportOrders - Manuel utilisateur

Édition 1.1 | 12 octobre 2026 | Français

## 1. Avant de commencer

TransportOrders suit les paniers d’extrusion aluminium des sorties de presse aux fours de vieillissement et à l’emballage. Les colis approuvés partent au buffer d’expédition ; les paniers vides reviennent pour être réutilisés. Le mode Demo actuel simule équipements, durée des fours et réponses PLC. Aucun automate réel n’est raccordé. Le mode Commissioning est en consultation, sans création de données de démonstration.

Ce manuel correspond au dernier `recipes-client` et à son API. Un service ancien peut ne pas fournir les nouvelles fonctions. L’application propose English et Deutsch. La documentation française n’ajoute pas d’interface française ; les noms anglais et allemands ci-dessous permettent de retrouver les commandes.

Il faut un service central actif et un compte Windows reconnu. Utiliser `scripts/Start-NetworkClient.ps1` pour le serveur HTTPS DORNBIRNTATA configuré, ou l’exécutable avec la connexion fournie par l’administrateur. Un poste distant doit reconnaître le certificat serveur. Il n’a pas besoin d’une copie de la base.

## 2. Démarrage, langue et droits

1. Lancer le client et attendre la première actualisation.
2. Contrôler l’identité Windows, les rôles et l’état du service central affichés.
3. Choisir **English** ou **Deutsch** ; le choix est conservé par utilisateur.
4. Si la connexion est indisponible, les données affichées peuvent être anciennes et les commandes sont désactivées. Après rétablissement, examiner l’état actualisé avant d’agir.

| Rôle | Principales actions |
| --- | --- |
| Quality | Inspection de colis ; blocage et libération d’ordres. |
| Logistics | Rechargement ; annulation ; départ et confirmation d’arrivée. |
| Maintenance | Équipements ; diagnostic/réessai/réconciliation PLC ; acquittement d’alarmes. |
| Supervisor | Toutes les actions, dont la démonstration pas à pas. |
| Sans permissions effectives | Consultation uniquement. |

Un bouton grisé peut indiquer un droit manquant, un état matière incompatible, une action occupée ou Commissioning. Les droits ne contournent pas les contraintes métier. Demander les affectations à l’administrateur ; aucun écran ne permet de s’attribuer soi-même un rôle.

## 3. Navigation et lecture de l’atelier

| Onglet anglais | Onglet allemand | Objet |
| --- | --- | --- |
| Plant overview | Anlagenübersicht | Stations, capacité, paniers, équipements et cycles. |
| Packing & basket reuse | Verpackung & Korbrücklauf | Contrôles de colis, étiquette et rechargement. |
| Production schedule | Produktionsplanung | Prochaine action admissible et motifs de blocage. |
| PLC exchanges | SPS-Kommunikation | États, réessais, délais et historique des commandes. |
| Alarms | Alarme | Occurrences actives/disparues et acquittements. |
| Oven recipes | Ofenrezepte | Brouillons, validation pour simulation, révisions et archivage. |
| Orders & traceability | Aufträge & Rückverfolgung | Ordres, historique, chargements et traçabilité des lots. |

Une destination réservée attend un chargement ; elle ne prouve pas sa présence physique. Un panier en mouvement occupe la ressource de transfert commune. Examiner ensemble identifiant du panier, lot et séquence de chargement : un panier réutilisé contient un nouveau chargement. Le colis conserve l’identité du chargement dont il provient.

## 4. Utiliser la démonstration

Ces commandes concernent Demo. **Next step** exécute une action simulée admissible lorsque le runtime central n’est pas déjà actif. S’il pilote l’automatique, la réponse indique que le service central conduit la démonstration. Fermer ou mettre en pause WPF n’arrête pas ce service indépendant.

Pour une démonstration pilotée par le poste, **Run demo** demande des pas périodiques ; l’arrêter avant des réglages ou contrôles qualité manuels. Pour arrêter le runtime indépendant dans un essai supervisé, demander à l’administrateur. La pause du poste n’est pas un arrêt machine.

Le trajet exemple est presse -> four -> emballage. Chauffage, maintien et refroidissement doivent se terminer avant la sortie du four. Les températures simulées ne sont pas des mesures. Dans le jeu initial, les ordres bloqués et annulés ne poursuivent pas normalement le flux. Libérer l’ordre bloqué autorise sa suite ; une annulation est définitive.

## 5. Équipements et fours

1. Dans Plant overview / Anlagenübersicht, sélectionner le bon équipement.
2. Lire mode, état, opération en cours et phase de four.
3. Avec le rôle autorisé, choisir automatique/manuel et opérationnel/défaut/maintenance pour l’essai.
4. Entrer un motif utile et choisir **Apply control / Übernehmen**.
5. Contrôler l’état actualisé et l’historique.

Défaut, maintenance ou manuel peuvent bloquer le procédé automatique. Revenir à opérationnel/automatique après examen. Le cycle survit au redémarrage, sans durée de traitement ajoutée pendant l’arrêt.

Dans Oven recipes / Ofenrezepte, le Supervisor crée une recette avec identifiant immuable, alliage à quatre chiffres, température et durées positives de chauffage, maintien et refroidissement. Saisir un motif et enregistrer le brouillon. Sélectionner le brouillon enregistré, saisir le motif de contrôle et valider pour la simulation. Seules les recettes validées démarrent de nouveaux cycles. Modifier crée une nouvelle révision en brouillon qui doit être revalidée. Enregistrer ou recharger les paramètres modifiés avant validation ou archivage. Une recette archivée reste dans l’historique et ne peut plus être modifiée. Les cycles en cours conservent leur révision et paramètres d’origine. L’historique contient compte Windows, heure, motif et paramètres complets. Un conflit impose de recharger la recette ; les actualisations périodiques préservent les saisies non enregistrées.

Un conflit de version signifie que la sélection a changé au serveur. Actualiser la version sélectionnée, examiner les dernières données et reformuler l’action voulue. Ne pas répéter Apply pour forcer une ancienne demande.

## 6. Qualité, emballage et réutilisation

Avant un contrôle manuel, arrêter les demandes automatiques du poste. Sélectionner un colis en attente de contrôle ou rejeté. Renseigner les contrôles visuel et quantité, saisir le motif et choisir **Record inspection / Prüfung erfassen**. Les deux doivent réussir pour approuver. Un rejet interdit l’approbation de l’étiquette et l’expédition ; effectuer explicitement une nouvelle inspection après correction. La qualité automatique de Demo est simulée, sans valeur de contrôle physique.

Le colis approuvé présente une étiquette textuelle traçable. Il n’y a pas d’imprimante intégrée ni de code-barres graphique généré. Le colis peut poursuivre son expédition indépendamment du retour du panier vide.

Pour recharger, choisir un panier vide revenu à une presse, sélectionner lot et four, puis quantité, poids et longueur. Choisir **Reload basket / Korb neu beladen** et contrôler la nouvelle séquence et le nouvel ordre. Les valeurs sont des entrées de démonstration, sans réservation de stock. Un traitement précédent ne valide jamais ce nouveau chargement.

## 7. Ordres, départs et traçabilité

Utiliser Orders & traceability / Aufträge & Rückverfolgung lorsque les droits l’autorisent. Rechercher ou scanner un numéro dans le champ prévu, puis examiner ordre et historique. La recherche par lot rassemble les mouvements ; la généalogie regroupe les identités matière liées.

Le blocage qualité exige un motif et exclut l’ordre du planning. Libérer après examen qualité. Annuler est définitif. **Plan loads** calcule des propositions sans les faire partir. Examiner trajet, longueur et poids avant départ. Un plein chargement utilise au moins 90 % de la longueur disponible ; un partiel doit avoir un motif autorisé enregistré. Les départs vides ou surchargés sont refusés.

La confirmation d’arrivée concerne le mouvement et ses ordres chargés. Une arrivée manuelle est une réconciliation physique enregistrée. Dans une future installation réelle, confirmer la position selon la procédure approuvée avant cette action. Elle ne prouve pas la réception d’une réponse PLC.

Raccourcis utiles : F5 actualise ; Ctrl+F place le curseur dans la recherche ; Esc dans ce champ la vide. Une actualisation peut révéler une action du runtime ou d’un autre opérateur.

## 8. Diagnostic PLC

Pending : en file ; AwaitingAck : envoyé ; Accepted : accepté sans arrivée prouvée. Completed : fin reçue, éventuellement différée faute d’équipement disponible. Applied : arrivée validée. TimedOut et Rejected demandent un examen.

En Demo, Maintenance peut masquer les réponses ou déconnecter le canal simulé. Le délai de 15 secondes conserve la réservation. Rétablir les réponses permet de tester une réponse tardive. Le réessai est explicite et motivé, avec même identifiant et trois tentatives maximum. Un rejet ne justifie pas une répétition aveugle. La réconciliation manuelle exige un motif et possède sa propre trace.

Sans configuration PLC, l’interface l’indique. Ces réglages concernent le simulateur, pas la connexion à un automate physique.

## 9. Alarmes et acquittement

Sélectionner une occurrence dans Alarms / Alarme. Lire source, condition, état actif/disparu, date et historique. Maintenance ou Supervisor saisit un commentaire puis acquitte. L’action enregistre le compte Windows et son commentaire ; elle ne répare pas le défaut et ne termine pas un transport. L’alarme peut rester active. Une récidive donne une nouvelle occurrence non acquittée.

La version de travail et le commentaire restent conservés pendant les actualisations. En cas de conflit, examiner l’occurrence courante puis utiliser **Refresh selected version / Ausgewählte Version aktualisieren** avant de soumettre l’action revue. L’acquittement est désactivé si le service ne fournit pas les alarmes ou si la connexion est perdue. Les erreurs runtime/sauvegarde peuvent apparaître si leurs fonctions sont activées.

## 10. Incidents, fermeture et relève

| Symptôme | Conduite recommandée |
| --- | --- |
| Connexion indisponible | Contrôler adresse et service avec l’administrateur ; données potentiellement anciennes. |
| Refus d’accès / bouton grisé | Vérifier identité, rôle, état de l’opération et mode Commissioning. |
| Conflit d’état | Actualiser et lire l’historique : une autre action peut avoir changé la sélection. |
| Aucun mouvement | Lire les blocages du planning ; contrôler qualité, capacité, traitement et équipement. |
| Délai PLC dépassé | Conserver les réservations ; examiner liaison et historique avant réessai motivé. |
| Réponse perdue après soumission | Actualiser d’abord : l’action peut être déjà enregistrée. |
| Alarme de sauvegarde | Informer l’administration ; contrôler la dernière sauvegarde avant maintenance. |

Fermer WPF normalement. Le runtime indépendant peut continuer. À la relève, transmettre alarmes non acquittées, matières bloquées/rejetées, commandes PLC en attente et actions manuelles non résolues. La restauration relève de la documentation technique ; ne pas supprimer ou remplacer la base pour réinitialiser l’atelier.
