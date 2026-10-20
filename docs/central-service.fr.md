# Service central de démonstration

Le processus API peut maintenant héberger le pilote automatique. Il continue à fonctionner lorsque WPF est fermé. L'activation est explicite : l'API conserve son comportement de consultation par défaut.

Depuis la racine du projet, dans PowerShell :

```powershell
dotnet run --project src/TransportOrders.Api -- --Runtime:Enabled=true --urls=http://localhost:5080
```

Laisser ce processus fonctionner. `Ctrl+C` l'arrête proprement. Lancer WPF séparément : il lit l'instantané API et actualise l'affichage environ toutes les deux secondes. Les boutons de simulation indiquent en EN/DE que le service pilote déjà l'installation. Le compte Windows doit posséder les permissions requises pour demander un pas.

Consulter [l'état du service](http://localhost:5080/api/runtime) et [les commandes PLC](http://localhost:5080/api/plc). L'état expose l'activation, l'exécution, le dernier cycle réussi, la dernière action et l'erreur éventuelle. `/health` reste un contrôle HTTP, pas une preuve que le procédé avance.

Le serveur utilise `Database:Path` explicite dans l'installation Windows. En développement, `DatabasePaths` privilégie une base ProgramData existante puis un chemin utilisateur. WPF utilise `TRANSPORTORDERS_SERVICE_URL` et éventuellement `TRANSPORTORDERS_PLANT_ID`, sans ouvrir la base. `TRANSPORTORDERS_DATABASE_PATH` concerne le choix de base serveur, pas le client réseau actuel.

## Fonctionnement

- Un verrou exclusif sur `<base>.driver.lock` empêche deux pilotes automatiques de fonctionner sur la même base. Le système libère le verrou à l'arrêt ou après un crash ; le fichier restant ne signifie pas que le pilote est actif.
- Le service garde le verrou pendant toute sa durée de vie. Un deuxième runtime échoue au démarrage si le pilotage est déjà détenu. WPF transmet ses demandes de pas à l'API et ne possède plus de scheduler SQLite local.
- Chaque cycle traite les commandes persistantes en attente, puis exécute une action de l'ordonnanceur. Les appels sont séquentiels ; une pause de deux secondes suit le cycle. Un appel PLC lent allonge ce délai.
- Les échanges restent interrogés lorsqu'une panne ou un mode manuel bloque l'ordonnanceur. Les délais expirent et les réponses tardives sont enregistrées ; l'arrivée reste différée tant que l'équipement n'est pas disponible.
- Une erreur de cycle est journalisée et visible dans `/api/runtime`. Le cycle suivant repart de la base. Un délai expiré ne déclenche aucun renvoi automatique : la demande de réessai motivée reste une action opérateur.
- Après redémarrage, les commandes, réservations, acquittements et états métier sont relus. Aucun rattrapage des minutes de four pendant l'arrêt n'est inventé.

## Portée actuelle

Ce service héberge **le procédé simulé** : l'automate SQLite, les minutes de four accélérées et les contrôles qualité automatiques de démonstration. Il ne convient pas à piloter une installation réelle avant raccordement et validation des signaux machine et des contrôles humains.

Le service s'installe avec démarrage automatique et compte LocalService. Les lectures et commandes WPF passent par l'API authentifiée, avec rôles et profil HTTPS préparé. Le second poste réseau reste à valider dans l'environnement cible. Le verrou protège le runtime local ; ce système n'est pas un cluster distribué. Voir la [documentation consolidée](handbook/README.md).

Les tests vérifient le fonctionnement du processus hôte sans WPF, la libération du verrou à l'arrêt, l'exclusion du pilote WPF et la reprise d'un acquittement tardif après redémarrage et panne d'équipement.
