# Postes opérateur via l’API

WPF ne référence plus `TransportOrders.Data` : aucune connexion SQLite, aucun fichier de base à copier sur un poste opérateur. Le service conserve la base et exécute les changements. Le client lit `/api/client-state` toutes les deux secondes avec son identité Windows et transmet les commandes à la même adresse.

L’instantané version 1 contient les paniers, lots, stations, ordres, transports, équipements, recettes, cycles, colis, retours, échanges PLC, historiques et versions de concurrence. Il inclut les droits effectifs, l’état du runtime et le fuseau horaire du site. Le client conserve les versions reçues ; les commandes périmées sont refusées par le serveur.

Chaque installation possède `Plant:Id`, conservé par le script de déploiement. Le client vérifie cet identifiant à chaque lecture et l’envoie dans ses commandes. Un changement d’installation ou une connexion perdue désactive les commandes. Une reconnexion au même site rétablit les droits. Les données encore affichées pendant une panne sont le dernier instantané reçu. Aucune commande n’est renvoyée automatiquement.

## Utilisation locale

Déployer d’abord le nouveau service depuis PowerShell administrateur :

```powershell
Set-Location C:\MeineProjekte\TransportOrders
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\network-api'
```

Puis fermer l’ancien WPF et lancer `C:\MeineProjekte\TransportOrders\artifacts\network-client\TransportOrders.App.exe`. L’adresse par défaut reste `http://localhost:5080`. Les préférences de langue et les logs du client sont dans `%LOCALAPPDATA%\TransportOrders.Client`, indépendamment de la base centrale.

## Préparation d’un poste distant

Le nom choisi pour cette installation est **DORNBIRNTATA** ; l’adresse prévue est `https://DORNBIRNTATA:5443`. Le script administrateur `scripts/Enable-DemoHttps.ps1` crée un certificat de démonstration valable un an, avec les noms DORNBIRNTATA et localhost. Il accorde au service la lecture de sa seule clé privée, exporte le certificat public dans `artifacts/network-https/DORNBIRNTATA.cer` et le fait reconnaître sur le serveur. La clé privée n’est pas exportable.

Le script conserve HTTP localhost pour les outils locaux et active une écoute HTTPS IPv4/IPv6. La règle de pare-feu qu’il prépare autorise uniquement le port 5443, sur profil **Privé**, depuis le sous-réseau local. Le réseau actuel du PC est classé **Public** ; le script ne change pas ce classement et cette règle ne permettra donc pas encore l’accès depuis un autre poste sur ce réseau.

Sur un second poste, copier le certificat public et l’importer dans les autorités racines de confiance avant d’utiliser HTTPS. Le lanceur `scripts/Start-NetworkClient.ps1` sélectionne le nom DORNBIRNTATA et l’identifiant `e4f95b8b-7ec2-4f9e-9477-c1491eccd0a5`. Le certificat de démonstration doit être remplacé par celui de l’entreprise pour une installation durable.

L’accès distant nécessite un endpoint Kestrel HTTPS et un certificat serveur valide pour le nom DNS utilisé, reconnu par les postes clients. Les identités Windows doivent être authentifiables par le serveur. En domaine, faire vérifier par l’administration les comptes, groupes et la configuration Kerberos/SPN pour le nom retenu. Les certificats invalides ne sont jamais acceptés par le client.

Exemple de bloc à ajouter à la configuration du serveur, après remplacement des noms et installation d’un certificat approprié dans `LocalMachine\My` :

```json
"Kestrel": {
  "Endpoints": {
    "Local": { "Url": "http://localhost:5080" },
    "Operators": {
      "Url": "https://*:5443",
      "Certificate": {
        "Subject": "plant-server.example.local",
        "Store": "My",
        "Location": "LocalMachine",
        "AllowInvalid": false
      }
    }
  }
}
```

Accorder à `LocalService` la lecture de la clé privée de ce certificat uniquement, puis autoriser le port HTTPS depuis les seuls postes concernés. Le bloc ci-dessus est un exemple à préparer, pas une configuration déjà activée. Aucun certificat ni règle de pare-feu n’est installé par cette étape.

Sur le poste client, lancer depuis PowerShell :

```powershell
$env:TRANSPORTORDERS_SERVICE_URL = 'https://plant-server.example.local:5443'
$env:TRANSPORTORDERS_PLANT_ID = 'identifiant-Plant-Id-du-serveur'
& 'C:\TransportOrders\TransportOrders.App.exe'
```

`TRANSPORTORDERS_PLANT_ID` est facultatif : sans valeur, le client fixe l’identifiant du premier instantané valide pour sa session. Le définir explicitement vérifie le site dès la première lecture. L’adresse est fixe pendant la session. HTTP est accepté sur loopback seulement ; le serveur refuse aussi les requêtes distantes non chiffrées avant l’authentification Windows.

Les lectures métier nécessitent maintenant une authentification, comme les commandes ; `/health` reste accessible sans identité pour la supervision. Un ancien service sans `/api/client-state` doit être mis à jour avant de lancer ce nouveau client.

Validation : tests de transfert JSON avec historique et PLC persistants, authentification des lectures, modification puis refus d’une révision périmée, mauvais identifiant de site, panne de connexion et contrat non pris en charge. Un vrai accès depuis un second poste et la configuration TLS de l’entreprise restent à valider sur le réseau cible.
