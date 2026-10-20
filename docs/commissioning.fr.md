# Passage à une installation réelle

Le mode actuel `Demo` utilise des données et un PLC simulés. Il ne représente pas une connexion à une installation physique.

Le nouveau mode `Plant:Mode = Commissioning` prépare un service de consultation : migrations de schéma uniquement, aucune création des douze ordres de démonstration, aucun modèle d’atelier inventé, aucune recette de démonstration et aucun scheduler simulé. Toutes les requêtes de modification sont refusées, même pour Supervisor. Les consultations Windows, les sauvegardes et la supervision des alarmes restent disponibles.

Créer un profil séparé, sans modifier le service actuel :

```powershell
.\scripts\New-CommissioningProfile.ps1
.\artifacts\alarms-api\TransportOrders.Api.exe --environment Production --contentRoot C:\MeineProjekte\TransportOrders\artifacts\commissioning
```

Le profil utilise une base neuve et un nouvel identifiant d’installation. Il écoute uniquement sur `http://localhost:5096`. Un profil Commissioning avec `Runtime:Enabled = true` est refusé au démarrage. La valeur `Live` est également refusée : le driver et les procédures de mise en service doivent être implémentés et vérifiés avant de permettre le pilotage réel.

Le fichier `artifacts\commissioning\site-requirements.json` est un dossier de préparation, pas un import de données ni un driver déjà opérationnel. Les listes sont laissées vides pour recevoir les informations réelles du site.

Pour terminer le raccordement, fournir :

- Automates : constructeur, modèle, firmware, protocole, adresses et accès de test.
- Signaux : correspondance des commandes, acquittements, identifiants, positions, défauts et interverrouillages ; séquences et délais attendus.
- Atelier : noms et capacités des stations, paniers, navettes et destinations autorisées.
- Fours : températures, durées, tolérances, mesures disponibles et règles de validation du traitement.
- Qualité et emballage : critères, quantités, données d’étiquette, imprimante et lecteur de codes.
- ERP : produit, format/API, identifiants et exemple d’ordre réel anonymisé.
- Recette : scénarios et responsables autorisés à valider le raccordement et les mouvements.

Les premiers essais doivent lire les états et rapprocher les positions physiques avec les données du système. Les essais de commandes, de coupures, de réponses tardives et de reprise viennent ensuite sur le matériel prévu pour la recette. Les procédures et résultats sont à consigner avant l’activation de l’exploitation.
