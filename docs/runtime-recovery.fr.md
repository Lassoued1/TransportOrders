# Recette de reprise après interruption

Le script `scripts/Test-RuntimeRecovery.ps1` lance le paquet API dans plusieurs processus indépendants, avec une base SQLite neuve dans `artifacts/recovery-<identifiant>`. Il n’arrête ni ne configure le service Windows installé.

Depuis Windows PowerShell 5.1, sans droits administrateur :

```powershell
Set-Location C:\MeineProjekte\TransportOrders
.\scripts\Test-RuntimeRecovery.ps1
```

Le port local 5099 doit être libre. Le compte Windows courant reçoit le rôle Supervisor uniquement dans cette installation de test. La base, les journaux et `result.json` restent disponibles pour examiner le résultat.

Le scénario initialise une commande de transport, masque les réponses du simulateur PLC, puis interrompt brutalement le processus après l’envoi. Au redémarrage, il vérifie la conservation de l’identifiant, du contenu et de la tentative enregistrée. Après rétablissement des réponses, il vérifie l’arrivée et l’état `Applied`, avec une seule tentative et une seule transition de l’ordre vers `Done`. Une nouvelle interruption et un nouveau démarrage doivent conserver l’arrivée sans la rejouer.

Recette exécutée le 12 octobre 2026 : **réussie**, trois interruptions de processus, une tentative d’envoi, un ordre récupéré et une seule application de l’arrivée. Rapport : `artifacts/recovery-fa375a7f309b4aac88a9cf79d16eeea3/result.json`.

Cette recette complète les tests existants de délais, réessais, doublons, réponses tardives, transactions échouées et réservations persistantes. Elle utilise le simulateur PLC persistant. Les coupures d’alimentation réelles, la reprise du service par Windows après panne et le protocole d’un automate physique restent des recettes distinctes à effectuer sur le matériel cible.
