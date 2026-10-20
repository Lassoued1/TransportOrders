# Alarmes persistantes

L’onglet **Alarms / Alarme** montre les défauts équipements, la déconnexion du simulateur PLC, les commandes expirées ou rejetées, les erreurs du runtime et les erreurs de sauvegarde. Le moniteur central examine ces conditions toutes les deux secondes, indépendamment de WPF.

Chaque occurrence possède un identifiant et une version. Elle conserve sa date d’apparition, sa disparition éventuelle, l’identité Windows et le commentaire de l’acquittement, ainsi que son historique. Une même condition active ne crée pas de doublons à chaque lecture. Si elle disparaît puis revient, une nouvelle occurrence non acquittée est créée.

**Acquitter ne supprime pas un défaut et ne confirme aucune arrivée PLC.** L’alarme reste active jusqu’à disparition de sa condition. Elle peut aussi être acquittée après sa disparition. Les rôles Maintenance et Supervisor peuvent acquitter ; les autres comptes authentifiés peuvent consulter. Une version périmée ou un mauvais identifiant d’installation est refusé. La disparition de la connexion désactive l’acquittement et signale que l’affichage peut être ancien.

API : les alarmes sont ajoutées à `/api/client-state` ; `POST /api/alarms/{id}/acknowledge` reçoit `ExpectedVersion`, `Reason` et `ExpectedPlantId`. L’identité vient de Windows, jamais du corps de la requête.

En configuration legacy, l’autorisation `equipment.control` autorise également l’acquittement. Si le service n’est pas encore mis à jour pour fournir les alarmes, l’onglet le signale et désactive cette commande.

Le service arrêté ou inaccessible ne peut pas enregistrer lui-même cette panne : WPF conserve son indicateur local de connexion indisponible. Le moniteur ne prétend pas détecter les défauts d’un automate physique tant que son driver et ses signaux ne sont pas raccordés.

La migration `PersistedAlarmHistory` ajoute uniquement `PlantAlarms` et `AlarmEvents`. Elle conserve les paniers, ordres, cycles et échanges PLC existants. Les sauvegardes SQLite incluent ces tables. Le paquet à déployer est `artifacts\alarms-api` et le client est `artifacts\alarms-client`.
