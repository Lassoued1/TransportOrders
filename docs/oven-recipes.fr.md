# Recettes de fours en simulation

L’onglet **Oven recipes / Ofenrezepte** permet au rôle **Supervisor** de créer, modifier, valider et archiver les recettes. Aucun paramètre de ce démonstrateur ne constitue une recette industrielle approuvée.

1. Cliquer **New recipe / Neues Rezept**. Donner un identifiant unique (1 à 100 lettres ASCII, chiffres, points, tirets ou underscores), un alliage à quatre chiffres et une température supérieure à 20 °C et inférieure ou égale à 500 °C.
2. Donner des durées entières positives de chauffage, maintien et refroidissement en minutes simulées. La somme doit tenir dans un entier 32 bits.
3. Saisir un motif, puis **Save draft / Entwurf speichern**. Le brouillon ne peut pas démarrer de cycle.
4. Examiner les paramètres enregistrés, saisir un motif de contrôle et **Validate for simulation / Für Simulation validieren**. Il peut alors être utilisé pour un chargement compatible.
5. Pour modifier : sélectionner la recette, modifier les valeurs, saisir un motif et enregistrer. Une nouvelle révision en brouillon est créée et demande une nouvelle validation.
6. Pour retirer une recette : charger ses paramètres enregistrés, saisir un motif et **Archive recipe / Rezept archivieren**. L’archivage conserve le dossier et empêche de nouvelles utilisations.

L’identifiant existant ne change pas. Les cycles en cours conservent les paramètres et la révision copiés au démarrage, même si la recette est modifiée ou archivée. Le panneau de four affiche cette révision. Chaque événement conserve heure, compte Windows, motif, état et paramètres complets.

Les rafraîchissements ne remplacent pas la saisie en cours. Si un autre opérateur change la recette, une commande avec une ancienne version reçoit un conflit : utiliser **Reload selected / Ausgewähltes Rezept neu laden**, examiner les changements puis réessayer. Enregistrer ou recharger les paramètres modifiés avant de valider ou archiver.

## Mise à jour de cette installation

Faire une sauvegarde vérifiée, puis exécuter dans PowerShell administrateur à la racine du projet :

```powershell
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\recipes-api'
```

Fermer l’ancien client puis lancer `scripts/Start-NetworkClient.ps1`. Le paquet client est `artifacts/recipes-client`. La migration conserve les recettes existantes comme révision 1 validée et importe une seule fois leur état de référence. Le profil Commissioning reste en consultation uniquement.
