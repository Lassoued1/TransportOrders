# Rôles opérateur

Le service répartit les commandes selon quatre rôles. La supervision possède toutes les permissions ; les autres rôles se cumulent lorsqu'un compte appartient à plusieurs affectations.

| Action | Qualité | Logistique | Maintenance | Supervision |
|---|---|---|---|---|
| Inspection et approbation/rejet qualité | Oui | — | — | Oui |
| Blocage et libération d'ordres | Oui | — | — | Oui |
| Annulation d'ordres | — | Oui | — | Oui |
| Rechargement de paniers | — | Oui | — | Oui |
| Départ et confirmation physique de transports | — | Oui | — | Oui |
| Modes et états des équipements | — | — | Oui | Oui |
| Réessais, réglages et réconciliation PLC | — | — | Oui | Oui |
| Pas de simulation manuel | — | — | — | Oui |

Les consultations conservent leur fonctionnement existant. Un utilisateur Windows authentifié sans rôle reçoit une liste de permissions vide et utilise le poste en consultation. Les règles de version, les motifs et les contraintes physiques s'appliquent après l'autorisation ; un rôle ne permet pas de les contourner.

## Affectations

Dans `appsettings.Production.json` du paquet du service :

```json
{
  "Access": {
    "UseRoles": true,
    "Roles": {
      "Supervisor": { "Users": ["DORNBIRNTATA\\moham"], "Groups": [] },
      "Quality": { "Users": [], "Groups": ["DORNBIRNTATA\\TransportOrders-Quality"] },
      "Logistics": { "Users": [], "Groups": [] },
      "Maintenance": { "Users": [], "Groups": [] }
    }
  }
}
```

`Users` contient les identités Windows effectivement authentifiées ; `Groups` contient des groupes Windows existants vérifiés via le principal authentifié. Aucun groupe n'est créé par cette configuration. Aucun nom d'utilisateur ou rôle envoyé dans le JSON d'une commande ne donne un droit. Les clés autorisées sont `Quality`, `Logistics`, `Maintenance`, `Supervisor` ; une clé inconnue fait échouer le démarrage afin d'éviter une configuration ambiguë.

Redémarrer le service après modification des affectations. Un changement d'appartenance à un groupe Windows peut également nécessiter une nouvelle session/authentification Windows. La configuration et les binaires du service doivent être administrés par des comptes de confiance. Il n'existe pas d'endpoint permettant à un opérateur de s'attribuer lui-même un rôle.

Avec `Access:UseRoles=true`, les anciennes listes `EquipmentControl:AllowedOperators` et `OperatorCommands:AllowedOperators` ne donnent plus de droits. Une configuration de rôles non vide active aussi ce fonctionnement. Laisser `UseRoles=true` permet de retirer toutes les affectations sans réactiver les anciennes listes. Pour compatibilité avec les installations précédentes uniquement, l'absence du mode et des rôles conserve les deux anciennes listes explicites.

## Poste opérateur

`GET /api/access` exige une authentification Windows et renvoie le compte, ses rôles, ses permissions effectives et le chemin serveur historique. Le client WPF actuel reçoit ses droits dans `/api/client-state` et vérifie `PlantId` ; il ne compare plus un chemin SQLite local. Le compte et les rôles sont visibles dans l'en-tête EN/DE ; les commandes non autorisées sont désactivées.

Si le service ou les droits deviennent indisponibles, les actions sont désactivées et le mode de simulation WPF s'arrête. Le pilote automatique du service continue son fonctionnement propre ; la révocation d'un poste ne constitue pas un arrêt de procédé. Le serveur vérifie les permissions à chaque requête, même si un ancien poste ou un appel HTTP direct tente une commande.

Les refus d'accès sont consignés dans les journaux du service avec l'identité, le chemin et le type de requête. Les mutations autorisées conservent l'historique métier existant. Les journaux de service ont actuellement une rétention de 14 fichiers quotidiens.

## Mise à jour

```powershell
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained false -o artifacts/roles-api
```

Puis, dans PowerShell administrateur :

```powershell
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\roles-api'
```

Le script conserve les affectations existantes, active les rôles et ajoute le compte au rôle demandé. La base ProgramData et le compte LocalService restent utilisés. L'application WPF doit être relancée après recompilation.

Cette gestion protège les commandes API. WPF n'accède plus directement à SQLite ; les permissions Windows du serveur restent nécessaires pour protéger base, binaires, sauvegardes et configuration. L'édition des affectations reste une opération d'administration ; aucune interface permettant de gérer les utilisateurs n'a été ajoutée. Consulter la [documentation consolidée](handbook/README.md) pour le paquet actuel.
